namespace Anomaly.Client.Diagnostics;

public static class CircularLogBuffer
{
    private const int Capacity = 200;

    private static readonly LogEntry[] _buffer = new LogEntry[Capacity];
    private static int _head = -1;
    private static int _count;

    public static void Add(string level, string message)
    {
        int index = Interlocked.Increment(ref _head) % Capacity;
        _buffer[index] = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message
        };

        // Track count up to capacity (for GetSnapshot ordering)
        if (_count < Capacity)
            Interlocked.Increment(ref _count);
    }

    public static string[] GetSnapshot()
    {
        int currentHead = _head;
        int currentCount = Math.Min(_count, Capacity);

        if (currentCount == 0 || currentHead < 0)
            return Array.Empty<string>();

        var result = new string[currentCount];
        int startIndex = currentCount < Capacity
            ? 0
            : (currentHead + 1) % Capacity;

        for (int i = 0; i < currentCount; i++)
        {
            int bufferIndex = (startIndex + i) % Capacity;
            var entry = _buffer[bufferIndex];
            result[i] = entry.Message != null
                ? $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}"
                : "";
        }

        return result;
    }

    public struct LogEntry
    {
        public DateTime Timestamp;
        public string Level;
        public string Message;
    }
}
