using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Anomaly.Server.Networking;
using Anomaly.Server.Content;
using Anomaly.Server.Api.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.FileTransfer;

/// <summary>One approved offer, bounded file pipeline, and cumulative receiver credits.</summary>
internal class FileTransferSession : IBulkTransferSource
{
    private enum State { AwaitingConsent, AwaitingRequest, Queued, Sending, Complete, Aborted }
    private static readonly Dictionary<NetworkConnectionToClient, FileTransferSession> ActiveSessions =
        new Dictionary<NetworkConnectionToClient, FileTransferSession>();
    private static readonly Dictionary<ReferenceHub, AssetPackSnapshot> ApprovedManifests =
        new Dictionary<ReferenceHub, AssetPackSnapshot>();
    private static readonly Queue<FileTransferSession> WaitingTransfers = new Queue<FileTransferSession>();
    private static readonly BulkTransferPump Pump = new BulkTransferPump();
    private const int MaxActiveTransfers = FileTransferAdmission.MaximumActiveTransfers;
    private const int MaxQueuedTransfers = 32;
    private const long MaxGlobalReservedBytes = FileTransferAdmission.MaximumReservedBytes;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);
    private static int _reservedTransferCount, _queuedTransferCount;
    private static long _globalReservedBytes;

    public static event Action<ReferenceHub> SessionCompleted;
    private readonly NetworkConnectionToClient _connection;
    private readonly Action _completed;
    private readonly Action<string> _failed;
    private readonly ulong _sessionId;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Func<double> _remainingSeconds;
    private readonly FileCompletionPacer _completionPacer = new FileCompletionPacer();
    private double _queueDeadline;
    private readonly FileManifestMessage _manifestMessage;
    private readonly Dictionary<string, ServerAssetManager.AssetInfo> _manifest;
    private readonly AssetPackSnapshot _assetSnapshot;
    private readonly Queue<TransferFile> _pendingFiles = new Queue<TransferFile>();
    private readonly List<TransferFile> _sendingFiles = new List<TransferFile>();
    private readonly Dictionary<string, TransferFile> _requestedFiles = new Dictionary<string, TransferFile>(StringComparer.Ordinal);
    private State _state = State.AwaitingConsent;
    private bool _disposed, _budgetReserved, _queueWaitAnnounced;
    private long _requestedBytes;
    private DateTime _lastActivityUtc;
    private BulkCreditWindow _credits;
    private ulong _laneMask;
    private int _domainCount, _nextDomain, _nextFile;

    private FileTransferSession(NetworkConnectionToClient connection, AssetPackSnapshot snapshot, Action completed, Action<string> failed,
        Func<double> remainingSeconds)
    {
        _connection = connection;
        _completed = completed;
        _failed = failed;
        _remainingSeconds = remainingSeconds ?? (() => ConnectionContentState.TimeoutSeconds - _elapsed.Elapsed.TotalSeconds);
        _sessionId = CreateSessionId();
        _assetSnapshot = snapshot;
        _manifestMessage = snapshot.BuildManifest(_sessionId);
        var bulkReplay = AuthenticatedTransportSessions.GetBulkReplayState(connection);
        if (bulkReplay != null) _manifestMessage.BulkCounterBases = bulkReplay.GetTxCounterBases();
        if (AnomalyMessaging.TryGetBulkTransferProfile(out byte version, out ulong mask))
        {
            _manifestMessage.BulkProfileVersion = version;
            _manifestMessage.BulkLaneMask = mask;
        }
        _manifest = snapshot.Files;
        _lastActivityUtc = DateTime.UtcNow;
    }

    internal FileManifestMessage Manifest => _manifestMessage;
    internal AssetPackSnapshot Snapshot => _assetSnapshot;
    internal bool IsComplete => _state == State.Complete && !_disposed;

    internal static void ConfigureBudget(int clientBytesPerSecond, int uploadBytesPerSecond, int headroomPercent) =>
        Pump.Configure(new BulkTransferBudget(clientBytesPerSecond, uploadBytesPerSecond, headroomPercent));

    internal static bool TryGetBulkSendScope(NetworkConnectionToClient connection, out ulong laneMask, out ulong sessionId)
    {
        laneMask = sessionId = 0;
        if (connection == null || !ActiveSessions.TryGetValue(connection, out var session) ||
            session._disposed || session._state != State.Sending) return false;
        laneMask = session._laneMask; sessionId = session._sessionId;
        return laneMask != 0;
    }

    internal static FileTransferSession StartForConnection(NetworkConnectionToClient connection,
        AssetPackSnapshot snapshot, Action completed, Action<string> failed, Func<double> remainingSeconds = null)
    {
        CleanupConnection(connection);
        var session = new FileTransferSession(connection, snapshot, completed, failed, remainingSeconds);
        ActiveSessions.Add(connection, session);
        return session;
    }

    internal bool Approve()
    {
        if (_disposed || _state != State.AwaitingConsent) return false;
        _state = State.AwaitingRequest;
        _lastActivityUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Called once per main-thread update. Workers never send network traffic.</summary>
    internal static void Tick()
    {
        foreach (var session in new List<FileTransferSession>(ActiveSessions.Values))
        {
            if (session._disposed || session._state == State.Complete || session._state == State.Aborted ||
                session._state == State.AwaitingConsent) continue;
            if (!AuthenticatedTransportSessions.IsActiveConnection(session._connection))
                session.EndSession(false, "connection lost");
            else if (session._state == State.Queued && session._elapsed.Elapsed.TotalSeconds >= session._queueDeadline)
                session.EndSession(false, "bounded file-transfer admission wait expired");
            else if (session._state != State.Queued && DateTime.UtcNow - session._lastActivityUtc > SessionTimeout)
                session.EndSession(false, "file transfer made no progress for 30 seconds");
        }
        AdmitWaitingTransfers();
        Pump.Tick();
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
        schema = null; indexHash = null; activationId = 0;
        if (hub?.connectionToClient == null || ContentGrantGate.GetReadiness(hub.netId) != ContentReadiness.Activated ||
            !ApprovedManifests.TryGetValue(hub, out var approved) ||
            !ActiveSessions.TryGetValue(hub.connectionToClient, out var session) || !session.IsComplete ||
            !ReferenceEquals(approved, session._assetSnapshot) || !ConnectionContentBootstrap.IsComplete(hub.connectionToClient) ||
            !approved.TryGetScriptSchema(packId, name, direction, out schema, out indexHash)) return false;
        activationId = session._sessionId;
        return true;
    }

    public static void Reset()
    {
        foreach (var connection in new List<NetworkConnectionToClient>(ActiveSessions.Keys)) CleanupConnection(connection);
        foreach (var hub in new List<ReferenceHub>(ApprovedManifests.Keys)) CleanupPlayer(hub);
        WaitingTransfers.Clear(); Pump.Reset();
        _reservedTransferCount = _queuedTransferCount = 0; _globalReservedBytes = 0;
    }

    internal void HandleMessage(IAnomalyMessage message)
    {
        if (_disposed || _state == State.Aborted) return;
        switch (message)
        {
            case FileRequestMessage request: HandleFileRequest(request); break;
            case FileAckMessage acknowledgement: HandleFileAck(acknowledgement); break;
            case FileCreditMessage credit: HandleCredit(credit); break;
            default: EndSession(false, "unsupported file-transfer message"); break;
        }
    }

    internal void SendManifest()
    {
        try
        {
            if (!AnomalyMessaging.TrySendToConnection(_connection, _manifestMessage))
                throw new InvalidOperationException("the authenticated manifest could not be delivered");
        }
        catch (Exception exception) { EndSession(false, "manifest send failed: " + exception.Message); throw; }
    }

    private void HandleFileRequest(FileRequestMessage request)
    {
        if (_state != State.AwaitingRequest || request.SessionId != _sessionId)
        { EndSession(false, "file request did not match the consented session state"); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hash in request.RequestedHashes)
        {
            string hex = ServerAssetManager.ToHex(hash);
            if (!seen.Add(hex) || !_manifest.TryGetValue(hex, out var asset))
            { EndSession(false, "request repeated or referenced an unadvertised hash"); return; }
            _requestedBytes += asset.Size;
            if (_requestedBytes > FileManifestMessage.MaxAggregateBytes)
            { EndSession(false, "request exceeded the per-client byte budget"); return; }
            var file = new TransferFile(asset);
            _pendingFiles.Enqueue(file); _requestedFiles.Add(hex, file);
        }
        _lastActivityUtc = DateTime.UtcNow;
        if (_pendingFiles.Count == 0) { EndSession(true, "all advertised files were cached"); return; }
        if (request.BulkProfileVersion != BulkTransferProtocol.ProfileVersion ||
            request.BulkProfileVersion != _manifestMessage.BulkProfileVersion ||
            !BulkTransferProtocol.IsValidMask(request.BulkLaneMask, false) ||
            (request.BulkLaneMask & _manifestMessage.BulkLaneMask) != request.BulkLaneMask ||
            request.ReceiveWindowBytes < FileChunkMessage.MaxChunkBytes ||
            request.ReceiveWindowBytes > BulkTransferProtocol.MaxWindowBytes ||
            request.ReceiveWindowFrames == 0 || request.ReceiveWindowFrames > BulkTransferProtocol.MaxWindowFrames)
        { EndSession(false, "request selected an unsupported bulk profile or receive window"); return; }
        _laneMask = request.BulkLaneMask;
        _domainCount = BulkTransferProtocol.DomainCount(_laneMask);
        _credits = new BulkCreditWindow(request.ReceiveWindowBytes, request.ReceiveWindowFrames, _domainCount,
            _manifestMessage.BulkCounterBases);
        PruneWaitingTransfers();
        if (_queuedTransferCount >= MaxQueuedTransfers)
        { EndSession(false, "bounded file-transfer admission queue is full"); return; }
        if (!TryCollectCapacityPlan(this, out var active, out var queued, out var requested, out int queuePosition) ||
            !FileTransferAdmission.TryPlan(Pump.Budget, active, queued, requested, _remainingSeconds(), out double queueLimit,
                Pump.AdmissionCapacity.AccountedBytesPerSecond, queuePosition))
        { EndSession(false, "available upload capacity cannot complete this request within the content admission deadline"); return; }
        _queueDeadline = _elapsed.Elapsed.TotalSeconds + queueLimit;
        _state = State.Queued; _queuedTransferCount++;
        WaitingTransfers.Enqueue(this);
        AdmitWaitingTransfers();
        if (_state != State.Queued) return;
        if (queueLimit <= 0) { EndSession(false, "no queue wait remains in the content admission deadline"); return; }
        _queueWaitAnnounced = true;
        if (!AnomalyMessaging.TrySendToConnection(_connection, new FileTransferStatusMessage
        { SessionId = _sessionId, Status = FileTransferStatus.Queued, QueuePosition = checked((byte)_queuedTransferCount),
            QueueWaitMilliseconds = checked((ushort)Math.Max(1, Math.Min(FileTransferStatusMessage.MaxQueueWaitMilliseconds,
                Math.Floor((_queueDeadline - _elapsed.Elapsed.TotalSeconds) * 1000)))) }))
            EndSession(false, "queue admission status could not be delivered");
    }

    private static void AdmitWaitingTransfers()
    {
        PruneWaitingTransfers();
        while (WaitingTransfers.Count != 0)
        {
            var session = WaitingTransfers.Peek();
            if (session._disposed || session._state != State.Queued) { WaitingTransfers.Dequeue(); continue; }
            if (_reservedTransferCount >= MaxActiveTransfers ||
                _globalReservedBytes + session._requestedBytes > MaxGlobalReservedBytes) return;
            if (session._queueWaitAnnounced && session._elapsed.Elapsed.TotalSeconds >= session._queueDeadline ||
                !TryCollectCapacityPlan(session, out var active, out var queued, out var requested, out int queuePosition) ||
                !FileTransferAdmission.TryPlan(Pump.Budget, active, queued, requested, session._remainingSeconds(), out _,
                    Pump.AdmissionCapacity.AccountedBytesPerSecond, queuePosition))
            { session.EndSession(false, "remaining content deadline cannot accommodate the admitted transfer"); continue; }
            WaitingTransfers.Dequeue(); _queuedTransferCount--;
            session._budgetReserved = true; _reservedTransferCount++; _globalReservedBytes += session._requestedBytes;
            session._state = State.Sending;
            session._lastActivityUtc = DateTime.UtcNow;
            try
            {
                if (!AnomalyMessaging.TrySendToConnection(session._connection, new FileTransferStatusMessage
                { SessionId = session._sessionId, Status = FileTransferStatus.Admitted }))
                    throw new InvalidOperationException("transfer admission status could not be delivered");
                session.FillPipeline(); Pump.Add(session);
            }
            catch (Exception exception) { session.EndSession(false, "spool reader could not start: " + exception.Message); }
        }
    }

    private bool TryCapacityWork(out FileTransferAdmission.Work work)
    {
        work = default;
        if (!AnomalyMessaging.TryGetBulkLinkCapacity(_connection, _laneMask, out int mtu, out int roundTripTime) ||
            !FileTransferAdmission.TryEstimateAccountedRate(Pump.Budget, _domainCount, mtu, roundTripTime,
                out double rate)) return false;
        long remainingBytes = 0, remainingAccountedBytes = 0;
        int remainingFiles = 0, pendingCompletions = 0;
        foreach (var file in _requestedFiles.Values)
        {
            if (file.Verified) continue;
            remainingFiles++;
            if (!file.CompletionSent) pendingCompletions++;
            long remaining = Math.Max(0L, (long)file.Asset.Size - (long)file.NextChunk * FileChunkMessage.MaxChunkBytes);
            remainingBytes = checked(remainingBytes + remaining);
            remainingAccountedBytes = checked(remainingAccountedBytes + FileTransferAdmission.AccountedFileRemainder(remaining));
        }
        work = new FileTransferAdmission.Work(_requestedBytes, remainingBytes, remainingAccountedBytes, rate,
            _remainingSeconds(), remainingFiles, pendingCompletions, roundTripTime,
            _queueWaitAnnounced ? Math.Max(0, _queueDeadline - _elapsed.Elapsed.TotalSeconds) : double.MaxValue);
        return true;
    }

    private static bool TryCollectCapacityPlan(FileTransferSession target, out List<FileTransferAdmission.Work> active,
        out List<FileTransferAdmission.Work> queued, out FileTransferAdmission.Work requested, out int targetQueuePosition)
    {
        active = new List<FileTransferAdmission.Work>();
        queued = new List<FileTransferAdmission.Work>();
        requested = default;
        targetQueuePosition = -1;
        foreach (var session in new List<FileTransferSession>(ActiveSessions.Values))
        {
            if (session._disposed || session._state != State.Sending) continue;
            if (!session.TryCapacityWork(out var work))
            { session.EndSession(false, "active bulk transport capacity is unavailable"); continue; }
            active.Add(work);
        }
        // Revalidate the entire FIFO, including promises behind an existing
        // target. Do not move that target to the tail during its admission check.
        foreach (var session in new List<FileTransferSession>(WaitingTransfers))
        {
            if (ReferenceEquals(session, target)) { targetQueuePosition = queued.Count; continue; }
            if (session._disposed || session._state != State.Queued) continue;
            if (!session.TryCapacityWork(out var work))
            { session.EndSession(false, "queued bulk transport capacity is unavailable"); continue; }
            queued.Add(work);
        }
        return !target._disposed && target.TryCapacityWork(out requested);
    }

    private static void PruneWaitingTransfers()
    {
        // Cancellation already releases the live queue count and snapshot owner.
        // Retired entries behind a capacity-blocked head must not retain sessions
        // outside that bound. Rotate only this finite snapshot, preserving FIFO.
        int count = WaitingTransfers.Count;
        for (int index = 0; index < count; index++)
        {
            var session = WaitingTransfers.Dequeue();
            if (!session._disposed && session._state == State.Queued)
                WaitingTransfers.Enqueue(session);
        }
    }

    private void FillPipeline()
    {
        while (_sendingFiles.Count < BulkTransferProtocol.MaxFilesInFlight && _pendingFiles.Count != 0)
        {
            var file = _pendingFiles.Dequeue();
            file.Reader = new AssetChunkReader(_assetSnapshot, file.Asset);
            _sendingFiles.Add(file);
        }
    }

    BulkPumpResult IBulkTransferSource.TrySendNext(int maximumBytes, out int sentBytes)
    {
        sentBytes = 0;
        if (_disposed || _state != State.Sending) return BulkPumpResult.Finished;
        try
        {
            for (int offset = 0; offset < _sendingFiles.Count; offset++)
            {
                int position = (_nextFile + offset) % _sendingFiles.Count;
                var file = _sendingFiles[position];
                if (file.CompletionSent) continue;
                if (!file.Reader.TryPeek(out byte[] data))
                {
                    if (!file.Reader.IsCompleted) continue;
                    if (file.Reader.Error != null || file.NextChunk != file.ChunkCount)
                    { EndSession(false, file.Reader.Error ?? "incomplete immutable spool read"); return BulkPumpResult.Finished; }
                    // Ordered control can overtake chunks on other physical
                    // lanes. Pace both completion and its eventual file ACK so
                    // tiny files/retries cannot flood ordinary receive limits.
                    if (!_completionPacer.TryTake()) continue;
                    if (!AnomalyMessaging.TrySendToConnection(_connection, new FileTransferCompleteMessage
                    { SessionId = _sessionId, Hash = (byte[])file.Asset.Hash.Clone(), Attempt = file.Attempt }))
                    { EndSession(false, "completion delivery failed"); return BulkPumpResult.Finished; }
                    file.CompletionSent = true;
                    file.Reader.Dispose(); file.Reader = null;
                    _lastActivityUtc = DateTime.UtcNow;
                    continue;
                }
                if (data.Length > maximumBytes) continue;
                for (int domainOffset = 0; domainOffset < _domainCount; domainOffset++)
                {
                    int domain = (_nextDomain + domainOffset) % _domainCount;
                    if (!_credits.CanSend(domain, data.Length)) continue;
                    var message = new FileChunkMessage
                    {
                        SessionId = _sessionId, Hash = (byte[])file.Asset.Hash.Clone(),
                        ProfileVersion = BulkTransferProtocol.ProfileVersion, Domain = (byte)domain, Attempt = file.Attempt,
                        ChunkIndex = checked((ushort)file.NextChunk), TotalChunks = checked((ushort)file.ChunkCount), Data = data,
                    };
                    BulkSendResult result = AnomalyMessaging.TrySendBulkChunkToConnection(_connection, message, domain);
                    if (result == BulkSendResult.Backpressured) continue;
                    if (result != BulkSendResult.Sent)
                    { EndSession(false, "bulk transport delivery failed"); return BulkPumpResult.Finished; }
                    _credits.RecordSent(domain, data.Length, message.FrameCounter);
                    if (!file.Reader.TryRead(out var consumed) || !ReferenceEquals(data, consumed))
                        throw new InvalidOperationException("Bulk spool ownership changed during a main-thread send.");
                    file.NextChunk++;
                    _nextDomain = (domain + 1) % _domainCount;
                    _nextFile = (position + 1) % _sendingFiles.Count;
                    _lastActivityUtc = DateTime.UtcNow;
                    sentBytes = data.Length;
                    return BulkPumpResult.Sent;
                }
            }
        }
        catch (Exception exception) { EndSession(false, "bulk pump failed: " + exception.Message); return BulkPumpResult.Finished; }
        return BulkPumpResult.Blocked;
    }

    private void HandleCredit(FileCreditMessage credit)
    {
        if (credit.SessionId != _sessionId) return;
        if (_credits == null) { EndSession(false, "credit arrived before a bulk request"); return; }
        ulong before = _credits.ReleasedBytes;
        if (!_credits.TryApplyCredit(credit, out string error)) { EndSession(false, "invalid bulk credit: " + error); return; }
        if (_credits.ReleasedBytes != before) _lastActivityUtc = DateTime.UtcNow;
    }

    private void HandleFileAck(FileAckMessage acknowledgement)
    {
        if (acknowledgement.SessionId != _sessionId) return;
        string hash = ServerAssetManager.ToHex(acknowledgement.Hash);
        if (!_requestedFiles.TryGetValue(hash, out var file))
        { EndSession(false, "ACK referenced an unrequested file"); return; }
        if (acknowledgement.Attempt < file.Attempt) return;
        if (acknowledgement.Attempt != file.Attempt || !file.CompletionSent)
        { EndSession(false, "ACK referenced an uncompleted or future file attempt"); return; }
        if (file.Verified) return;
        _lastActivityUtc = DateTime.UtcNow;
        if (acknowledgement.Status == 0)
        {
            file.Verified = true; _sendingFiles.Remove(file);
            try { FillPipeline(); }
            catch (Exception exception) { EndSession(false, "spool reader could not start: " + exception.Message); return; }
            if (_sendingFiles.Count == 0 && _pendingFiles.Count == 0)
                EndSession(true, "all requested files were verified by the client");
            return;
        }
        if (file.Attempt >= BulkTransferProtocol.MaxRetries)
        { EndSession(false, "client failed integrity verification after the bounded retries"); return; }
        file.Attempt++;
        file.NextChunk = 0; file.CompletionSent = false;
        try { file.Reader = new AssetChunkReader(_assetSnapshot, file.Asset); }
        catch (Exception exception) { EndSession(false, "retry spool reader could not start: " + exception.Message); }
    }

    private void EndSession(bool completed, string reason)
    {
        if (_disposed || _state == State.Complete || _state == State.Aborted) return;
        ReleaseResources();
        _state = completed ? State.Complete : State.Aborted;
        if (completed) _completed(); else _failed(reason);
    }

    private void ReleaseResources()
    {
        if (_state == State.Queued) _queuedTransferCount--;
        Pump.Remove(this);
        foreach (var file in _sendingFiles) { file.Reader?.Dispose(); file.Reader = null; }
        if (_budgetReserved)
        {
            _budgetReserved = false; _reservedTransferCount--;
            _globalReservedBytes -= _requestedBytes;
        }
    }

    private void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseResources(); _state = State.Aborted;
        _assetSnapshot.Dispose();
    }

    private static ulong CreateSessionId()
    {
        var bytes = new byte[sizeof(ulong)];
        using (var random = RandomNumberGenerator.Create())
        {
            ulong value;
            do { random.GetBytes(bytes); value = BitConverter.ToUInt64(bytes, 0); } while (value == 0);
            return value;
        }
    }

    private static bool HashesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        int difference = 0;
        for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
        return difference == 0;
    }

    private sealed class TransferFile
    {
        internal readonly ServerAssetManager.AssetInfo Asset;
        internal readonly int ChunkCount;
        internal AssetChunkReader Reader;
        internal byte Attempt;
        internal int NextChunk;
        internal bool CompletionSent, Verified;
        internal TransferFile(ServerAssetManager.AssetInfo asset)
        {
            Asset = asset;
            ChunkCount = checked((int)((asset.Size + FileChunkMessage.MaxChunkBytes - 1) / FileChunkMessage.MaxChunkBytes));
            if (ChunkCount <= 0 || ChunkCount > FileChunkMessage.MaxChunksPerFile)
                throw new InvalidOperationException("File exceeded its bounded chunk count.");
        }
    }
}
