using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Anomaly.Server.Networking;
using Anomaly.Server.Content;
using Anomaly.Server.Api.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using MEC;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

internal class FileTransferSession
{
    private enum State
    {
        AwaitingConsent,
        AwaitingRequest,
        Sending,
        AwaitingAck,
        Complete,
        Aborted
    }

    private static readonly Dictionary<NetworkConnectionToClient, FileTransferSession> ActiveSessions =
        new Dictionary<NetworkConnectionToClient, FileTransferSession>();
    private static readonly Dictionary<ReferenceHub, AssetPackSnapshot> ApprovedManifests =
        new Dictionary<ReferenceHub, AssetPackSnapshot>();
    private static readonly object BudgetLock = new object();

    private const int MaxActiveTransfers = 8;
    private const long MaxGlobalReservedBytes = 512L * 1024L * 1024L;
    private const int DefaultChunkIntervalMs = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    private static int _reservedTransferCount;
    private static long _globalReservedBytes;

    public static event Action<ReferenceHub> SessionCompleted;

    private readonly NetworkConnectionToClient _connection;
    private readonly Action _completed;
    private readonly Action<string> _failed;
    private bool _disposed;
    private readonly ulong _sessionId;
    private readonly FileManifestMessage _manifestMessage;
    private readonly Dictionary<string, ServerAssetManager.AssetInfo> _manifest;
    private readonly AssetPackSnapshot _assetSnapshot;
    private AssetChunkReader _chunkReader;
    private readonly Queue<ServerAssetManager.AssetInfo> _pendingFiles =
        new Queue<ServerAssetManager.AssetInfo>();

    private State _state = State.AwaitingConsent;
    private ServerAssetManager.AssetInfo _currentFile;
    private bool _hasCurrentFile;
    private int _retryCount;
    private long _reservedBytes;
    private bool _budgetReserved;
    private DateTime _lastActivityUtc;
    private CoroutineHandle _sendHandle;
    private CoroutineHandle _watchdogHandle;

    private FileTransferSession(NetworkConnectionToClient connection, AssetPackSnapshot snapshot, Action completed, Action<string> failed)
    {
        _connection = connection;
        _completed = completed;
        _failed = failed;
        _sessionId = CreateSessionId();
        _assetSnapshot = snapshot;
        _manifestMessage = snapshot.BuildManifest(_sessionId);
        _manifest = snapshot.Files;
        _lastActivityUtc = DateTime.UtcNow;
    }

    internal FileManifestMessage Manifest => _manifestMessage;
    internal AssetPackSnapshot Snapshot => _assetSnapshot;
    internal bool IsComplete => _state == State.Complete && !_disposed;

    internal static FileTransferSession StartForConnection(NetworkConnectionToClient connection,
        AssetPackSnapshot snapshot, Action completed, Action<string> failed)
    {
        CleanupConnection(connection);
        var session = new FileTransferSession(connection, snapshot, completed, failed);
        ActiveSessions.Add(connection, session);
        return session;
    }

    internal bool Approve()
    {
        if (_disposed || _state != State.AwaitingConsent) return false;
        _state = State.AwaitingRequest;
        _lastActivityUtc = DateTime.UtcNow;
        _watchdogHandle = Timing.RunCoroutine(Watchdog());
        return true;
    }

