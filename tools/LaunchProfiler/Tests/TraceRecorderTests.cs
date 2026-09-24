using System.Diagnostics;
using System.Text.Json;
using Anomaly.LaunchProfiler;
using Xunit;

public sealed class TraceRecorderTests
{
    [Fact]
    public void ConcurrentCaptureIsBoundedAndAccountsForEveryDroppedEvent()
    {
        var recorder = new TraceRecorder(127);
        Parallel.For(0, 4096, i => recorder.Add("parallel", i));
        var events = recorder.Snapshot();
        Assert.Equal(127, events.Length);
        Assert.Equal(4096 - 127, recorder.Dropped);
        Assert.Equal(events.Select(e => e.TimestampQpc).OrderBy(t => t), events.Select(e => e.TimestampQpc));
    }

    [Fact]
    public void TraceRetainsNestedInclusiveSpansAndEscapesCsvNames()
    {
        string directory = Path.Combine(Path.GetTempPath(), "anomaly-launch-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            long origin = 1000;
            var recorder = new TraceRecorder();
            recorder.Add("outer,\"quoted\"", origin, Stopwatch.Frequency, span: true);
            recorder.Add("inner", origin + Stopwatch.Frequency / 4, Stopwatch.Frequency / 2, span: true);
            recorder.Add("ready", origin + Stopwatch.Frequency);
            recorder.Write(directory, "test", "detailed", "baseline", origin);
            using var trace = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "trace.json")));
            var events = trace.RootElement.GetProperty("traceEvents");
            Assert.Equal(3, events.GetArrayLength());
            Assert.Equal("X", events[0].GetProperty("ph").GetString());
            Assert.Equal(1_000_000, events[0].GetProperty("dur").GetDouble());
            Assert.Equal(500_000, events[1].GetProperty("dur").GetDouble());
            Assert.Equal("i", events[2].GetProperty("ph").GetString());
            Assert.Contains("\"outer,\"\"quoted\"\"\"", File.ReadAllText(Path.Combine(directory, "events.csv")));
            Assert.False(File.Exists(Path.Combine(directory, "events.json.tmp")));
            using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "events.json")));
            Assert.Equal(Stopwatch.Frequency, raw.RootElement.GetProperty("stopwatchFrequency").GetInt64());
            Assert.Equal(origin, raw.RootElement.GetProperty("firstPluginQpc").GetInt64());
        }
        finally
        {
            // Exact test-owned temp path only; this test never receives caller paths.
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void ZeroLengthSpanIsNotMisrepresentedAsAMilestone()
    {
        var recorder = new TraceRecorder();
        recorder.Add("very-fast", 10, 0, span: true);
        recorder.Add("mark", 10);
        Assert.Equal("span", recorder.Snapshot()[0].Kind);
        Assert.Equal("instant", recorder.Snapshot()[1].Kind);
    }
}
