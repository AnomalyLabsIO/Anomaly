using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

/// <summary>Four slots include the read in progress. Four active readers retain at most 256 KiB.</summary>
internal sealed class AssetChunkReader : IDisposable
{
    private readonly ConcurrentQueue<byte[]> _queue = new ConcurrentQueue<byte[]>();
    private readonly BlockingCollection<byte[]> _chunks;
    private readonly SemaphoreSlim _slots = new SemaphoreSlim(4, 4);
    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private int _disposed;
    internal string Error;
    internal bool IsCompleted => _chunks.IsCompleted;
    internal bool TryPeek(out byte[] bytes)
    {
        bytes = null;
        return _chunks.Count != 0 && _queue.TryPeek(out bytes);
    }
    internal bool TryRead(out byte[] bytes)
    {
        if (!_chunks.TryTake(out bytes)) return false;
        _slots.Release();
        return true;
    }
    internal AssetChunkReader(AssetPackSnapshot snapshot, ServerAssetManager.AssetInfo asset)
    {
        _chunks = new BlockingCollection<byte[]>(_queue, 4);
        snapshot.Retain();
        Task.Run(async () =>
        {
            try
            {
                using (var stream = new FileStream(asset.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    FileChunkMessage.MaxChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (stream.Length != asset.Size) throw new InvalidDataException("Immutable spool size mismatch.");
                    long remaining = asset.Size;
                    while (remaining > 0)
                    {
                        await _slots.WaitAsync(_cancel.Token).ConfigureAwait(false);
                        bool queued = false;
                        try
                        {
                            var bytes = new byte[(int)Math.Min(remaining, FileChunkMessage.MaxChunkBytes)];
                            int read = 0;
                            while (read < bytes.Length)
                            {
                                int count = await stream.ReadAsync(bytes, read, bytes.Length - read, _cancel.Token).ConfigureAwait(false);
                                if (count == 0) throw new EndOfStreamException("Immutable spool was truncated.");
                                read += count;
                            }
                            _chunks.Add(bytes, _cancel.Token);
                            queued = true;
                            remaining -= bytes.Length;
                        }
                        finally { if (!queued) _slots.Release(); }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Error = exception.Message; }
            finally { _chunks.CompleteAdding(); snapshot.Dispose(); }
        });
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel();
        while (_chunks.TryTake(out _)) _slots.Release();
    }
}
