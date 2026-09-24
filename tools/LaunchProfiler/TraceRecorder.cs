using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Anomaly.LaunchProfiler;

// Bounded memory only during measurement. Serialization and disk I/O happen after
// the ready marker; nested spans are preserved rather than summed as exclusive cost.
internal sealed class TraceRecorder
{
    internal sealed record Entry(string Name, long TimestampQpc, long DurationQpc,
        int ThreadId, string Kind, object? Detail);

    private readonly ConcurrentQueue<Entry> _entries = new();
    private readonly int _capacity;
    private int _count;
    private int _dropped;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal TraceRecorder(int capacity = 32768) => _capacity = capacity;
    internal int Dropped => Volatile.Read(ref _dropped);
    internal Entry[] Snapshot() => _entries.ToArray().OrderBy(e => e.TimestampQpc).ToArray();

    internal void Add(string name, long timestamp, long duration = 0, object? detail = null, bool span = false)
    {
        if (Interlocked.Increment(ref _count) > _capacity)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        _entries.Enqueue(new Entry(name, timestamp, Math.Max(0, duration),
            Environment.CurrentManagedThreadId, span ? "span" : "instant", detail));
    }

    internal void Write(string directory, string runId, string mode, string experiment, long origin)
    {
        var entries = Snapshot();
        WriteJson(Path.Combine(directory, "events.json"), new
        {
            schemaVersion = 1, stopwatchFrequency = Stopwatch.Frequency,
            pid = Environment.ProcessId, mode, experiment, runId,
            firstPluginQpc = origin, droppedEvents = Dropped, events = entries,
        });
        var trace = entries.Select(e => new
        {
            name = e.Name, cat = e.Kind == "span" ? "measured-method" : "milestone",
            ph = e.Kind == "span" ? "X" : "i", s = e.Kind == "span" ? (string?)null : "t",
            ts = (e.TimestampQpc - origin) * 1_000_000.0 / Stopwatch.Frequency,
            dur = e.DurationQpc * 1_000_000.0 / Stopwatch.Frequency,
            pid = Environment.ProcessId, tid = e.ThreadId, args = e.Detail,
        });
        WriteJson(Path.Combine(directory, "trace.json"), new
        {
            displayTimeUnit = "ms", traceEvents = trace,
            metadata = new { runId, originQpc = origin, stopwatchFrequency = Stopwatch.Frequency,
                evidence = "Instrumented managed/game wall spans; overlapping spans are not additive CPU time." },
        });
        var csv = new StringBuilder("name,kind,timestampQpc,relativeMs,durationMs,threadId\r\n");
        foreach (var e in entries)
            csv.Append(Csv(e.Name)).Append(',').Append(e.Kind).Append(',').Append(e.TimestampQpc).Append(',')
                .Append(((e.TimestampQpc - origin) * 1000.0 / Stopwatch.Frequency).ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append((e.DurationQpc * 1000.0 / Stopwatch.Frequency).ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(e.ThreadId).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "events.csv"), csv.ToString(), new UTF8Encoding(false));
    }

    internal static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    internal static void WriteJson(string path, object value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
