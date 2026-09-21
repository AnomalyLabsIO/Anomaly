using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Anomaly.Server.Networking;
using Anomaly.Server.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using MEC;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

public class FileTransferSession
{
    private enum State
    {
        AwaitingRequest,
        Sending,
        AwaitingAck,
        Complete,
        Aborted
    }

    private static readonly Dictionary<ReferenceHub, FileTransferSession> ActiveSessions =
        new Dictionary<ReferenceHub, FileTransferSession>();
    private static readonly Dictionary<ReferenceHub, AssetManifestSnapshot> ApprovedManifests =
        new Dictionary<ReferenceHub, AssetManifestSnapshot>();
    private static readonly object BudgetLock = new object();

    private const int MaxActiveTransfers = 8;
    private const long MaxGlobalReservedBytes = 512L * 1024L * 1024L;
    private const int DefaultChunkIntervalMs = 5;
    private const int MaxRetries = 2;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    private static int _reservedTransferCount;
    private static long _globalReservedBytes;

    public static event Action<ReferenceHub> SessionCompleted;

    private readonly ReferenceHub _hub;
    private readonly ulong _sessionId;
    private readonly FileManifestMessage _manifestMessage;
    private readonly Dictionary<string, ServerAssetManager.AssetInfo> _manifest;
    private readonly AssetManifestSnapshot _assetSnapshot;
    private readonly Queue<ServerAssetManager.AssetInfo> _pendingFiles =
        new Queue<ServerAssetManager.AssetInfo>();

    private State _state = State.AwaitingRequest;
    private ServerAssetManager.AssetInfo _currentFile;
    private bool _hasCurrentFile;
    private int _retryCount;
    private long _reservedBytes;
    private bool _budgetReserved;
    private DateTime _lastActivityUtc;
    private CoroutineHandle _sendHandle;
    private CoroutineHandle _watchdogHandle;

    private FileTransferSession(ReferenceHub hub)
    {
        _hub = hub;
        _sessionId = CreateSessionId();
        _manifestMessage = ServerAssetManager.BuildManifest(_sessionId, out var snapshot);
        _manifest = snapshot;
        _assetSnapshot = new AssetManifestSnapshot(_manifestMessage.Files);
        _lastActivityUtc = DateTime.UtcNow;
    }

    public static void StartForPlayer(ReferenceHub hub, Action<byte[]> beforeManifestSend = null)
    {
        if (hub == null)
            return;

        CleanupPlayer(hub);
        var session = new FileTransferSession(hub);
        ActiveSessions[hub] = session;
        try
        {
            beforeManifestSend?.Invoke((byte[])session._manifestMessage.ManifestSha256.Clone());
            session.SendManifest();
        }
        catch (Exception exception)
        {
            session.EndSession(false, "content offer preparation failed: " + exception.Message);
            throw;
        }
    }

    public static void CleanupPlayer(ReferenceHub hub)
    {
        if (hub == null)
            return;
        ApprovedManifests.Remove(hub);
        if (ActiveSessions.TryGetValue(hub, out var session))
            session.EndSession(
                false,
                "player disconnected or session was replaced",
                failClosed: false);
    }

    internal static bool IsAssetApproved(ReferenceHub hub, byte[] hash, AssetKind expectedKind) =>
        hub != null &&
        ApprovedManifests.TryGetValue(hub, out AssetManifestSnapshot manifest) &&
        manifest.Contains(hash, expectedKind);

    public static void Reset()
    {
        var sessions = new List<FileTransferSession>(ActiveSessions.Values);
        foreach (var session in sessions)
            session.EndSession(false, "server reset", failClosed: false);

        ActiveSessions.Clear();
        ApprovedManifests.Clear();
        lock (BudgetLock)
        {
            _reservedTransferCount = 0;
            _globalReservedBytes = 0;
        }
    }

    public static void HandleMessage(ReferenceHub sender, IAnomalyMessage msg)
    {
        if (sender == null || !ActiveSessions.TryGetValue(sender, out var session))
            return;

        switch (msg)
        {
            case FileRequestMessage request:
                session.HandleFileRequest(request);
                break;
            case FileAckMessage ack:
                session.HandleFileAck(ack);
                break;
        }
    }

