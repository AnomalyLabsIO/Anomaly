using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

// This tool correlates independent process/plugin timestamps with ETW on the
// same machine, so retaining raw QPC values is intentional.
#pragma warning disable CS0618

namespace Anomaly.LaunchProfiler.NativeSummary;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") { SelfTest(); return 0; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Arguments must be --name value pairs.");
                options.Add(args[i], args[i + 1]);
            }
            if (!options.TryGetValue("--etl", out var etl) || !options.TryGetValue("--run", out var run) || !options.TryGetValue("--output", out var output))
                throw new ArgumentException("Usage: NativeSummary --etl launch.etl --run evidence-directory --output summary.json");
            using var inputs = JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "runtime-inputs.json")));
            int processId = inputs.RootElement.GetProperty("processId").GetInt32();
            long frequency = inputs.RootElement.GetProperty("stopwatchFrequency").GetInt64();
            long launch = inputs.RootElement.GetProperty("launchQpc").GetInt64();
            if (processId <= 0 || frequency <= 0 || launch <= 0) throw new InvalidDataException("Run metadata has no valid process/QPC identity.");
            var capture = new Capture(processId);
            using (var source = new ETWTraceEventSource(Path.GetFullPath(etl)))
            {
                source.Kernel.PerfInfoSample += data =>
                {
                    if (data.ProcessID != processId || data.TimeStampQPC < launch) return;
                    if (data.NonProcess) { capture.ExcludedDpcIsrSamples++; return; }
                    capture.Samples.Add(new(data.TimeStampQPC, data.InstructionPointer, data.ThreadID));
                };
                source.Kernel.ImageLoad += data => capture.Image(data, false);
                source.Kernel.ImageDCStart += data => capture.Image(data, true);
                source.Kernel.ImageDCStop += data => capture.Image(data, true);
                source.Kernel.ImageUnload += capture.Unload;
                source.Kernel.FileIORead += data => capture.FileIo(data, "read");
                source.Kernel.FileIOWrite += data => capture.FileIo(data, "write");
                source.Kernel.DiskIORead += data => capture.DiskIo(data, "read");
                source.Kernel.DiskIOWrite += data => capture.DiskIo(data, "write");
                source.Process();
                capture.EventsLost = source.EventsLost;
            }
            var windows = new List<(string Name, long Start, long End)> { ("whole-target-process", launch, long.MaxValue) };
            string eventsPath = Path.Combine(run, "events.json");
            if (File.Exists(eventsPath))
            {
                using var events = JsonDocument.Parse(File.ReadAllText(eventsPath));
                if (events.RootElement.GetProperty("pid").GetInt32() != processId
                    || events.RootElement.GetProperty("stopwatchFrequency").GetInt64() != frequency)
                    throw new InvalidDataException("Profiler event PID/frequency differs from the run metadata.");
                var all = events.RootElement.GetProperty("events").EnumerateArray().ToArray();
                JsonElement core = all.FirstOrDefault(e => e.GetProperty("name").GetString() == "Anomaly.Client.Core.OnInitializeMelon");
                JsonElement menu = all.FirstOrDefault(e => e.GetProperty("name").GetString() == "game.NewMainMenu.Start");
                if (core.ValueKind != JsonValueKind.Undefined && menu.ValueKind != JsonValueKind.Undefined)
                {
                    long start = core.GetProperty("timestampQpc").GetInt64() + core.GetProperty("durationQpc").GetInt64();
                    long end = menu.GetProperty("timestampQpc").GetInt64();
                    if (end > start) windows.Add(("core-init-end-to-main-menu-start", start, end));
                }
                JsonElement awake = all.FirstOrDefault(e => e.GetProperty("name").GetString() == "game.SimpleMenu.Awake");
                JsonElement firstStart = all.FirstOrDefault(e => e.GetProperty("name").GetString() == "game.SimpleMenu.Start");
                if (awake.ValueKind != JsonValueKind.Undefined && firstStart.ValueKind != JsonValueKind.Undefined)
                {
                    long start = awake.GetProperty("timestampQpc").GetInt64() + awake.GetProperty("durationQpc").GetInt64();
                    long end = firstStart.GetProperty("timestampQpc").GetInt64();
                    if (end > start) windows.Add(("first-simple-menu-awake-end-to-start", start, end));
                }
            }
            var summary = new
            {
                schemaVersion = 1, evidenceKind = "ETW sampled instruction pointers and observed file/disk events",
                etl = Path.GetFullPath(etl), processId, stopwatchFrequency = frequency, eventsLost = capture.EventsLost,
                symbolsResolved = false, capture.ExcludedDpcIsrSamples,
                limitations = new[] {
                    "Instruction addresses are samples, not exact function durations or proof of why the main thread waited.",
                    "Native module plus RVA is reported without symbol lookup; unresolved/JIT/kernel addresses remain explicit.",
                    "I/O byte counts and response-time sums can overlap and are not additive launch wall time.",
                    "Only events attributed to the target PID are included; unattributed kernel I/O is excluded.",
                    "This capture adds tracing overhead and must not be mixed into ordinary A/B timing medians." },
                warnings = new[] { capture.EventsLost > 0 ? "ETW reported lost events: totals are incomplete." : null,
                    capture.Samples.Count == 0 ? "No target CPU samples decoded; CPU attribution is unavailable." : null,
                    capture.Io.Count == 0 ? "No target file/disk events decoded; I/O attribution is unavailable." : null,
                    windows.Count == 1 ? "Detailed core-init/menu markers unavailable; gap-specific attribution omitted." : null }.Where(x => x != null),
                windows = windows.Select(w => capture.Summarize(w.Name, w.Start, w.End, launch, frequency)),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(summary, JsonOptions));
            Console.WriteLine($"Wrote sampled ETW summary for PID {processId}: {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.ToString()); return 1; }
    }

    private static void SelfTest()
    {
        var capture = new Capture(42);
        capture.Modules.Add(new("GameAssembly.dll", 0x1000, 0x100, 5, 20));
        if (capture.Resolve(0x1050, 10)?.Name != "GameAssembly.dll" || capture.Resolve(0x1050, 21) != null || capture.Resolve(0x1100, 10) != null)
            throw new Exception("Image lifetime/range lookup failed.");
        capture.Samples.AddRange(new[] { new Sample(10, 0x1050, 1), new Sample(12, 0x1050, 1), new Sample(30, 0x1050, 2) });
        capture.Io.AddRange(new[] { new Io(11, "file-read", "asset", 4096, null), new Io(40, "disk-read", "other", 99, 2) });
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(capture.Summarize("test", 5, 20, 0, 1000), JsonOptions));
        if (result.RootElement.GetProperty("sampleCount").GetInt32() != 2
            || result.RootElement.GetProperty("sampledInstructionAddresses")[0].GetProperty("relativeVirtualAddress").GetString() != "0x50"
            || result.RootElement.GetProperty("ioHotspots")[0].GetProperty("bytes").GetInt64() != 4096)
            throw new Exception("Time-window aggregation failed.");
        Console.WriteLine("PASS: native summary range/lifetime lookup, gap filtering, sample aggregation and I/O filtering. Synthetic tests only; no ETL was captured.");
    }

    private sealed record Sample(long Qpc, ulong Address, int ThreadId);
    private sealed record Io(long Qpc, string Kind, string FileName, long Bytes, double? ResponseMilliseconds);
    private sealed class Module(string name, ulong address, ulong size, long start, long end)
    {
        internal string Name = name;
        internal ulong Address = address, Size = size;
        internal long Start = start, End = end;
    }

    private sealed class Capture(int processId)
    {
        internal readonly List<Sample> Samples = new();
        internal readonly List<Io> Io = new();
        internal readonly List<Module> Modules = new();
        internal long EventsLost, ExcludedDpcIsrSamples;

        internal void Image(ImageLoadTraceData data, bool rundown)
        {
            if (data.ProcessID != processId || data.ImageSize <= 0) return;
            if (rundown && Modules.Any(m => m.Address == data.ImageBase && m.End == long.MaxValue && m.Name == data.FileName)) return;
            Modules.Add(new(data.FileName, data.ImageBase, (uint)data.ImageSize, rundown ? long.MinValue : data.TimeStampQPC, long.MaxValue));
        }
        internal void Unload(ImageLoadTraceData data)
        {
            if (data.ProcessID != processId) return;
            var module = Modules.LastOrDefault(m => m.Address == data.ImageBase && m.End == long.MaxValue);
            if (module != null) module.End = data.TimeStampQPC;
        }
        internal void FileIo(FileIOReadWriteTraceData data, string operation)
        {
            if (data.ProcessID == processId) Io.Add(new(data.TimeStampQPC, "file-" + operation, Name(data.FileName, data.FileKey), Math.Max(0, data.IoSize), null));
        }
        internal void DiskIo(DiskIOTraceData data, string operation)
        {
            if (data.ProcessID == processId) Io.Add(new(data.TimeStampQPC, "disk-" + operation, Name(data.FileName, data.FileKey), Math.Max(0, data.TransferSize), Math.Max(0, data.ElapsedTimeMSec)));
        }
        private static string Name(string? name, ulong key) => string.IsNullOrEmpty(name) ? $"<unresolved-file-key:0x{key:x}>" : name;
        internal Module? Resolve(ulong address, long qpc) => Modules.LastOrDefault(m => m.Start <= qpc && qpc <= m.End && address >= m.Address && address - m.Address < m.Size);
        internal object Summarize(string name, long start, long end, long origin, long frequency)
        {
            var selected = Samples.Where(s => s.Qpc >= start && s.Qpc <= end).ToArray();
            var addresses = selected.Select(s => new { sample = s, module = Resolve(s.Address, s.Qpc) }).ToArray();
            return new
            {
                name, startQpc = start, endQpc = end == long.MaxValue ? (long?)null : end,
                startAfterLaunchMilliseconds = (start - origin) * 1000.0 / frequency,
                durationMilliseconds = end == long.MaxValue ? (double?)null : (end - start) * 1000.0 / frequency,
                sampleCount = selected.Length,
                sampledModules = addresses.GroupBy(x => x.module?.Name ?? "<unresolved-or-kernel>").Select(g => new { module = g.Key, samples = g.Count(), percentOfTargetSamples = 100.0 * g.Count() / selected.Length }).OrderByDescending(x => x.samples).Take(30),
                sampledThreads = selected.GroupBy(s => s.ThreadId).Select(g => new { threadId = g.Key, samples = g.Count() }).OrderByDescending(x => x.samples).Take(20),
                sampledInstructionAddresses = addresses.GroupBy(x => (x.sample.Address, Module: x.module?.Name, Base: x.module?.Address)).Select(g => new {
                    instructionPointer = $"0x{g.Key.Address:x}", module = g.Key.Module ?? "<unresolved-or-kernel>",
                    relativeVirtualAddress = g.Key.Base.HasValue ? $"0x{g.Key.Address - g.Key.Base.Value:x}" : null,
                    samples = g.Count(), percentOfTargetSamples = 100.0 * g.Count() / selected.Length,
                }).OrderByDescending(x => x.samples).Take(60),
                ioHotspots = Io.Where(i => i.Qpc >= start && i.Qpc <= end).GroupBy(i => (i.Kind, i.FileName)).Select(g => new {
                    kind = g.Key.Kind, file = g.Key.FileName, operations = g.Count(), bytes = g.Sum(i => i.Bytes),
                    responseMillisecondsSum = g.Any(i => i.ResponseMilliseconds.HasValue) ? g.Sum(i => i.ResponseMilliseconds ?? 0) : (double?)null,
                    responseMillisecondsMax = g.Max(i => i.ResponseMilliseconds),
                }).OrderByDescending(x => x.bytes).ThenByDescending(x => x.operations).Take(60),
            };
        }
    }
}
