using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Shared.Networking.Messages;
using System.Threading;

namespace Anomaly.Client.FileTransfer;

/// <summary>
/// Bounded, disk-backed receiver for one manifest-declared file. Chunks may
/// arrive out of order, but they can never extend the pre-sized temp file or
/// exceed the manifest's expected byte count.
/// </summary>
public sealed class ChunkBuffer : IDisposable, IFileReceiveBuffer
{
    private readonly bool[] _received;
    private FileStream _stream;
    private bool _committed;

    public byte[] Hash { get; }
    public string OriginalPath { get; }
    public uint ExpectedSize { get; }
    public ushort TotalChunks { get; }
    public string TempPath { get; }
    public int ReceivedCount { get; private set; }
    public long ReceivedBytes { get; private set; }
    public DateTime LastActivityUtc { get; private set; }
    public bool IsComplete => ReceivedCount == TotalChunks && ReceivedBytes == ExpectedSize;

    public ChunkBuffer(ulong sessionId, byte[] hash, uint expectedSize, string originalPath)
    {
        if (sessionId == 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        if (hash == null || hash.Length != 32)
            throw new ArgumentException("File hash must be 32 bytes.", nameof(hash));
        if (expectedSize == 0 || expectedSize > FileManifestMessage.MaxFileBytes)
            throw new ArgumentOutOfRangeException(nameof(expectedSize));

        int totalChunks = checked((int)((expectedSize + FileChunkMessage.MaxChunkBytes - 1) /
            FileChunkMessage.MaxChunkBytes));
        if (totalChunks <= 0 || totalChunks > FileChunkMessage.MaxChunksPerFile)
            throw new InvalidDataException("Manifest size requires an invalid chunk count.");

        Hash = (byte[])hash.Clone();
        ExpectedSize = expectedSize;
        OriginalPath = originalPath ?? string.Empty;
        TotalChunks = (ushort)totalChunks;
        _received = new bool[totalChunks];

        string incomingDir = Path.Combine(Paths.FileCachePath, ".incoming");
        Directory.CreateDirectory(incomingDir);
        string hex = FileCache.ToHex(hash);
        // Each owner/attempt has a unique path; delayed cancellation cleanup must
        // never remove a newer transfer's partial file for the same hash.
        TempPath = Path.Combine(incomingDir, $"{sessionId:x16}-{hex}-{Guid.NewGuid():N}.part");
        _stream = new FileStream(
            TempPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            FileChunkMessage.MaxChunkBytes,
            FileOptions.RandomAccess);
        _stream.SetLength(expectedSize);
        LastActivityUtc = DateTime.UtcNow;
    }

    public bool TryAddChunk(
        ushort index,
        ushort advertisedTotalChunks,
        byte[] data,
        out bool duplicate,
        out string error)
    {
        duplicate = false;
        error = null;

        if (_stream == null)
        {
            error = "buffer is closed";
            return false;
        }
        if (advertisedTotalChunks != TotalChunks || index >= TotalChunks)
        {
            error = "chunk count/index does not match the manifest";
            return false;
        }
        if (_received[index])
        {
            duplicate = true;
            return false;
        }

        long offset = (long)index * FileChunkMessage.MaxChunkBytes;
        int expectedLength = (int)Math.Min(
            FileChunkMessage.MaxChunkBytes,
            ExpectedSize - offset);
        if (data == null || data.Length != expectedLength)
        {
            error = $"chunk length {data?.Length ?? 0} did not match expected {expectedLength}";
            return false;
        }

        _stream.Position = offset;
        _stream.Write(data, 0, data.Length);
        _received[index] = true;
        ReceivedCount++;
        ReceivedBytes += data.Length;
        LastActivityUtc = DateTime.UtcNow;
        return true;
    }

    public bool CommitToCache() => CommitToCache(CancellationToken.None);

    public bool CommitToCache(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!IsComplete || _stream == null)
            return false;

        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        _stream = null;

        _committed = FileCache.ImportVerifiedFile(
            TempPath,
            Hash,
            OriginalPath,
            ExpectedSize,
            cancellation);
        return _committed;
    }

    public void Dispose()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // Best-effort cleanup during disconnect/abort.
        }
        _stream = null;

        if (_committed)
            return;

        try
        {
            if (File.Exists(TempPath))
                File.Delete(TempPath);
        }
        catch
        {
            // Best-effort cleanup; startup cache maintenance can remove stale .part files.
        }
    }
}
