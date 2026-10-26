using System.Collections;
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
using UnityEngine;

namespace Anomaly.Client.FileTransfer;

public static class FileTransferClient
{
    private enum State
    {
        Idle,
        AwaitingConsent,
        Receiving,
        Complete,
        Aborted
    }

    private sealed class ManifestEntry
    {
        public byte[] Hash;
        public string Path;
        public uint Size;
    }

    private const int MaxInFlightFiles = 4;
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(30);

    private static State _state = State.Idle;
    private static ulong _sessionId;
    private static readonly Dictionary<string, ChunkBuffer> _buffers = new();
    private static readonly Dictionary<string, ManifestEntry> _manifest = new();
    private static readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _completionReceived = new(StringComparer.Ordinal);
    private static int _totalFiles;
    private static int _completedFiles;
    private static long _totalBytes;
    private static long _receivedBytes;
    private static int _totalAssetsInManifest;
    private static DateTime _lastActivityUtc;
    private static byte[] _manifestSha256;
    private static AssetManifestSnapshot _manifestSnapshot;
    private static readonly TransactionalInitializer Initializer = new();

    internal static event Action<FileManifestMessage> ManifestOffered;
    internal static event Action<bool> TransferFinished;

    public static void Init()
    {
        Initializer.Initialize(
            new InitializationStep(
                () => AnomalyMessaging.MessageReceived += OnMessageReceived,
                () => AnomalyMessaging.MessageReceived -= OnMessageReceived),
            new InitializationStep(
                () => ClientEvents.Connected += HandleConnected,
                () => ClientEvents.Connected -= HandleConnected),
            new InitializationStep(
                () => ClientEvents.Ready += HandleReady,
                () => ClientEvents.Ready -= HandleReady));
    }

