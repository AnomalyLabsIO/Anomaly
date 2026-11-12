using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Components;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using MelonLoader;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Anomaly.Client.FileTransfer;

public static class FileTransferClient
{
    private enum State { Idle, AwaitingConsent, Preparing, AwaitingAdmission, Queued, Receiving, Complete, Aborted }

    private sealed class ReceiveFile
    {
        internal FileReceiveFile File;
        internal bool[] Received;
        internal bool CompletionReceived;
    }

    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(30);
    private static State _state;
    private static ulong _sessionId;
    private static long _epoch;
    private static byte _localProfile = BulkTransferProtocol.ProfileVersion;
    private static ulong _localLaneMask, _selectedLaneMask;
    private static Func<bool> _transportAvailable;
    private static double _queueDeadline;
    private static FileReceiveWorker _worker;
    private static BulkCreditWindow _receiveWindow;
    private static BulkConsumptionTracker _consumedCounters;
    private static uint[] _counterBases;
    private static readonly Dictionary<string, FileReceiveFile> _manifest = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ReceiveFile> _buffers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, byte> _attempts = new(StringComparer.Ordinal);
    private static readonly uint[] _releasedFrames = new uint[BulkTransferProtocol.MaxDomains];
    private static ulong _releasedBytes;
    private static uint _creditSequence;
    private static bool _creditDirty, _progressDirty;
    private static DateTime _lastCreditUtc, _lastProgressUtc, _lastActivityUtc;
    private static string _progressPath;
    private static int _totalFiles, _completedFiles, _totalAssetsInManifest;
    private static long _totalBytes, _receivedBytes;
    private static byte[] _manifestSha256;
    private static AssetManifestSnapshot _manifestSnapshot;
    private static readonly TransactionalInitializer Initializer = new();

    internal static event Action<FileManifestMessage> ManifestOffered;
    internal static event Action<bool> TransferFinished;
    internal static event Action<byte[]> FileVerified;
    internal static ulong SelectedBulkLaneMask => _selectedLaneMask;
    internal static bool IsBulkReceiving => IsRequestedPhase && _selectedLaneMask != 0;
    private static bool IsRequestedPhase => _state == State.AwaitingAdmission || _state == State.Queued || _state == State.Receiving;

    // Physical channel ownership belongs to the transport lifetime, not a file session.
    internal static void ConfigureBulkProfile(byte version, ulong ownedMask, Func<bool> transportAvailable = null)
    {
        if (version != BulkTransferProtocol.ProfileVersion || !BulkTransferProtocol.IsValidMask(ownedMask))
            throw new ArgumentException("Unsupported client bulk transport profile.");
        if ((IsRequestedPhase || _state == State.Preparing) &&
            (_localProfile != version || _localLaneMask != ownedMask))
            Abort("the owned bulk transport profile changed during a transfer");
        _localProfile = version;
        _localLaneMask = ownedMask;
        _transportAvailable = transportAvailable;
    }

    public static void Init()
    {
        Initializer.Initialize(
            new InitializationStep(() =>
                {
                    AnomalyMessaging.BulkReceiveExpected = () => IsBulkReceiving;
                    AnomalyMessaging.BulkSessionExpected = session => IsRequestedPhase && session == _sessionId;
                },
                () =>
                {
                    AnomalyMessaging.BulkReceiveExpected = null;
                    AnomalyMessaging.BulkSessionExpected = null;
                }),
            new InitializationStep(() => AnomalyMessaging.MessageReceived += OnMessageReceived,
                () => AnomalyMessaging.MessageReceived -= OnMessageReceived),
            new InitializationStep(() => ClientEvents.Connected += HandleConnected,
                () => ClientEvents.Connected -= HandleConnected),
            new InitializationStep(() => ClientEvents.Ready += HandleReady,
                () => ClientEvents.Ready -= HandleReady));
    }

