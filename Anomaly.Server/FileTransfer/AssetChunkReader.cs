using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Server.FileTransfer;

/// <summary>At most four buffered chunks; the game thread only dequeues completed reads.</summary>
internal sealed class AssetChunkReader : IDisposable
{
    private readonly BlockingCollection<byte[]> _chunks = new BlockingCollection<byte[]>(4);
    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private int _disposed;
    internal string Error;
    internal bool IsCompleted => _chunks.IsCompleted;
    internal bool TryRead(out byte[] bytes) => _chunks.TryTake(out bytes);
    internal AssetChunkReader(AssetPackSnapshot snapshot, ServerAssetManager.AssetInfo asset)
    {
        snapshot.Retain();
        Task.Run(() =>
        {
            try
            {
                using (var stream = new FileStream(asset.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length != asset.Size) throw new InvalidDataException("Immutable spool size mismatch.");
                    long remaining = asset.Size;
                    while (remaining > 0)
                    {
                        _cancel.Token.ThrowIfCancellationRequested();
                        var bytes = new byte[(int)Math.Min(remaining, FileChunkMessage.MaxChunkBytes)];
                        int read = 0;
                        while (read < bytes.Length)
                        {
                            int count = stream.Read(bytes, read, bytes.Length - read);
                            if (count == 0) throw new EndOfStreamException("Immutable spool was truncated.");
                            read += count;
                        }
                        _chunks.Add(bytes, _cancel.Token);
                        remaining -= bytes.Length;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Error = exception.Message; }
            finally { _chunks.CompleteAdding(); snapshot.Dispose(); }
        });
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _cancel.Cancel(); }
}