    private static void HandleConnected(ClientConnectedEventArgs _)
    {
        if (NetworkServer.active)
        {
            FileTransferOverlay.Hide();
            return;
        }

        FileTransferOverlay.Show();
        FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_Connecting));
    }

    private static void HandleReady(ClientReadyEventArgs _) => FileTransferOverlay.Hide();

    public static void Reset()
    {
        // Revoke authorization before any best-effort file/UI cleanup can fail.
        _state = State.Idle;
        _manifestSnapshot = null;
        _manifestSha256 = null;
        _sessionId = 0;
        _manifest.Clear();
        _requested.Clear();
        _completionReceived.Clear();
        _totalFiles = 0;
        _completedFiles = 0;
        _totalBytes = 0;
        _receivedBytes = 0;
        _totalAssetsInManifest = 0;
        _lastActivityUtc = default;
        try { DisposeBuffers(); }
        catch (Exception exception)
        {
            MelonLogger.Error($"FileTransfer: Failed to dispose one or more receive buffers during reset: {exception}");
            _buffers.Clear();
        }
        try { FileTransferOverlay.Hide(); }
        catch (Exception exception)
        {
            MelonLogger.Error($"FileTransfer: Failed to hide the transfer overlay during reset: {exception}");
        }
    }

    internal static bool IsFileApproved(byte[] hash, FileContentKind expectedKind) =>
        _state == State.Complete &&
        _manifestSnapshot != null &&
        _manifestSnapshot.Contains(hash, expectedKind);

    private static void OnMessageReceived(IAnomalyMessage msg)
    {
        switch (msg)
        {
            case FileManifestMessage manifest:
                HandleManifest(manifest);
                break;
            case FileChunkMessage chunk:
                HandleChunk(chunk);
                break;
            case FileTransferCompleteMessage complete:
                HandleFileComplete(complete);
                break;
        }
    }

    internal static void ReceiveBootstrap(IAnomalyMessage message) => OnMessageReceived(message);

    private static void HandleManifest(FileManifestMessage manifest)
    {
        if (_state != State.Idle)
        {
            MelonLogger.Warning("FileTransfer: Ignoring duplicate/out-of-state manifest.");
            return;
        }

        _sessionId = manifest.SessionId;
        _manifestSha256 = (byte[])manifest.ManifestSha256.Clone();
        try
        {
            _manifestSnapshot = new AssetManifestSnapshot(manifest.Files);
        }
        catch (Exception exception)
        {
            Abort("manifest authorization snapshot was invalid: " + exception.Message);
            return;
        }
        _totalAssetsInManifest = manifest.Files.Count;
        _manifest.Clear();
        _requested.Clear();
        _completionReceived.Clear();
        _totalBytes = 0;
        _receivedBytes = 0;
        _completedFiles = 0;
        foreach (var file in manifest.Files)
        {
            if (file.Kind != FileContentKind.Bundle && file.Kind != FileContentKind.PackIndex &&
                file.Kind != FileContentKind.ScriptIndex && file.Kind != FileContentKind.LuaSource)
            {
                Abort("manifest contained an unsupported content kind");
                return;
            }

            string hex = FileCache.ToHex(file.Hash);
            if (_manifest.ContainsKey(hex))
            {
                MelonLogger.Warning($"FileTransfer: Manifest repeated hash {hex}; using the first entry.");
                continue;
            }

            _manifest[hex] = new ManifestEntry
            {
                Hash = (byte[])file.Hash.Clone(),
                Path = file.Path,
                Size = file.Size
            };

        }
        _lastActivityUtc = DateTime.UtcNow;
        _state = State.AwaitingConsent;
        InvokeIsolated(ManifestOffered, manifest, "ManifestOffered");
        if (_state == State.AwaitingConsent)
        {
            InvokeBestEffort(FileTransferOverlay.Show, "consent overlay");
            InvokeBestEffort(
                () => FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_AwaitingConsent)),
                "consent overlay status");
        }
    }

    internal static bool ApprovePendingManifest(byte[] expectedManifestSha256)
    {
        if (_state != State.AwaitingConsent ||
            !HashesEqual(expectedManifestSha256, _manifestSha256))
            return false;

        var missing = new List<byte[]>();
        _requested.Clear();
        _totalBytes = 0;
        foreach (ManifestEntry file in _manifest.Values)
        {
            if (FileCache.HasFile(file.Hash))
                continue;
            _totalBytes += file.Size;
            if (_totalBytes > FileManifestMessage.MaxAggregateBytes)
            {
                Abort("manifest exceeded the aggregate download budget");
                return false;
            }
            string hex = FileCache.ToHex(file.Hash);
            _requested.Add(hex);
            missing.Add(file.Hash);
        }

        _totalFiles = missing.Count;
        if (!AnomalyMessaging.TrySend(new FileRequestMessage
        {
            SessionId = _sessionId,
            RequestedHashes = missing,
        }))
        {
            Abort("approved file request could not be delivered over the authenticated transport");
            return false;
        }

        if (missing.Count == 0)
        {
            _state = State.Complete;
            InvokeIsolated(TransferFinished, true, "TransferFinished");
            InvokeBestEffort(() => MelonLogger.Msg("FileTransfer: Approved manifest is already cached."), "cache-hit log");
            InvokeBestEffort(FileTransferOverlay.Hide, "cache-hit overlay cleanup");
            InvokeBestEffort(
                () => FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, 0)),
                "cache-hit completion event");
            return true;
        }

        _state = State.Receiving;
        _lastActivityUtc = DateTime.UtcNow;
        try
        {
            MelonCoroutines.Start(WatchTimeout(_sessionId));
        }
        catch (Exception exception)
        {
            Abort("the transfer timeout watchdog could not start: " + exception.Message);
            return false;
        }
        InvokeBestEffort(
            () => MelonLogger.Msg($"FileTransfer: Requesting {missing.Count} approved files ({_totalBytes} bytes), session={_sessionId:x16}."),
            "request log");
        return true;
    }

    internal static void DeclinePendingManifest(byte[] expectedManifestSha256)
    {
        if (_state != State.AwaitingConsent ||
            !HashesEqual(expectedManifestSha256, _manifestSha256))
            return;
        DisposeBuffers();
        _requested.Clear();
        _manifestSnapshot = null;
        _state = State.Aborted;
        InvokeIsolated(TransferFinished, false, "TransferFinished");
        InvokeBestEffort(FileTransferOverlay.Hide, "decline overlay cleanup");
    }

    private static void HandleChunk(FileChunkMessage chunk)
    {
        if (_state != State.Receiving || chunk.SessionId != _sessionId)
        {
            MelonLogger.Warning("FileTransfer: Ignoring unsolicited or stale chunk.");
            return;
        }

        string hex = FileCache.ToHex(chunk.Hash);
        if (!_requested.Contains(hex) || !_manifest.TryGetValue(hex, out var entry))
        {
            MelonLogger.Warning($"FileTransfer: Ignoring chunk for unrequested hash {hex}.");
            return;
        }

        if (!_buffers.TryGetValue(hex, out var buffer))
        {
            if (_buffers.Count >= MaxInFlightFiles)
            {
                Abort($"server exceeded the {MaxInFlightFiles}-file in-flight limit");
                return;
            }

            try
            {
                buffer = new ChunkBuffer(_sessionId, entry.Hash, entry.Size, entry.Path);
                _buffers[hex] = buffer;
            }
            catch (Exception ex)
            {
                Abort($"could not allocate bounded receive buffer: {ex.Message}");
                return;
            }
        }

        bool accepted;
        bool duplicate;
        string error;
        try
        {
            accepted = buffer.TryAddChunk(
                chunk.ChunkIndex,
                chunk.TotalChunks,
                chunk.Data,
                out duplicate,
                out error);
        }
        catch (Exception exception)
        {
            Abort($"could not write the bounded receive buffer for {hex}: {exception.GetType().Name}");
            return;
        }

        if (!accepted)
        {
            if (!duplicate)
                Abort($"invalid chunk for {hex}: {error}");
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
        _receivedBytes += chunk.Data.Length;
        if (_receivedBytes > _totalBytes)
        {
            Abort("received bytes exceeded the manifest budget");
            return;
        }

        // ReliableUnordered may deliver the completion marker before the final
        // chunk. Keep the marker and finalize only after the manifest-sized
        // buffer is complete instead of sending a false integrity failure.
        if (buffer.IsComplete && _completionReceived.Contains(hex))
        {
            FinalizeCompletedFile(hex, buffer);
            return;
        }

        float percent = _totalBytes > 0
            ? Math.Min(1f, Math.Max(0f, (float)_receivedBytes / _totalBytes))
            : 0f;
        InvokeBestEffort(
            () => FileTransferOverlay.UpdateProgress(entry.Path, _completedFiles + 1, _totalFiles, percent),
            "transfer progress overlay");
        InvokeBestEffort(
            () => FileTransferEvents.OnProgress(new FileTransferProgressEventArgs(
                entry.Path,
                _completedFiles,
                _totalFiles,
                _receivedBytes,
                _totalBytes,
                percent)),
            "transfer progress event");
    }

    private static void HandleFileComplete(FileTransferCompleteMessage complete)
    {
        if (_state != State.Receiving || complete.SessionId != _sessionId)
        {
            MelonLogger.Warning("FileTransfer: Ignoring unsolicited or stale completion.");
            return;
        }

        string hex = FileCache.ToHex(complete.Hash);
        if (!_requested.Contains(hex) || !_manifest.ContainsKey(hex))
        {
            bool acknowledgementDelivered;
            try { acknowledgementDelivered = SendAck(complete.Hash, 1); }
            catch { acknowledgementDelivered = false; }
            if (!acknowledgementDelivered)
            {
                Abort("failure ACK could not be delivered over the authenticated transport");
                return;
            }
            SafeLogWarning($"FileTransfer: Completion for unknown/inactive file {hex}.");
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
        if (!_completionReceived.Add(hex))
        {
            MelonLogger.Warning($"FileTransfer: Ignoring duplicate completion for {hex}.");
            return;
        }

        if (!_buffers.TryGetValue(hex, out var buffer) || !buffer.IsComplete)
        {
            MelonLogger.Msg(
                $"FileTransfer: Completion for {hex} arrived before all reliable-unordered chunks; waiting for the bounded buffer.");
            return;
        }

        FinalizeCompletedFile(hex, buffer);
    }

    private static void FinalizeCompletedFile(string hex, ChunkBuffer buffer)
    {
        if (buffer == null || !_requested.Contains(hex) || !_completionReceived.Contains(hex))
            return;

        bool committed = false;
        Exception commitFailure = null;
        if (buffer.IsComplete)
        {
            try { committed = buffer.CommitToCache(); }
            catch (Exception exception) { commitFailure = exception; }
        }

        if (!buffer.IsComplete || !committed)
        {
            long failedBytes = buffer.ReceivedBytes;
            byte[] failedHash = buffer.Hash;
            _receivedBytes = Math.Max(0, _receivedBytes - failedBytes);
            buffer.Dispose();
            _buffers.Remove(hex);
            _completionReceived.Remove(hex);

            bool acknowledgementDelivered;
            try { acknowledgementDelivered = SendAck(failedHash, 1); }
            catch { acknowledgementDelivered = false; }
            if (commitFailure != null)
            {
                Abort(
                    "cache commit failed" +
                    (acknowledgementDelivered
                        ? $": {commitFailure.GetType().Name}"
                        : $" and the failure ACK could not be delivered: {commitFailure.GetType().Name}"));
                return;
            }
            if (!acknowledgementDelivered)
            {
                Abort("integrity-failure ACK could not be delivered over the authenticated transport");
                return;
            }
            SafeLogWarning($"FileTransfer: File {hex} failed size or SHA-256 verification.");
            return;
        }

        string path = buffer.OriginalPath;
        uint expectedSize = buffer.ExpectedSize;
        buffer.Dispose();
        _buffers.Remove(hex);
        _requested.Remove(hex);
        _completionReceived.Remove(hex);
        _completedFiles++;

        if (!SendAck(buffer.Hash, 0))
        {
            Abort("success ACK could not be delivered over the authenticated transport");
            return;
        }
        if (_completedFiles < _totalFiles)
        {
            InvokeBestEffort(
                () => MelonLogger.Msg($"FileTransfer: File '{path}' received and verified ({expectedSize} bytes)."),
                "verified-file log");
            return;
        }

        _state = State.Complete;
        InvokeIsolated(TransferFinished, true, "TransferFinished");
        InvokeBestEffort(
            () => MelonLogger.Msg($"FileTransfer: File '{path}' received and verified ({expectedSize} bytes)."),
            "verified-file log");
        InvokeBestEffort(FileTransferOverlay.Hide, "completion overlay cleanup");
        InvokeBestEffort(() => FileCache.Evict(500 * 1024 * 1024), "completion cache maintenance");
        InvokeBestEffort(
            () => FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, _completedFiles)),
            "completion event");
        InvokeBestEffort(
            () => MelonLogger.Msg($"FileTransfer: All {_completedFiles} requested files received."),
            "completion log");
    }

    private static bool SendAck(byte[] hash, byte status)
    {
        try
        {
            return AnomalyMessaging.TrySend(new FileAckMessage
            {
                SessionId = _sessionId,
                Hash = hash,
                Status = status
            });
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerator WatchTimeout(ulong sessionId)
    {
        while (_state == State.Receiving && _sessionId == sessionId)
        {
            yield return new WaitForSeconds(1f);
            if (_state == State.Receiving &&
                _sessionId == sessionId &&
                DateTime.UtcNow - _lastActivityUtc > TransferTimeout)
            {
                Abort($"no valid transfer progress for {TransferTimeout.TotalSeconds:F0} seconds");
                yield break;
            }
        }
    }

    private static void Abort(string reason)
    {
        if (_state == State.Aborted)
            return;

        // Revoke all authorization-bearing state before any diagnostics or UI
        // operation. TransferFinished drives the content fail-closed disconnect,
        // so it must run before best-effort presentation callbacks.
        ulong abortedSessionId = _sessionId;
        int completedFiles = _completedFiles;
        _state = State.Aborted;
        _manifestSnapshot = null;
        _manifestSha256 = null;
        _manifest.Clear();
        _requested.Clear();
        _completionReceived.Clear();
        DisposeBuffers();

        InvokeIsolated(TransferFinished, false, "TransferFinished");
        try { FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(false, completedFiles)); }
        catch (Exception exception)
        {
            try { MelonLogger.Error($"FileTransfer: failure event dispatch failed: {exception}"); }
            catch { }
        }
        try { FileTransferOverlay.Hide(); }
        catch (Exception exception)
        {
            try { MelonLogger.Error($"FileTransfer: failed to hide the aborted transfer overlay: {exception}"); }
            catch { }
        }
        try { MelonLogger.Error($"FileTransfer: Aborted session {abortedSessionId:x16}: {reason}."); }
        catch { }
    }

    private static void DisposeBuffers()
    {
        foreach (var buffer in new List<ChunkBuffer>(_buffers.Values))
        {
            try { buffer.Dispose(); }
            catch (Exception exception)
            {
                try { MelonLogger.Error($"FileTransfer: Failed to dispose a receive buffer: {exception}"); }
                catch { /* later buffers still need cleanup */ }
            }
        }
        _buffers.Clear();
    }

    private static bool HashesEqual(byte[] first, byte[] second)
    {
        if (first == null || second == null || first.Length != second.Length)
            return false;
        int difference = 0;
        for (int index = 0; index < first.Length; index++)
            difference |= first[index] ^ second[index];
        return difference == 0;
    }

    private static void InvokeIsolated<T>(Action<T> handlers, T value, string name)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<T>)handler)(value); }
            catch (Exception exception)
            {
                try { MelonLogger.Error($"FileTransfer.{name} subscriber failed: {exception}"); }
                catch { /* logging must not suppress later subscribers */ }
            }
        }
    }

    private static void InvokeBestEffort(Action action, string name)
    {
        try { action(); }
        catch (Exception exception)
        {
            try { MelonLogger.Error($"FileTransfer: {name} failed: {exception}"); }
            catch { }
        }
    }

    private static void SafeLogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { }
    }
}