    private void SendManifest()
    {
        try
        {
            if (!AnomalyMessaging.TrySendToClient(_hub, _manifestMessage))
                throw new InvalidOperationException("the authenticated manifest could not be delivered");
            _lastActivityUtc = DateTime.UtcNow;
            _watchdogHandle = Timing.RunCoroutine(Watchdog());
            try
            {
                Logger.Info(
                    $"FileTransfer: Sent bounded manifest ({_manifestMessage.Files.Count} files, session={_sessionId:x16}) " +
                    $"to {_hub.LoggedNameFromRefHub()}.");
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
        if (_state != State.AwaitingRequest)
        {
            Logger.Warn($"FileTransfer: Rejected duplicate/out-of-state request from {_hub.LoggedNameFromRefHub()}.");
            return;
        }
        if (request.SessionId != _sessionId)
        {
            EndSession(false, "request session id did not match the advertised manifest");
            return;
        }
        if (!ContentGrantGate.TryBeginTransfer(_hub.netId))
        {
            EndSession(false, "file request arrived without matching user consent");
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
                $"for {_hub.LoggedNameFromRefHub()}.");
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
        FileStream stream;
        try
        {
            stream = new FileStream(asset.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            EndSession(false, $"failed to open '{asset.RelativePath}': {ex.Message}", false);
            yield break;
        }

        using (stream)
        using (var sha = SHA256.Create())
        {
            long streamLength;
            try
            {
                streamLength = stream.Length;
            }
            catch (Exception ex)
            {
                EndSession(false, $"failed to inspect '{asset.RelativePath}': {ex.Message}", false);
                yield break;
            }

            if (streamLength != asset.Size)
            {
                EndSession(false, $"asset changed size after manifest: '{asset.RelativePath}'", false);
                yield break;
            }

            int totalChunks = checked((int)((asset.Size + FileChunkMessage.MaxChunkBytes - 1) /
                                            FileChunkMessage.MaxChunkBytes));
            if (totalChunks <= 0 || totalChunks > FileChunkMessage.MaxChunksPerFile)
            {
                EndSession(false, $"asset exceeded the chunk-count limit: '{asset.RelativePath}'", false);
                yield break;
            }

            for (int i = 0; i < totalChunks; i++)
            {
                if (_state != State.Sending)
                    yield break;
                if (_hub.connectionToClient == null || !_hub.connectionToClient.isReady)
                {
                    EndSession(false, "connection was lost during transfer", false);
                    yield break;
                }

                int expectedLength = (int)Math.Min(
                    FileChunkMessage.MaxChunkBytes,
                    (long)asset.Size - stream.Position);
                var data = new byte[expectedLength];
                try
                {
                    int read = 0;
                    while (read < expectedLength)
                    {
                        int current = stream.Read(data, read, expectedLength - read);
                        if (current == 0)
                            break;
                        read += current;
                    }
                    if (read != expectedLength)
                    {
                        EndSession(false, $"asset was truncated during transfer: '{asset.RelativePath}'", false);
                        yield break;
                    }

                    sha.TransformBlock(data, 0, data.Length, data, 0);
                    if (!AnomalyMessaging.TrySendToClient(_hub, new FileChunkMessage
                    {
                        SessionId = _sessionId,
                        Hash = (byte[])asset.Hash.Clone(),
                        ChunkIndex = checked((ushort)i),
                        TotalChunks = checked((ushort)totalChunks),
                        Data = data
                    }))
                        throw new InvalidOperationException("the authenticated chunk could not be delivered");
                }
                catch (Exception ex)
                {
                    EndSession(false, $"failed to stream '{asset.RelativePath}': {ex.Message}", false);
                    yield break;
                }
                _lastActivityUtc = DateTime.UtcNow;
                yield return Timing.WaitForSeconds(DefaultChunkIntervalMs / 1000f);
            }

            try
            {
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            catch (Exception ex)
            {
                EndSession(false, $"failed to verify '{asset.RelativePath}': {ex.Message}", false);
                yield break;
            }
            if (!HashesEqual(sha.Hash, asset.Hash))
            {
                EndSession(false, $"asset changed content after manifest: '{asset.RelativePath}'", false);
                yield break;
            }

            try
            {
                if (!AnomalyMessaging.TrySendToClient(_hub, new FileTransferCompleteMessage
                    {
                        SessionId = _sessionId,
                        Hash = (byte[])asset.Hash.Clone()
                    }))
                {
                    throw new InvalidOperationException(
                        "the authenticated completion marker could not be delivered");
                }
            }
            catch (Exception ex)
            {
                EndSession(
                    false,
                    $"completion marker send failed for '{asset.RelativePath}': {ex.Message}",
                    false);
                yield break;
            }
            _state = State.AwaitingAck;
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    private void HandleFileAck(FileAckMessage ack)
    {
        if (_state != State.AwaitingAck || !_hasCurrentFile)
        {
            Logger.Warn($"FileTransfer: Rejected unsolicited/out-of-state ACK from {_hub.LoggedNameFromRefHub()}.");
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
            if (DateTime.UtcNow - _lastActivityUtc > SessionTimeout)
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

    private void EndSession(
        bool completed,
        string reason,
        bool stopSend = true,
        bool failClosed = true)
    {
        if (_state == State.Complete || _state == State.Aborted)
            return;

        _state = completed ? State.Complete : State.Aborted;

        // Revoke logical ownership and release the global budget before
        // touching the external scheduler. A failed coroutine cancellation
        // cannot retain an active/approved transfer or exhaust future slots.
        ReleaseBudget();

        bool ownedActiveSession = false;
        if (ActiveSessions.TryGetValue(_hub, out var current) && ReferenceEquals(current, this))
        {
            ownedActiveSession = true;
            ActiveSessions.Remove(_hub);
        }

        if (!completed && ownedActiveSession)
            ApprovedManifests.Remove(_hub);

        if (!completed && failClosed && ownedActiveSession)
        {
            ContentGrantGate.Remove(_hub.netId);
            FailClosedAuthenticatedPlayer("file-transfer-failure");
        }

        if (stopSend)
            StopCoroutineBestEffort(_sendHandle, "send");
        StopCoroutineBestEffort(_watchdogHandle, "watchdog");

        if (!completed)
        {
            try
            {
                Logger.Warn(
                    $"FileTransfer: Aborted session {_sessionId:x16} for {_hub.LoggedNameFromRefHub()}: {reason}.");
            }
            catch { }
            return;
        }

        if (!ContentGrantGate.TryCompleteTransfer(_hub.netId))
        {
            ApprovedManifests.Remove(_hub);
            if (ownedActiveSession)
            {
                ContentGrantGate.Remove(_hub.netId);
                FailClosedAuthenticatedPlayer("file-transfer-completion-state-mismatch");
            }
            try
            {
                Logger.Error(
                    $"FileTransfer: Completed session {_sessionId:x16} without an approved transfer state for " +
                    $"{_hub.LoggedNameFromRefHub()}; activation remains closed.");
            }
            catch { }
            return;
        }

        try { ApprovedManifests[_hub] = _assetSnapshot; }
        catch (Exception exception)
        {
            if (ownedActiveSession)
            {
                ContentGrantGate.Remove(_hub.netId);
                FailClosedAuthenticatedPlayer("approved-manifest-publication-failure");
            }
            try { Logger.Error($"FileTransfer: Approved manifest publication failed: {exception}"); }
            catch { }
            return;
        }

        Delegate[] subscribers = SessionCompleted?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<ReferenceHub>)subscriber)(_hub);
            }
            catch (Exception ex)
            {
                try { Logger.Error($"FileTransfer: SessionCompleted subscriber threw: {ex}"); }
                catch { }
            }
        }
        try
        {
            Logger.Info(
                $"FileTransfer: Completed session {_sessionId:x16} for {_hub.LoggedNameFromRefHub()}: {reason}.");
        }
        catch { }
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

    private void FailClosedAuthenticatedPlayer(string context)
    {
        Exception quarantineFailure = null;
        Exception disconnectFailure = null;
        try { Plugin.QuarantineAuthenticatedPlayer(_hub, context); }
        catch (Exception exception) { quarantineFailure = exception; }
        try { _hub?.connectionToClient?.Disconnect(); }
        catch (Exception exception) { disconnectFailure = exception; }

        if (quarantineFailure != null)
        {
            try { Logger.Error($"FileTransfer: Failed to quarantine after {context}: {quarantineFailure}"); }
            catch { }
        }
        if (disconnectFailure != null)
        {
            try { Logger.Error($"FileTransfer: Failed to disconnect after {context}: {disconnectFailure}"); }
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