    internal static bool AdoptForPlayer(ReferenceHub hub)
    {
        if (hub?.connectionToClient == null ||
            !ActiveSessions.TryGetValue(hub.connectionToClient, out var session) || !session.IsComplete)
            return false;
        CleanupPlayer(hub);
        ApprovedManifests.Add(hub, session._assetSnapshot.Retain());
        foreach (Delegate subscriber in SessionCompleted?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<ReferenceHub>)subscriber)(hub); }
            catch (Exception exception) { try { Logger.Error("FileTransfer completion observer failed: " + exception); } catch { } }
        }
        return true;
    }

    public static void CleanupPlayer(ReferenceHub hub)
    {
        if (hub == null) return;
        ScriptMessageGate.Remove(hub);
        if (ApprovedManifests.TryGetValue(hub, out var approved))
        {
            ApprovedManifests.Remove(hub);
            approved.Dispose();
        }
    }

    internal static void CleanupConnection(NetworkConnectionToClient connection)
    {
        if (connection == null) return;
        if (ActiveSessions.TryGetValue(connection, out var session))
        {
            ActiveSessions.Remove(connection);
            session.Dispose();
        }
        foreach (var hub in new List<ReferenceHub>(ApprovedManifests.Keys))
            if (ReferenceEquals(hub.connectionToClient, connection)) CleanupPlayer(hub);
    }

    internal static bool IsAssetApproved(ReferenceHub hub, AssetAddress address, AssetKind kind) =>
        hub != null && ContentGrantGate.GetReadiness(hub.netId) == ContentReadiness.Activated &&
        ApprovedManifests.TryGetValue(hub, out AssetPackSnapshot snapshot) && snapshot.Contains(address, kind);

    internal static bool IsAssetApproved(ReferenceHub hub, AssetAddress address, AssetKind kind, byte[] indexHash) =>
        IsAssetApproved(hub, address, kind) && ApprovedManifests[hub].TryGetAsset(address, out var record) &&
        HashesEqual(record.IndexHash, indexHash);

    internal static bool TryGetScriptMessageScope(ReferenceHub hub, string packId, string name,
        AnomalyMessageDirection direction, out ScriptMessageSchema schema, out string indexHash, out ulong activationId)
    {
        schema = null;
        indexHash = null;
        activationId = 0;
        if (hub?.connectionToClient == null || ContentGrantGate.GetReadiness(hub.netId) != ContentReadiness.Activated ||
            !ApprovedManifests.TryGetValue(hub, out var approved) ||
            !ActiveSessions.TryGetValue(hub.connectionToClient, out var session) || !session.IsComplete ||
            !ReferenceEquals(approved, session._assetSnapshot) || !ConnectionContentBootstrap.IsComplete(hub.connectionToClient) ||
            !approved.TryGetScriptSchema(packId, name, direction, out schema, out indexHash))
            return false;
        activationId = session._sessionId;
        return true;
    }

    public static void Reset()
    {
        foreach (var connection in new List<NetworkConnectionToClient>(ActiveSessions.Keys)) CleanupConnection(connection);
        foreach (var hub in new List<ReferenceHub>(ApprovedManifests.Keys)) CleanupPlayer(hub);
        lock (BudgetLock) { _reservedTransferCount = 0; _globalReservedBytes = 0; }
    }

    internal void HandleMessage(IAnomalyMessage message)
    {
        if (_disposed) return;
        switch (message)
        {
            case FileRequestMessage request: HandleFileRequest(request); break;
            case FileAckMessage acknowledgement: HandleFileAck(acknowledgement); break;
            default: EndSession(false, "unsupported file-transfer message"); break;
        }
    }

    internal void SendManifest()
    {
        try
        {
            if (!AnomalyMessaging.TrySendToConnection(_connection, _manifestMessage))
                throw new InvalidOperationException("the authenticated manifest could not be delivered");
            // The connection bootstrap owns the fixed consent/transfer/activation deadline.
            try
            {
                Logger.Info(
                    $"FileTransfer: Sent bounded manifest ({_manifestMessage.Files.Count} files, session={_sessionId:x16}) " +
                    $"to {_connection.connectionId.ToString()}.");
            }
            catch { }
        }
        catch (Exception ex)
        {
            EndSession(false, $"manifest send failed: {ex.Message}");
            throw;
        }
    }

    private void HandleFileRequest(FileRequestMessage request)
    {
        if (_state == State.AwaitingConsent)
        {
            EndSession(false, "file request arrived before matching user consent");
            return;
        }
        if (_state != State.AwaitingRequest)
        {
            EndSession(false, "duplicate or out-of-state file request");
            return;
        }
        if (request.SessionId != _sessionId)
        {
            EndSession(false, "request session id did not match the advertised manifest");
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long requestedBytes = 0;
        foreach (var hash in request.RequestedHashes)
        {
            string hex = ServerAssetManager.ToHex(hash);
            if (!seen.Add(hex))
            {
                EndSession(false, $"request repeated hash {hex}");
                return;
            }
            if (!_manifest.TryGetValue(hex, out var asset))
            {
                EndSession(false, $"request referenced unadvertised hash {hex}");
                return;
            }

            requestedBytes += asset.Size;
            if (requestedBytes > FileManifestMessage.MaxAggregateBytes)
            {
                EndSession(false, "request exceeded the per-client byte budget");
                return;
            }
            _pendingFiles.Enqueue(asset);
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (_pendingFiles.Count == 0)
        {
            EndSession(true, "all advertised files were already cached");
            return;
        }

        if (!TryReserveBudget(requestedBytes))
        {
            EndSession(false, "global transfer budget is currently exhausted");
            return;
        }

        try
        {
            Logger.Info(
                $"FileTransfer: Accepted {_pendingFiles.Count} files ({requestedBytes} bytes, session={_sessionId:x16}) " +
                $"for {_connection.connectionId.ToString()}.");
        }
        catch { }
        SendNextFile();
    }

    private void SendNextFile()
    {
        if (_state == State.Aborted || _state == State.Complete)
            return;

        if (_pendingFiles.Count == 0)
        {
            EndSession(true, "all requested files were verified by the client");
            return;
        }

        _currentFile = _pendingFiles.Dequeue();
        _hasCurrentFile = true;
        _retryCount = 0;
        StartCurrentFile();
    }

    private void StartCurrentFile()
    {
        if (!_hasCurrentFile)
        {
            EndSession(false, "internal transfer state lost the current file");
            return;
        }

        _state = State.Sending;
        _lastActivityUtc = DateTime.UtcNow;
        try
        {
            if (_sendHandle.IsRunning)
                Timing.KillCoroutines(_sendHandle);
            _sendHandle = Timing.RunCoroutine(SendFileChunks(_currentFile));
        }
        catch (Exception exception)
        {
            EndSession(false, "transfer coroutine could not be started safely: " + exception.Message);
        }
    }

    private IEnumerator<float> SendFileChunks(ServerAssetManager.AssetInfo asset)
    {
        if (_disposed || _state != State.Sending) yield break;
        int total = checked((int)((asset.Size + FileChunkMessage.MaxChunkBytes - 1) / FileChunkMessage.MaxChunkBytes));
        if (total <= 0 || total > FileChunkMessage.MaxChunksPerFile) { EndSession(false, "chunk budget exceeded", false); yield break; }
        _chunkReader?.Dispose();
        var reader = _chunkReader = OpenReader(asset);
        if (reader == null) yield break;
        try
        {
            int index = 0;
            while (!reader.IsCompleted)
            {
                if (_disposed || _state != State.Sending) yield break;
                if (!AuthenticatedTransportSessions.IsActiveConnection(_connection)) { EndSession(false, "connection lost", false); yield break; }
                if (!reader.TryRead(out byte[] data)) { yield return Timing.WaitForOneFrame; continue; }
                bool sent = false;
                try { sent = AnomalyMessaging.TrySendToConnection(_connection, new FileChunkMessage { SessionId = _sessionId, Hash = (byte[])asset.Hash.Clone(), ChunkIndex = checked((ushort)index++), TotalChunks = checked((ushort)total), Data = data }); }
                catch (Exception exception) { EndSession(false, "chunk send failed: " + exception.Message, false); }
                if (!sent) { EndSession(false, "chunk send failed", false); yield break; }
                _lastActivityUtc = DateTime.UtcNow;
                yield return Timing.WaitForSeconds(DefaultChunkIntervalMs / 1000f);
            }
            if (_disposed || _state != State.Sending) yield break;
            if (reader.Error != null || index != total) { EndSession(false, reader.Error ?? "incomplete spool read", false); yield break; }
            bool completeSent = false;
            try { completeSent = AnomalyMessaging.TrySendToConnection(_connection, new FileTransferCompleteMessage { SessionId = _sessionId, Hash = (byte[])asset.Hash.Clone() }); }
            catch (Exception exception) { EndSession(false, "completion send failed: " + exception.Message, false); }
            if (!completeSent) { EndSession(false, "completion send failed", false); yield break; }
            _state = State.AwaitingAck;
            _lastActivityUtc = DateTime.UtcNow;
        }
        finally
        {
            reader.Dispose();
            if (ReferenceEquals(_chunkReader, reader)) _chunkReader = null;
        }
    }

    private AssetChunkReader OpenReader(ServerAssetManager.AssetInfo asset)
    {
        try { return new AssetChunkReader(_assetSnapshot, asset); }
        catch (Exception exception) { EndSession(false, "spool reader could not start: " + exception.Message, false); return null; }
    }

    private void HandleFileAck(FileAckMessage ack)
    {
        if (_state != State.AwaitingAck || !_hasCurrentFile)
        {
            EndSession(false, "unsolicited or out-of-state file ACK");
            return;
        }
        if (ack.SessionId != _sessionId || !HashesEqual(ack.Hash, _currentFile.Hash))
        {
            EndSession(false, "ACK did not match the active session and file");
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (ack.Status == 0)
        {
            _hasCurrentFile = false;
            SendNextFile();
            return;
        }

        _retryCount++;
        if (_retryCount > MaxRetries)
        {
            EndSession(false, $"client failed integrity verification for '{_currentFile.RelativePath}' after {MaxRetries} retries");
            return;
        }

        try
        {
            Logger.Warn(
                $"FileTransfer: Matched failure ACK for '{_currentFile.RelativePath}'; " +
                $"retrying {_retryCount}/{MaxRetries}.");
        }
        catch { }
        StartCurrentFile();
    }

    private IEnumerator<float> Watchdog()
    {
        while (_state != State.Complete && _state != State.Aborted)
        {
            yield return Timing.WaitForSeconds(1f);
            if (_state != State.AwaitingConsent && DateTime.UtcNow - _lastActivityUtc > SessionTimeout)
            {
                EndSession(false, $"timed out while {_state.ToString().ToLowerInvariant()}", false);
                yield break;
            }
        }
    }

    private bool TryReserveBudget(long bytes)
    {
        lock (BudgetLock)
        {
            if (_reservedTransferCount >= MaxActiveTransfers ||
                bytes < 0 ||
                _globalReservedBytes + bytes > MaxGlobalReservedBytes)
                return false;

            _reservedTransferCount++;
            _globalReservedBytes += bytes;
            _reservedBytes = bytes;
            _budgetReserved = true;
            return true;
        }
    }

    private void ReleaseBudget()
    {
        if (!_budgetReserved)
            return;

        lock (BudgetLock)
        {
            _reservedTransferCount = Math.Max(0, _reservedTransferCount - 1);
            _globalReservedBytes = Math.Max(0, _globalReservedBytes - _reservedBytes);
            _reservedBytes = 0;
            _budgetReserved = false;
        }
    }

    private void EndSession(bool completed, string reason, bool stopSend = true)
    {
        if (_state == State.Complete || _state == State.Aborted || _disposed) return;
        _state = completed ? State.Complete : State.Aborted;
        ReleaseBudget();
        _chunkReader?.Dispose();
        _chunkReader = null;
        if (stopSend) StopCoroutineBestEffort(_sendHandle, "send");
        StopCoroutineBestEffort(_watchdogHandle, "watchdog");
        if (completed) _completed();
        else _failed(reason);
    }

    private void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state = State.Aborted;
        ReleaseBudget();
        _chunkReader?.Dispose();
        _chunkReader = null;
        StopCoroutineBestEffort(_sendHandle, "send");
        StopCoroutineBestEffort(_watchdogHandle, "watchdog");
        _assetSnapshot.Dispose();
    }

    private void StopCoroutineBestEffort(CoroutineHandle handle, string name)
    {
        if (!handle.IsRunning)
            return;
        try { Timing.KillCoroutines(handle); }
        catch (Exception exception)
        {
            try
            {
                Logger.Error(
                    $"FileTransfer: Failed to stop {name} coroutine for session {_sessionId:x16}: {exception}");
            }
            catch { }
        }
    }

    private static ulong CreateSessionId()
    {
        var bytes = new byte[sizeof(ulong)];
        using (var rng = RandomNumberGenerator.Create())
        {
            ulong value;
            do
            {
                rng.GetBytes(bytes);
                value = BitConverter.ToUInt64(bytes, 0);
            }
            while (value == 0);
            return value;
        }
    }

    private static bool HashesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;

        int difference = 0;
        for (int i = 0; i < left.Length; i++)
            difference |= left[i] ^ right[i];
        return difference == 0;
    }
}