    private static void HandleConnected(ClientConnectedEventArgs _)
    {
        if (NetworkServer.active) { FileTransferOverlay.Hide(); return; }
        FileTransferOverlay.Show();
        FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_Connecting));
    }

    private static void HandleReady(ClientReadyEventArgs _) => FileTransferOverlay.Hide();

    public static void Reset()
    {
        _state = State.Idle;
        Interlocked.Increment(ref _epoch);
        var worker = _worker;
        _worker = null;
        worker?.Dispose(); // Signals only; the disk owner cleans up streams.
        _manifestSnapshot = null;
        _manifestSha256 = null;
        _sessionId = 0;
        _selectedLaneMask = 0;
        _queueDeadline = 0;
        _receiveWindow = null;
        _consumedCounters = null;
        _counterBases = null;
        _manifest.Clear();
        _requested.Clear();
        _attempts.Clear();
        _buffers.Clear();
        Array.Clear(_releasedFrames, 0, _releasedFrames.Length);
        _releasedBytes = 0;
        _creditSequence = 0;
        _creditDirty = _progressDirty = false;
        _totalFiles = _completedFiles = _totalAssetsInManifest = 0;
        _totalBytes = _receivedBytes = 0;
        _lastActivityUtc = _lastCreditUtc = _lastProgressUtc = default;
        InvokeBestEffort(FileTransferOverlay.Hide, "reset overlay cleanup");
    }

    internal static void Shutdown()
    {
        Reset();
        Initializer.Reset();
    }

    internal static bool IsFileApproved(byte[] hash, FileContentKind expectedKind) =>
        _state == State.Complete && _manifestSnapshot != null && _manifestSnapshot.Contains(hash, expectedKind);

    private static void OnMessageReceived(IAnomalyMessage message)
    {
        switch (message)
        {
            case FileManifestMessage manifest: HandleManifest(manifest); break;
            case FileChunkMessage chunk: HandleChunk(chunk); break;
            case FileTransferCompleteMessage complete: HandleFileComplete(complete); break;
            case FileTransferStatusMessage status: HandleStatus(status); break;
        }
    }

    internal static void ReceiveBootstrap(IAnomalyMessage message) => OnMessageReceived(message);

    private static void HandleManifest(FileManifestMessage manifest)
    {
        if (_state != State.Idle) { SafeLogWarning("FileTransfer: Ignoring duplicate/out-of-state manifest."); return; }
        _sessionId = manifest.SessionId;
        _manifestSha256 = (byte[])manifest.ManifestSha256.Clone();
        if (manifest.BulkProfileVersion != _localProfile || !BulkTransferProtocol.IsValidMask(manifest.BulkLaneMask))
        { Abort("manifest used an unsupported bulk profile"); return; }
        if (manifest.BulkCounterBases == null || manifest.BulkCounterBases.Length != BulkTransferProtocol.MaxDomains)
        { Abort("manifest did not declare the authenticated bulk counter bases"); return; }
        _counterBases = (uint[])manifest.BulkCounterBases.Clone();
        _selectedLaneMask = TransportAvailable() ? BulkTransferProtocol.SelectLaneMask(_localLaneMask, manifest.BulkLaneMask) : 0;
        try { _manifestSnapshot = new AssetManifestSnapshot(manifest.Files); }
        catch (Exception exception) { Abort("manifest authorization snapshot was invalid: " + exception.Message); return; }
        _totalAssetsInManifest = manifest.Files.Count;
        foreach (var file in manifest.Files)
        {
            if (file.Kind != FileContentKind.Bundle && file.Kind != FileContentKind.PackIndex &&
                file.Kind != FileContentKind.ScriptIndex && file.Kind != FileContentKind.LuaSource)
            { Abort("manifest contained an unsupported content kind"); return; }
            string key = FileCache.ToHex(file.Hash);
            if (_manifest.ContainsKey(key)) { Abort("manifest repeated a content hash"); return; }
            _manifest.Add(key, new FileReceiveFile { Key = key, Hash = (byte[])file.Hash.Clone(), Path = file.Path, Size = file.Size });
        }
        _lastActivityUtc = DateTime.UtcNow;
        _state = State.AwaitingConsent;
        InvokeIsolated(ManifestOffered, manifest, "ManifestOffered");
        if (_state == State.AwaitingConsent)
        {
            InvokeBestEffort(FileTransferOverlay.Show, "consent overlay");
            InvokeBestEffort(() => FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_AwaitingConsent)), "consent status");
        }
    }

    internal static bool ApprovePendingManifest(byte[] expectedManifestSha256)
    {
        if (_state != State.AwaitingConsent || !HashesEqual(expectedManifestSha256, _manifestSha256)) return false;
        _state = State.Preparing;
        _lastActivityUtc = DateTime.UtcNow;
        long epoch = _epoch;
        ulong session = _sessionId;
        try
        {
            // Existing cache verification may hash full blobs, so it also runs on the disk owner.
            _worker = new FileReceiveWorker(epoch, session, new List<FileReceiveFile>(_manifest.Values),
                FileCache.HasFile, file => new ChunkBuffer(session, file.Hash, file.Size, file.Path));
            return true;
        }
        catch (Exception exception) { Abort("receive storage could not start: " + exception.GetType().Name); return false; }
    }

    internal static void DeclinePendingManifest(byte[] expectedManifestSha256)
    {
        if (_state != State.AwaitingConsent || !HashesEqual(expectedManifestSha256, _manifestSha256)) return;
        _state = State.Aborted;
        _manifestSnapshot = null;
        _requested.Clear();
        InvokeIsolated(TransferFinished, false, "TransferFinished");
        InvokeBestEffort(FileTransferOverlay.Hide, "decline overlay cleanup");
    }

    /// <summary>Pumps storage results on the game thread; performs no file I/O.</summary>
    internal static void Tick()
    {
        var worker = _worker;
        if (worker == null || (_state != State.Preparing && !IsRequestedPhase)) return;
        if (IsRequestedPhase && _requested.Count != 0 && !TransportAvailable())
        { Abort("the active transport no longer owns the selected bulk pool"); return; }
        for (int count = 0; count < BulkTransferProtocol.MaxWindowFrames + 8 && worker.TryRead(out var result); count++)
        {
            if (!ReferenceEquals(worker, _worker)) return;
            if (result.Epoch != _epoch || result.SessionId != _sessionId) continue;
            if (result.Kind == FileReceiveResultKind.Failed)
            {
                if (result.File != null) SendAck(result.File, 1);
                Abort("receive storage failed: " + result.Error);
                return;
            }
            if (result.Kind == FileReceiveResultKind.Prepared)
            {
                if (_state == State.Preparing)
                    try { StartReceiving(result.Missing); }
                    catch (Exception exception) { Abort("file request failed: " + exception.GetType().Name); }
                continue;
            }
            var file = result.File;
            if (_state != State.Receiving || file == null || !_requested.Contains(file.Key) ||
                !_attempts.TryGetValue(file.Key, out byte attempt) || attempt != file.Attempt) continue;
            _lastActivityUtc = DateTime.UtcNow;
            if (result.Kind == FileReceiveResultKind.Written)
            {
                // Disk completion can arrive out of transport order. Return byte
                // storage credits independently, but advance the replay watermark
                // only through a contiguous run of authenticated frame counters.
                if (_consumedCounters == null || !_consumedCounters.TryConsume(result.Domain, result.FrameCounter))
                { Abort("receive storage returned an invalid bulk frame counter"); return; }
                _receivedBytes += result.Bytes;
                _releasedBytes = checked(_releasedBytes + (ulong)result.Bytes);
                _releasedFrames[result.Domain] = checked(_releasedFrames[result.Domain] + 1);
                _creditDirty = _progressDirty = true;
                _progressPath = file.Path;
                if (_receivedBytes > _totalBytes) { Abort("received bytes exceeded the manifest budget"); return; }
            }
            else if (result.Kind == FileReceiveResultKind.Finished) FinishFile(file, result.Success, result.Error);
        }
        if (!ReferenceEquals(worker, _worker)) return;
        if (_state == State.Preparing && DateTime.UtcNow - _lastActivityUtc > TransferTimeout)
        { Abort("cache verification exceeded the preparation deadline"); return; }
        if (_state == State.AwaitingAdmission && DateTime.UtcNow - _lastActivityUtc > TransferTimeout)
        { Abort("server did not acknowledge the file-transfer admission request"); return; }
        if (_state == State.Queued && MonotonicSeconds() >= _queueDeadline)
        { Abort("bounded file-transfer admission wait expired"); return; }
        if (_state != State.Receiving) return;
        FlushCredit(false);
        if (_state != State.Receiving) return;
        PublishProgress();
        if (DateTime.UtcNow - _lastActivityUtc > TransferTimeout)
            Abort($"no valid transfer progress for {TransferTimeout.TotalSeconds:F0} seconds");
    }

    private static void StartReceiving(List<FileReceiveFile> missing)
    {
        _totalFiles = missing.Count;
        _totalBytes = 0;
        foreach (var file in missing)
        {
            _totalBytes += file.Size;
            if (_totalBytes > FileManifestMessage.MaxAggregateBytes) { Abort("manifest exceeded the download budget"); return; }
            _requested.Add(file.Key);
            _attempts[file.Key] = 0;
        }
        if (missing.Count != 0 && (_selectedLaneMask == 0 || !TransportAvailable()))
        { Abort("no common owned bulk transport lanes are available for the requested files"); return; }
        _receiveWindow = missing.Count == 0 ? null : new BulkCreditWindow(BulkTransferProtocol.MaxWindowBytes,
            BulkTransferProtocol.MaxWindowFrames, BulkTransferProtocol.DomainCount(_selectedLaneMask), _counterBases);
        _consumedCounters = missing.Count == 0 ? null :
            new BulkConsumptionTracker(_counterBases, BulkTransferProtocol.DomainCount(_selectedLaneMask));
        _state = missing.Count == 0 ? State.Receiving : State.AwaitingAdmission;
        _lastActivityUtc = DateTime.UtcNow;
        long epoch = _epoch;
        foreach (var file in _manifest.Values)
        {
            if (!_requested.Contains(file.Key)) InvokeIsolated(FileVerified, (byte[])file.Hash.Clone(), "FileVerified");
            if (_epoch != epoch || !IsRequestedPhase) return;
        }
        bool sent = AnomalyMessaging.TrySend(new FileRequestMessage
        {
            SessionId = _sessionId, RequestedHashes = missing.Select(file => file.Hash).ToList(),
            BulkProfileVersion = _localProfile, BulkLaneMask = missing.Count == 0 ? 0 : _selectedLaneMask,
            ReceiveWindowBytes = missing.Count == 0 ? 0 : BulkTransferProtocol.MaxWindowBytes,
            ReceiveWindowFrames = missing.Count == 0 ? (ushort)0 : BulkTransferProtocol.MaxWindowFrames
        });
        if (_epoch != epoch || !IsRequestedPhase) return;
        if (!sent) { Abort("approved file request could not be delivered over the authenticated transport"); return; }
        if (missing.Count == 0) CompleteTransfer();
    }

    private static bool TransportAvailable()
    {
        try { return _transportAvailable == null || _transportAvailable(); }
        catch { return false; }
    }

    private static double MonotonicSeconds() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    private static void HandleStatus(FileTransferStatusMessage status)
    {
        if (status.SessionId != _sessionId || !IsRequestedPhase) return;
        if (status.Status == FileTransferStatus.Queued)
        {
            // Admission control and bulk use independent reliable lanes. A valid
            // chunk can establish admission before the older queued status arrives.
            if (_state == State.Receiving || _state == State.Queued) return;
            if (status.QueueWaitMilliseconds == 0 || status.QueueWaitMilliseconds > FileTransferStatusMessage.MaxQueueWaitMilliseconds ||
                status.QueuePosition == 0 || status.QueuePosition > 32)
            { Abort("server supplied an invalid bounded queue admission"); return; }
            _state = State.Queued;
            _queueDeadline = MonotonicSeconds() + status.QueueWaitMilliseconds / 1000d;
            InvokeBestEffort(() => FileTransferOverlay.SetStatus("Waiting for content download capacity…"), "queue status");
        }
        else if (status.Status == FileTransferStatus.Admitted)
        {
            if (status.QueueWaitMilliseconds != 0 || status.QueuePosition != 0)
            { Abort("server supplied invalid transfer admission fields"); return; }
            if (_state == State.Receiving) return; // A late marker must not renew inactivity.
            if (_state == State.Queued && MonotonicSeconds() >= _queueDeadline)
            { Abort("transfer admission arrived after its bounded queue wait"); return; }
            _state = State.Receiving;
            _lastActivityUtc = DateTime.UtcNow;
        }
        else Abort("server supplied an unknown file-transfer admission state");
    }

    private static ReceiveFile GetReceiveFile(byte[] hash, byte attempt)
    {
        string key = FileCache.ToHex(hash);
        if (!_requested.Contains(key) || !_manifest.TryGetValue(key, out var entry)) return null;
        byte expected = _attempts[key];
        if (attempt < expected) return null;
        if (attempt != expected) { Abort("server sent an unrequested future file attempt"); return null; }
        if (_buffers.TryGetValue(key, out var existing)) return existing;
        if (_buffers.Count >= BulkTransferProtocol.MaxFilesInFlight)
        { Abort("server exceeded the four-file receive pipeline"); return null; }
        var file = new FileReceiveFile { Key = key, Hash = entry.Hash, Path = entry.Path, Size = entry.Size, Attempt = attempt };
        var receive = new ReceiveFile { File = file,
            Received = new bool[(entry.Size + FileChunkMessage.MaxChunkBytes - 1) / FileChunkMessage.MaxChunkBytes] };
        _buffers.Add(key, receive);
        return receive;
    }

    private static void HandleChunk(FileChunkMessage chunk)
    {
        if (!IsRequestedPhase || chunk.SessionId != _sessionId) return;
        if (!TransportAvailable()) { Abort("the active transport no longer owns the selected bulk pool"); return; }
        if (_state == State.Queued && MonotonicSeconds() >= _queueDeadline)
        { Abort("bulk data arrived after its bounded queue wait"); return; }
        if (chunk.ProfileVersion != BulkTransferProtocol.ProfileVersion ||
            chunk.Domain >= BulkTransferProtocol.DomainCount(_selectedLaneMask))
        { Abort("chunk used a domain outside the negotiated bulk pool"); return; }
        var receive = GetReceiveFile(chunk.Hash, chunk.Attempt);
        if (receive == null) return;
        int total = receive.Received.Length;
        long offset = (long)chunk.ChunkIndex * FileChunkMessage.MaxChunkBytes;
        int length = (int)Math.Min(FileChunkMessage.MaxChunkBytes, receive.File.Size - offset);
        if (chunk.TotalChunks != total || chunk.ChunkIndex >= total || chunk.Data == null || chunk.Data.Length != length)
        { Abort("chunk bounds did not match the approved file"); return; }
        // Replayed chunks do not consume a second storage slot or earn credits.
        if (receive.Received[chunk.ChunkIndex]) return;
        if (!_receiveWindow.CanReceive(chunk.Domain, chunk.FrameCounter, chunk.Data.Length))
        { Abort("server exceeded the granted receive window"); return; }
        if (!_worker.TryEnqueue(receive.File, chunk.ChunkIndex, chunk.TotalChunks, chunk.Data, chunk.Domain, chunk.FrameCounter))
        { Abort("bounded receive storage queue exceeded its allowance"); return; }
        _receiveWindow.RecordReceived(chunk.Domain, chunk.FrameCounter, chunk.Data.Length);
        receive.Received[chunk.ChunkIndex] = true;
        _state = State.Receiving;
        _lastActivityUtc = DateTime.UtcNow;
    }

    private static void HandleFileComplete(FileTransferCompleteMessage complete)
    {
        if (!IsRequestedPhase || complete.SessionId != _sessionId) return;
        if (!TransportAvailable()) { Abort("the active transport no longer owns the selected bulk pool"); return; }
        if (_state == State.Queued && MonotonicSeconds() >= _queueDeadline)
        { Abort("file completion arrived after its bounded queue wait"); return; }
        var receive = GetReceiveFile(complete.Hash, complete.Attempt);
        if (receive == null || receive.CompletionReceived) return;
        _state = State.Receiving;
        _lastActivityUtc = DateTime.UtcNow;
        receive.CompletionReceived = true;
        if (!_worker.TryEnqueue(receive.File, 0, 0, null, 0)) Abort("file completion exceeded the receive queue allowance");
    }

    private static void FinishFile(FileReceiveFile file, bool success, string error)
    {
        // Intermediate ACKs can advance the four-file pipeline while credits
        // remain coalesced. Flush the final transaction before publishing readiness.
        if (!FlushCredit(success && _completedFiles + 1 == _totalFiles)) return;
        long epoch = _epoch;
        _buffers.Remove(file.Key);
        if (!success)
        {
            _receivedBytes = Math.Max(0, _receivedBytes - file.Size);
            if (error != null || file.Attempt >= BulkTransferProtocol.MaxRetries)
            { SendAck(file, 1); Abort("file failed integrity verification after all permitted attempts"); return; }
            _attempts[file.Key] = (byte)(file.Attempt + 1);
            if (!SendAck(file, 1)) Abort("integrity-failure ACK could not be delivered");
            return;
        }
        _requested.Remove(file.Key);
        _completedFiles++;
        if (!SendAck(file, 0)) { Abort("success ACK could not be delivered over the authenticated transport"); return; }
        if (_epoch != epoch || _state != State.Receiving) return;
        InvokeIsolated(FileVerified, (byte[])file.Hash.Clone(), "FileVerified");
        if (_epoch != epoch || _state != State.Receiving) return;
        if (_completedFiles == _totalFiles) CompleteTransfer();
    }

    private static bool FlushCredit(bool force)
    {
        if (!_creditDirty) return true;
        var now = DateTime.UtcNow;
        if (!force && now - _lastCreditUtc < TimeSpan.FromMilliseconds(25)) return true;
        if (_creditSequence == uint.MaxValue) { Abort("bulk credit sequence exhausted"); return false; }
        var credit = new FileCreditMessage { SessionId = _sessionId, CreditSequence = ++_creditSequence,
            ReleasedBytes = _releasedBytes, ReleasedFrames = (uint[])_releasedFrames.Clone(),
            ConsumedCounters = _consumedCounters.Snapshot() };
        var window = _receiveWindow;
        long epoch = _epoch;
        bool sent;
        try { sent = AnomalyMessaging.TrySend(credit); }
        catch { sent = false; }
        if (_epoch != epoch || _state != State.Receiving) return false;
        if (!sent || !window.TryApplyCredit(credit, out _))
        { Abort("consumed receive credits could not be delivered"); return false; }
        _lastCreditUtc = now;
        _creditDirty = false;
        return true;
    }

    private static bool SendAck(FileReceiveFile file, byte status)
    {
        try { return AnomalyMessaging.TrySend(new FileAckMessage { SessionId = _sessionId, Hash = file.Hash,
            Attempt = file.Attempt, Status = status }); }
        catch { return false; }
    }

    private static void PublishProgress()
    {
        var now = DateTime.UtcNow;
        if (!_progressDirty || now - _lastProgressUtc < TimeSpan.FromMilliseconds(100)) return;
        _progressDirty = false;
        _lastProgressUtc = now;
        float percent = _totalBytes == 0 ? 0 : Math.Min(1f, Math.Max(0f, (float)_receivedBytes / _totalBytes));
        InvokeBestEffort(() => FileTransferOverlay.UpdateProgress(_progressPath, _completedFiles + 1, _totalFiles, percent), "progress overlay");
        InvokeBestEffort(() => FileTransferEvents.OnProgress(new FileTransferProgressEventArgs(_progressPath,
            _completedFiles, _totalFiles, _receivedBytes, _totalBytes, percent)), "progress event");
    }

    private static void CompleteTransfer()
    {
        _state = State.Complete;
        long epoch = _epoch;
        var worker = _worker;
        _worker = null;
        worker?.Dispose();
        InvokeIsolated(TransferFinished, true, "TransferFinished");
        if (_epoch != epoch || _state != State.Complete) return;
        InvokeBestEffort(FileTransferOverlay.Hide, "completion overlay cleanup");
        // Maintenance is independent of content readiness and never blocks the game thread.
        _ = Task.Run(() => InvokeBestEffort(() => FileCache.Evict(500L * 1024 * 1024), "cache maintenance"));
        InvokeBestEffort(() => FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, _completedFiles)), "completion event");
    }

    private static void Abort(string reason)
    {
        if (_state == State.Aborted) return;
        ulong sessionId = _sessionId;
        int completedFiles = _completedFiles;
        _state = State.Aborted;
        Interlocked.Increment(ref _epoch);
        var worker = _worker;
        _worker = null;
        worker?.Dispose();
        _manifestSnapshot = null;
        _manifestSha256 = null;
        _requested.Clear();
        _buffers.Clear();
        // Subscribers may synchronously reset this transaction and present a
        // replacement offer. Finish old UI/log cleanup before handing them control.
        InvokeBestEffort(FileTransferOverlay.Hide, "abort overlay cleanup");
        InvokeBestEffort(() => MelonLogger.Error($"FileTransfer: Aborted session {sessionId:x16}: {reason}."), "abort log");
        InvokeIsolated(TransferFinished, false, "TransferFinished");
        InvokeBestEffort(() => FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(false, completedFiles)), "failure event");
    }

    private static bool HashesEqual(byte[] first, byte[] second)
    {
        if (first == null || second == null || first.Length != second.Length) return false;
        int difference = 0;
        for (int index = 0; index < first.Length; index++) difference |= first[index] ^ second[index];
        return difference == 0;
    }

    private static void InvokeIsolated<T>(Action<T> handlers, T value, string name)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<T>)handler)(value); }
            catch (Exception exception) { InvokeBestEffort(() => MelonLogger.Error($"FileTransfer.{name} subscriber failed: {exception}"), "subscriber log"); }
        }
    }

    private static void InvokeBestEffort(Action action, string name)
    {
        try { action(); }
        catch (Exception exception) { try { MelonLogger.Error($"FileTransfer: {name} failed: {exception}"); } catch { } }
    }

    private static void SafeLogWarning(string message) { try { MelonLogger.Warning(message); } catch { } }
}
