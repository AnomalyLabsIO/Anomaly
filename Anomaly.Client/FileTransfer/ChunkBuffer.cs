namespace Anomaly.Client.FileTransfer;

public class ChunkBuffer
{
    private readonly byte[][] _chunks;
    private readonly ushort _totalChunks;
    private int _receivedCount;

    public string OriginalPath { get; }
    public int ReceivedCount => _receivedCount;
    public int TotalChunks => _totalChunks;
    public bool IsComplete => _receivedCount >= _totalChunks;

    public ChunkBuffer(ushort totalChunks, string originalPath)
    {
        _totalChunks = totalChunks;
        _chunks = new byte[totalChunks][];
        OriginalPath = originalPath;
    }

    /// <summary>
    /// Stores a chunk. Returns true if this was a new chunk (not a duplicate).
    /// </summary>
    public bool AddChunk(ushort index, byte[] data)
    {
        if (index >= _totalChunks)
            return false;

        if (_chunks[index] != null)
            return false; // Duplicate

        _chunks[index] = data;
        _receivedCount++;
        return true;
    }

    /// <summary>
    /// Reassembles all chunks into a single byte array.
    /// Only call when IsComplete is true.
    /// </summary>
    public byte[] Reassemble()
    {
        int totalSize = 0;
        for (int i = 0; i < _totalChunks; i++)
            totalSize += _chunks[i].Length;

        var result = new byte[totalSize];
        int offset = 0;
        for (int i = 0; i < _totalChunks; i++)
        {
            Array.Copy(_chunks[i], 0, result, offset, _chunks[i].Length);
            offset += _chunks[i].Length;
        }

        return result;
    }
}
