using System.Security.Cryptography;
using Anomaly.Client.API.Networking;
using Anomaly.Client.Components;
using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using MelonLoader;

namespace Anomaly.Client.FileTransfer;

public static class FileTransferClient
{
    private enum State { Idle, ManifestReceived, Receiving, Complete }

    private static State _state = State.Idle;
    private static readonly Dictionary<string, ChunkBuffer> _buffers = new();
    private static readonly Dictionary<string, string> _hashToPath = new();
    private static int _totalFiles;
    private static int _completedFiles;
    private static long _totalBytes;
    private static long _receivedBytes;

    public static void Init()
    {
        AnomalyMessaging.MessageReceived += OnMessageReceived;
    }

    public static void Reset()
    {
        _state = State.Idle;
        _buffers.Clear();
        _hashToPath.Clear();
        _totalFiles = 0;
        _completedFiles = 0;
        _totalBytes = 0;
        _receivedBytes = 0;
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
        var missing = new List<byte[]>();

        foreach (var file in manifest.Files)
        {
            string hex = FileCache.ToHex(file.Hash);
            _hashToPath[hex] = file.Path;

            if (!FileCache.HasFile(file.Hash))
            {
                missing.Add(file.Hash);
                _totalBytes += file.Size;
            }
        }

        _totalFiles = missing.Count;
        _completedFiles = 0;
        _receivedBytes = 0;

        AnomalyMessaging.Send(new FileRequestMessage { RequestedHashes = missing });

        if (missing.Count == 0)
        {
            _state = State.Complete;
            MelonLogger.Msg("FileTransfer: All files already cached.");
            FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, 0));
            return;
        }

        _state = State.ManifestReceived;
        FileTransferOverlay.Show();
        MelonLogger.Msg($"FileTransfer: Requesting {missing.Count} files ({_totalBytes} bytes).");
    }

    private static void HandleChunk(FileChunkMessage chunk)
    {
        if (_state == State.ManifestReceived)
            _state = State.Receiving;

        string hex = FileCache.ToHex(chunk.Hash);

        if (!_buffers.TryGetValue(hex, out var buffer))
        {
            string path = _hashToPath.TryGetValue(hex, out var p) ? p : "unknown";
            buffer = new ChunkBuffer(chunk.TotalChunks, path);
            _buffers[hex] = buffer;
        }

        if (buffer.AddChunk(chunk.ChunkIndex, chunk.Data))
        {
            _receivedBytes += chunk.Data.Length;

            float percent = _totalBytes > 0 ? (float)_receivedBytes / _totalBytes : 0f;
            string fileName = buffer.OriginalPath;

            FileTransferOverlay.UpdateProgress(fileName, _completedFiles + 1, _totalFiles, percent);
            FileTransferEvents.OnProgress(new FileTransferProgressEventArgs(
                fileName, _completedFiles, _totalFiles, _receivedBytes, _totalBytes, percent));
        }
    }

    private static void HandleFileComplete(FileTransferCompleteMessage complete)
    {
        string hex = FileCache.ToHex(complete.Hash);

        if (!_buffers.TryGetValue(hex, out var buffer))
        {
            MelonLogger.Warning($"FileTransfer: Received completion for unknown file {hex}.");
            AnomalyMessaging.Send(new FileAckMessage { Hash = complete.Hash, Status = 1 });
            return;
        }

        if (!buffer.IsComplete)
        {
            MelonLogger.Warning($"FileTransfer: File {hex} marked complete but missing {buffer.TotalChunks - buffer.ReceivedCount} chunks.");
            AnomalyMessaging.Send(new FileAckMessage { Hash = complete.Hash, Status = 1 });
            return;
        }

        byte[] data = buffer.Reassemble();

        // Verify SHA-256
        using var sha = SHA256.Create();
        byte[] computedHash = sha.ComputeHash(data);
        bool hashMatch = computedHash.AsSpan().SequenceEqual(complete.Hash);

        if (!hashMatch)
        {
            MelonLogger.Warning($"FileTransfer: Hash mismatch for {hex}.");
            AnomalyMessaging.Send(new FileAckMessage { Hash = complete.Hash, Status = 1 });
            _buffers.Remove(hex);
            return;
        }

        string path = _hashToPath.TryGetValue(hex, out var p) ? p : hex;
        FileCache.WriteFile(complete.Hash, data, path);

        _buffers.Remove(hex);
        _completedFiles++;

        AnomalyMessaging.Send(new FileAckMessage { Hash = complete.Hash, Status = 0 });
        MelonLogger.Msg($"FileTransfer: File '{path}' received ({data.Length} bytes).");

        if (_completedFiles >= _totalFiles)
        {
            _state = State.Complete;
            FileTransferOverlay.Hide();
            FileCache.Evict(500 * 1024 * 1024); // 500 MB limit
            FileTransferEvents.OnComplete(new FileTransferCompleteEventArgs(true, _completedFiles));
            MelonLogger.Msg($"FileTransfer: All {_completedFiles} files received.");
        }
    }
}
