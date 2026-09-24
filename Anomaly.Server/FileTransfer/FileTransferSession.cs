using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Anomaly.Server.Networking;
using Anomaly.Server.Content;
using Anomaly.Server.Api.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
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

    private static readonly Dictionary<ReferenceHub, FileTransferSession> ActiveSessions =
        new Dictionary<ReferenceHub, FileTransferSession>();
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

    private readonly ReferenceHub _hub;
    private readonly ulong _sessionId;
    private readonly FileManifestMessage _manifestMessage;
    private readonly Dictionary<string, ServerAssetManager.AssetInfo> _manifest;
    private readonly AssetPackSnapshot _assetSnapshot;
    private AssetChunkReader _chunkReader;
    private bool _snapshotTransferred;
    private sealed class PendingStart { internal CoroutineHandle Handle; }
    private static readonly Dictionary<ReferenceHub, PendingStart> PendingStarts = new Dictionary<ReferenceHub, PendingStart>();
    private readonly Queue<ServerAssetManager.AssetInfo> _pendingFiles =
        new Queue<ServerAssetManager.AssetInfo>();

    private State _state = State.AwaitingConsent;
    private ServerAssetManager.AssetInfo _currentFile;
    private bool _hasCurrentFile;
    private bool _failureClosed;
    private int _retryCount;
    private long _reservedBytes;
    private bool _budgetReserved;
    private DateTime _lastActivityUtc;
    private CoroutineHandle _sendHandle;
    private CoroutineHandle _watchdogHandle;

    private FileTransferSession(ReferenceHub hub, AssetPackSnapshot snapshot)
    {
        _hub = hub;
        _sessionId = CreateSessionId();
        _assetSnapshot = snapshot;
        _manifestMessage = snapshot.BuildManifest(_sessionId);
        _manifest = snapshot.Files;
        _lastActivityUtc = DateTime.UtcNow;
    }

    public static void StartForPlayer(ReferenceHub hub, Action<byte[], AssetPackSnapshot> beforeManifestSend = null)
    {
        if (hub == null) return;
        CleanupPlayer(hub);
        var pending = new PendingStart();
        PendingStarts[hub] = pending;
        try { pending.Handle = Timing.RunCoroutine(StartWhenReady(hub, pending, beforeManifestSend)); }
        catch
        {
            PendingStarts.Remove(hub);
            ContentGrantGate.Remove(hub.netId);
            Plugin.QuarantineAuthenticatedPlayer(hub, "asset-offer-scheduler-failure");
            hub.connectionToClient?.Disconnect();
        }
    }

    private static IEnumerator<float> StartWhenReady(ReferenceHub hub, PendingStart pending, Action<byte[], AssetPackSnapshot> beforeManifestSend)
    {
        // Yield before publication so the pending handle is installed before this coroutine removes it.
        yield return Timing.WaitForOneFrame;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!ServerAssetManager.IsReady && DateTime.UtcNow < deadline)
        {
            if (!PendingStarts.TryGetValue(hub, out var current) || !ReferenceEquals(current, pending)) yield break;
            yield return Timing.WaitForOneFrame;
        }
        if (!PendingStarts.TryGetValue(hub, out var owner) || !ReferenceEquals(owner, pending)) yield break;
        PendingStarts.Remove(hub);
        if (hub == null || hub.connectionToClient == null || !hub.connectionToClient.isReady) yield break;
        var snapshot = ServerAssetManager.AcquireSnapshot();
        FileTransferSession session = null;
        try
        {
            if (!ServerAssetManager.IsReady) throw new TimeoutException("Initial pack indexing did not complete.");
            session = new FileTransferSession(hub, snapshot);
            ActiveSessions[hub] = session;
            beforeManifestSend?.Invoke((byte[])session._manifestMessage.ManifestSha256.Clone(), snapshot);
            session.SendManifest();
        }
        catch (Exception exception)
        {
            if (session != null)
            {
                try { session.EndSession(false, "content offer preparation failed: " + exception.Message); }
                finally
                {
                    // The catalog callback may already have aborted and removed this
                    // session. That cleanup must not suppress admission revocation.
                    session.FailClosedAuthenticatedPlayer("asset-offer-failure");
                }
            }
            else { snapshot.Dispose(); Plugin.QuarantineAuthenticatedPlayer(hub, "asset-index-failure"); hub.connectionToClient?.Disconnect(); }
        }
    }
    public static void CleanupPlayer(ReferenceHub hub)
    {
        if (hub == null)
            return;
        if (PendingStarts.TryGetValue(hub, out var pending))
        {
            PendingStarts.Remove(hub);
            StopPendingBestEffort(pending);
        }
        if (ApprovedManifests.TryGetValue(hub, out var approved))
        {
            ApprovedManifests.Remove(hub);
            approved.Dispose();
        }
        if (ActiveSessions.TryGetValue(hub, out var session))
            session.EndSession(
                false,
                "player disconnected or session was replaced",
                failClosed: false);
    }

    internal static bool ApproveForPlayer(ReferenceHub hub)
    {
        if (hub == null || !ActiveSessions.TryGetValue(hub, out var session) ||
            session._state != State.AwaitingConsent ||
            ContentGrantGate.GetReadiness(hub.netId) != ContentReadiness.Approved)
            return false;

        session._state = State.AwaitingRequest;
        session._lastActivityUtc = DateTime.UtcNow;
        try
        {
            session._watchdogHandle = Timing.RunCoroutine(session.Watchdog());
            return true;
        }
        catch (Exception exception)
        {
            session.EndSession(false, "approved transfer watchdog could not start: " + exception.Message);
            return false;
        }
    }

    internal static bool IsAssetApproved(ReferenceHub hub, AssetAddress address, AssetKind kind) =>
        hub != null && ContentGrantGate.GetReadiness(hub.netId) == ContentReadiness.Activated &&
        ApprovedManifests.TryGetValue(hub, out AssetPackSnapshot snapshot) && snapshot.Contains(address, kind);

    internal static bool IsAssetApproved(ReferenceHub hub, AssetAddress address, AssetKind kind, byte[] indexHash) =>
        IsAssetApproved(hub, address, kind) && ApprovedManifests[hub].TryGetAsset(address, out var record) &&
        HashesEqual(record.IndexHash, indexHash);

    internal static IEnumerable<string> OfferedFailureKeys(ReferenceHub hub)
    {
        AssetPackSnapshot snapshot = ActiveSessions.TryGetValue(hub, out var session) ? session._assetSnapshot :
            ApprovedManifests.TryGetValue(hub, out var approved) ? approved : null;
        if (snapshot == null) yield break;
        foreach (var pack in snapshot.CopyPacks()) yield return pack.PackId;
        foreach (var asset in snapshot.Assets) yield return asset.Address.Value;
    }
    public static void Reset()
    {
        var sessions = new List<FileTransferSession>(ActiveSessions.Values);
        foreach (var session in sessions)
            session.EndSession(false, "server reset", failClosed: false);

        ActiveSessions.Clear();
        foreach (var snapshot in ApprovedManifests.Values) snapshot.Dispose();
        ApprovedManifests.Clear();
        var pendingStarts = new List<PendingStart>(PendingStarts.Values);
        PendingStarts.Clear();
        foreach (var pending in pendingStarts) StopPendingBestEffort(pending);
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
            // One bounded manifest is retained per connected player. Reading the
            // consent prompt has no deadline and reserves no active-transfer budget.
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
        if (_state == State.AwaitingConsent)
        {
            EndSession(false, "file request arrived before matching user consent");
            return;
        }
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
        int total = checked((int)((asset.Size + FileChunkMessage.MaxChunkBytes - 1) / FileChunkMessage.MaxChunkBytes));
        if (total <= 0 || total > FileChunkMessage.MaxChunksPerFile) { EndSession(false, "chunk budget exceeded", false); yield break; }
        _chunkReader?.Dispose();
        var reader = _chunkReader = new AssetChunkReader(_assetSnapshot, asset);
        int index = 0;
        while (!reader.IsCompleted)
        {
            if (_state != State.Sending) yield break;
            if (_hub.connectionToClient == null || !_hub.connectionToClient.isReady) { EndSession(false, "connection lost", false); yield break; }
            if (!reader.TryRead(out byte[] data)) { yield return Timing.WaitForOneFrame; continue; }
            bool sent = false;
            try { sent = AnomalyMessaging.TrySendToClient(_hub, new FileChunkMessage { SessionId = _sessionId, Hash = (byte[])asset.Hash.Clone(), ChunkIndex = checked((ushort)index++), TotalChunks = checked((ushort)total), Data = data }); }
            catch (Exception exception) { EndSession(false, "chunk send failed: " + exception.Message, false); }
            if (!sent) { EndSession(false, "chunk send failed", false); yield break; }
            _lastActivityUtc = DateTime.UtcNow;
            yield return Timing.WaitForSeconds(DefaultChunkIntervalMs / 1000f);
        }
        reader.Dispose();
        _chunkReader = null;
        if (reader.Error != null || index != total) { EndSession(false, reader.Error ?? "incomplete spool read", false); yield break; }
        bool completeSent = false;
        try { completeSent = AnomalyMessaging.TrySendToClient(_hub, new FileTransferCompleteMessage { SessionId = _sessionId, Hash = (byte[])asset.Hash.Clone() }); }
        catch (Exception exception) { EndSession(false, "completion send failed: " + exception.Message, false); }
        if (!completeSent) { EndSession(false, "completion send failed", false); yield break; }
        _state = State.AwaitingAck;
        _lastActivityUtc = DateTime.UtcNow;
    }

    private static void StopPendingBestEffort(PendingStart pending)
    {
        try { if (pending.Handle.IsRunning) Timing.KillCoroutines(pending.Handle); }
        catch (Exception exception) { try { Logger.Warn("FileTransfer: pending offer cancellation failed after ownership was revoked: " + exception.Message); } catch { } }
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
        _chunkReader?.Dispose();
        _chunkReader = null;

        bool ownedActiveSession = false;
        if (ActiveSessions.TryGetValue(_hub, out var current) && ReferenceEquals(current, this))
        {
            ownedActiveSession = true;
            ActiveSessions.Remove(_hub);
        }

        if (!completed && !_snapshotTransferred) _assetSnapshot.Dispose();

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
            _assetSnapshot.Dispose();
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

        try { ApprovedManifests[_hub] = _assetSnapshot; _snapshotTransferred = true; }
        catch (Exception exception)
        {
            if (!_snapshotTransferred) _assetSnapshot.Dispose();
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
        if (_failureClosed) return;
        _failureClosed = true;
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
