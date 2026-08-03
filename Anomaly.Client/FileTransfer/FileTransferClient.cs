using System.Collections;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Internal.Localization;
using Anomaly.Client.Api.Localization;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Components;
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

    public static void Init()
    {
        AnomalyMessaging.MessageReceived += OnMessageReceived;

        ClientEvents.Connected += _ =>
        {
            if (NetworkServer.active)
            {
                FileTransferOverlay.Hide();
                return;
            }

            FileTransferOverlay.Show();
            FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_Connecting));
        };
        ClientEvents.Ready += _ => FileTransferOverlay.Hide();
    }

    public static void Reset()
    {
        DisposeBuffers();
        _state = State.Idle;
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
        FileTransferOverlay.Hide();
    }

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

    private static void HandleManifest(FileManifestMessage manifest)
    {
        if (_state != State.Idle)
        {
            MelonLogger.Warning("FileTransfer: Ignoring duplicate/out-of-state manifest.");
            return;
        }

        _sessionId = manifest.SessionId;
        _totalAssetsInManifest = manifest.Files.Count;
        _manifest.Clear();
        _requested.Clear();
        _completionReceived.Clear();
        _totalBytes = 0;
        _receivedBytes = 0;
        _completedFiles = 0;

        var missing = new List<byte[]>();
        foreach (var file in manifest.Files)
        {
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

            if (FileCache.HasFile(file.Hash))
                continue;

            _totalBytes += file.Size;
            if (_totalBytes > FileManifestMessage.MaxAggregateBytes)
            {
                Abort("manifest exceeded the aggregate download budget");
                return;
            }

            _requested.Add(hex);
            missing.Add(file.Hash);
        }

        _totalFiles = missing.Count;
        _lastActivityUtc = DateTime.UtcNow;

        AnomalyMessaging.Send(new FileRequestMessage
        {
            SessionId = _sessionId,
            RequestedHashes = missing
        });

        if (missing.Count == 0)
        {
            _state = State.Complete;
            MelonLogger.Msg("FileTransfer: All files already cached.");
            FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_LoadingLevel, _totalAssetsInManifest));
            FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, 0));
            return;
        }

        _state = State.Receiving;
        FileTransferOverlay.Show();
        MelonLogger.Msg($"FileTransfer: Requesting {missing.Count} files ({_totalBytes} bytes), session={_sessionId:x16}.");
        MelonCoroutines.Start(WatchTimeout(_sessionId));
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

        if (!buffer.TryAddChunk(
                chunk.ChunkIndex,
                chunk.TotalChunks,
                chunk.Data,
                out bool duplicate,
                out string error))
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

        float percent = _totalBytes > 0
            ? Math.Min(1f, Math.Max(0f, (float)_receivedBytes / _totalBytes))
            : 0f;
        FileTransferOverlay.UpdateProgress(entry.Path, _completedFiles + 1, _totalFiles, percent);
        FileTransferEvents.OnProgress(new FileTransferProgressEventArgs(
            entry.Path,
            _completedFiles,
            _totalFiles,
            _receivedBytes,
            _totalBytes,
            percent));

        // ReliableUnordered may deliver the completion marker before the final
        // chunk. Keep the marker and finalize only after the manifest-sized
        // buffer is complete instead of sending a false integrity failure.
        if (buffer.IsComplete && _completionReceived.Contains(hex))
            FinalizeCompletedFile(hex, buffer);
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
            MelonLogger.Warning($"FileTransfer: Completion for unknown/inactive file {hex}.");
            SendAck(complete.Hash, 1);
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

        if (!buffer.IsComplete || !buffer.CommitToCache())
        {
            MelonLogger.Warning($"FileTransfer: File {hex} failed size or SHA-256 verification.");
            SendAck(buffer.Hash, 1);
            _receivedBytes = Math.Max(0, _receivedBytes - buffer.ReceivedBytes);
            buffer.Dispose();
            _buffers.Remove(hex);
            _completionReceived.Remove(hex);
            return;
        }

        string path = buffer.OriginalPath;
        buffer.Dispose();
        _buffers.Remove(hex);
        _requested.Remove(hex);
        _completionReceived.Remove(hex);
        _completedFiles++;

        SendAck(buffer.Hash, 0);
        MelonLogger.Msg($"FileTransfer: File '{path}' received and verified ({buffer.ExpectedSize} bytes).");

        if (_completedFiles < _totalFiles)
            return;

        _state = State.Complete;
        FileTransferOverlay.SetStatus(Tr.Get(TrKeys.FileTransfer_LoadingLevel, _totalAssetsInManifest));
        FileCache.Evict(500 * 1024 * 1024);
        FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, _completedFiles));
        MelonLogger.Msg($"FileTransfer: All {_completedFiles} requested files received.");
    }

    private static void SendAck(byte[] hash, byte status)
    {
        AnomalyMessaging.Send(new FileAckMessage
        {
            SessionId = _sessionId,
            Hash = hash,
            Status = status
        });
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

        MelonLogger.Error($"FileTransfer: Aborted session {_sessionId:x16}: {reason}.");
        DisposeBuffers();
        _requested.Clear();
        _completionReceived.Clear();
        _state = State.Aborted;
        FileTransferOverlay.Hide();
        FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(false, _completedFiles));
    }

    private static void DisposeBuffers()
    {
        foreach (var buffer in _buffers.Values)
            buffer.Dispose();
        _buffers.Clear();
    }
}
