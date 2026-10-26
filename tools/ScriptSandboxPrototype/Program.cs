using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Anomaly.ScriptSandboxPrototype;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private const long JobMemoryBytes = 256L * 1024 * 1024;
    private const long SampledMemoryBytes = 192L * 1024 * 1024;
    private const int WorkerTimeoutMs = 2000;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--worker")
                return Worker(args);
            return await Compare(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Worker(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException("worker requires runtime, case, sentinel path, and loopback URL");
        // The parent assigns process memory limits before allowing any script to run.
        Console.WriteLine("READY");
        Console.Out.Flush();
        if (Console.ReadLine() != "go")
            return 2;

        // Positive controls run in the same job-limited worker, before any script.
        // They establish that missing script capabilities, rather than OS denial,
        // account for the failed file/network attempts.
        if (!File.ReadAllText(args[3]).StartsWith("Anomaly task-owned", StringComparison.Ordinal))
            throw new InvalidOperationException("worker could not read the file witness");
        using (var control = new System.Net.Sockets.TcpClient())
        {
            control.Connect("127.0.0.1", new Uri(args[4]).Port);
            control.GetStream().ReadTimeout = 2000;
            if (control.GetStream().Read(new byte[128]) == 0)
                throw new InvalidOperationException("worker loopback control response missing");
        }
        Console.WriteLine("CONTROLLED");
        Console.Out.Flush();
        if (Console.ReadLine() != "run")
            return 2;

        var limits = new SandboxLimits();
        var probe = ProbeCases.Create(args[3], args[4]).Single(p => p.Name == args[2]);
        var api = new SandboxedObjectApi(limits);
        IScriptRuntime runtime = args[1] switch
        {
            "moonsharp" => new MoonSharpRuntime(),
            "luau" => new LuauRuntime(),
            _ => throw new ArgumentException("unknown runtime")
        };
        // Prime trusted assembly/JIT setup separately from each untrusted VM's
        // initialization and compilation. This is still no game performance benchmark.
        var warmLimits = limits with { ExecutionTimeoutMs = 1000 };
        var warm = runtime.Execute("return 1", new SandboxedObjectApi(warmLimits), warmLimits);
        if (warm.Outcome != "Completed")
            throw new InvalidOperationException($"runtime warm-up failed: {warm.Outcome}: {warm.Message}");
        var process = Process.GetCurrentProcess();
        var beforeCpu = process.TotalProcessorTime;
        var beforeHeap = GC.GetTotalMemory(false);
        var beforeAllocation = GC.GetTotalAllocatedBytes(false);
        var stopwatch = Stopwatch.StartNew();
        RuntimeExecution execution;
        try
        {
            var rejection = SourcePolicy.Reject(probe.Source, limits);
            execution = rejection == null ? runtime.Execute(probe.Source, api, limits)
                : new RuntimeExecution("SourceRejected", rejection);
        }
        catch (OutOfMemoryException)
        {
            execution = new RuntimeExecution("WorkerMemoryFailure", "Worker allocation failed; this is not a guest heap quota.");
        }
        catch (Exception ex)
        {
            execution = new RuntimeExecution("WorkerFailure", $"{ex.GetType().Name}: {ex.Message}");
        }
        stopwatch.Stop();
        var beforeDiscard = api.ObjectCount;
        if (execution.Outcome != "Completed")
            api.Revoke();
        var measurement = new WorkerMeasurement(execution, stopwatch.Elapsed.TotalMilliseconds,
            (process.TotalProcessorTime - beforeCpu).TotalMilliseconds,
            GC.GetTotalMemory(false) - beforeHeap, GC.GetTotalAllocatedBytes(false) - beforeAllocation,
            api.Calls, api.Violations, beforeDiscard, api.Snapshot);
        Console.WriteLine(JsonSerializer.Serialize(measurement));
        return 0;
    }

    private static async Task<int> Compare(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("ScriptSandboxPrototype [--runtime all|moonsharp|luau] [--case NAME] [--output DIRECTORY]");
            return 0;
        }
        var root = FindRepositoryRoot();
        var output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(root, "artifacts", "script-sandbox", "comparison"));
        if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("comparison output must be inside this repository");
        Directory.CreateDirectory(output);
        var hostChecks = HostContractChecks.Run();
        var runtimeOption = Option(args, "--runtime") ?? "all";
        var runtimes = runtimeOption == "all" ? new[] { "moonsharp", "luau" } : new[] { runtimeOption };
        if (runtimes.Any(r => r != "moonsharp" && r != "luau"))
            throw new ArgumentException("runtime must be all, moonsharp, or luau");
        if (runtimes.Contains("luau") && !File.Exists(Path.Combine(AppContext.BaseDirectory, "luau_sandbox.dll")))
            throw new FileNotFoundException("Build-Luau.ps1 must run before building/running the Luau comparison.");

        var results = new List<ProbeResult>();
        var sentinelPath = Path.Combine(output, "sentinel.txt");
        const string sentinelContents = "Anomaly task-owned scripting isolation sentinel.\n";
        var sentinelBytes = new UTF8Encoding(false).GetBytes(sentinelContents);
        await using var witness = new LoopbackWitness();
        // Control checks ensure our observers actually work and the host itself can use
        // files/network. No OS filesystem/network sandbox is applied to the workers.
        using (var control = new System.Net.Sockets.TcpClient())
        {
            await control.ConnectAsync("127.0.0.1", new Uri(witness.Url).Port);
            var response = new byte[128];
            if (await control.GetStream().ReadAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(2)) == 0)
                throw new InvalidOperationException("loopback witness control response missing");
        }
        var controlWait = Stopwatch.StartNew();
        while (witness.Connections == 0 && controlWait.ElapsedMilliseconds < 1000)
            await Task.Delay(5);
        if (witness.Connections != 1 || !witness.IsListening)
            throw new InvalidOperationException("loopback witness control connection was not observed");

        var probes = ProbeCases.Create(sentinelPath, witness.Url);
        var caseOption = Option(args, "--case");
        if (caseOption != null)
        {
            probes = probes.Where(p => p.Name == caseOption).ToArray();
            if (probes.Count == 0)
                throw new ArgumentException("unknown probe case");
        }
        foreach (var runtime in runtimes)
        {
            foreach (var probe in probes)
            {
                await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);
                var connectionsBefore = witness.Connections;
                var result = await RunWorker(runtime, probe, sentinelPath, witness.Url);
                var intact = File.Exists(sentinelPath) &&
                    (await File.ReadAllBytesAsync(sentinelPath)).AsSpan().SequenceEqual(sentinelBytes);
                // Accept loop is already active. Allow an arrived connection to be counted.
                await Task.Delay(10);
                var connections = witness.Connections - connectionsBefore - 1; // one worker host control
                var expected = runtime == "moonsharp" ? probe.MoonSharpOutcomes : probe.LuauOutcomes;
                var objectCount = result.Measurement?.Objects.Count;
                var externallyStopped = result.Measurement == null && result.Outcome is "ProcessTimeout" or "ProcessMemoryLimit" or "WorkerFailure";
                var passed = expected.Contains(result.Outcome, StringComparer.Ordinal) && intact && connections == 0 && witness.IsListening
                    && (probe.ExpectedObjects == null || objectCount == probe.ExpectedObjects || externallyStopped)
                    && (!probe.ExpectPolicyViolation || result.Measurement?.PolicyViolations > 0)
                    && (result.Outcome == "Completed" || objectCount == null || objectCount == 0);
                result = result with
                {
                    ExpectedOutcomes = expected,
                    ExpectationMet = passed,
                    SentinelIntact = intact,
                    LoopbackConnections = connections,
                    Note = probe.Note
                };
                results.Add(result);
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {runtime,-9} {probe.Name,-27} {result.Outcome,-21} {result.ParentElapsedMs,7:F1} ms");
            }
        }

        var nativePath = Path.Combine(AppContext.BaseDirectory, "luau_sandbox.dll");
        var report = new ComparisonReport(DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            new SandboxLimits(), WorkerTimeoutMs, SampledMemoryBytes, JobMemoryBytes,
            hostChecks, witness.Connections - 1 - results.Count,
            File.Exists(nativePath) ? Hash(File.ReadAllBytes(nativePath)) : null, results,
            ReadMoonSharpIdentity(), Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)));
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(report, Json) + "\n");
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.md"), Render(report));
        Console.WriteLine($"Host checks: {hostChecks.Count}; probes: {results.Count(r => r.ExpectationMet)}/{results.Count}; script loopback connections: {report.ScriptLoopbackConnections}");
        Console.WriteLine(Path.Combine(output, "comparison.md"));
        return results.All(r => r.ExpectationMet) ? 0 : 1;
    }

    private static async Task<ProbeResult> RunWorker(string runtime, ProbeCase probe, string sentinelPath, string url)
    {
        var start = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("missing process executable"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in new[] { "--worker", runtime, probe.Name, sentinelPath, url })
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x08000000";
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("worker did not start");
        WorkerJob? job = null;
        var stopwatch = new Stopwatch();
        try
        {
            // Fail closed: if the job cannot be assigned, the worker receives no 'go'.
            job = new WorkerJob(process, JobMemoryBytes);
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (ready != "READY")
                throw new InvalidOperationException("worker readiness protocol failed");
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync("go");
            var control = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            if (control != "CONTROLLED")
                throw new InvalidOperationException("worker filesystem/loopback positive controls failed");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync("run");
            process.StandardInput.Close();
            stopwatch.Start();
            string? intervention = null;
            long peakPrivateBytes = 0;
            while (!process.HasExited)
            {
                process.Refresh();
                try { peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64); }
                catch (InvalidOperationException) when (process.HasExited) { }
                if (peakPrivateBytes >= SampledMemoryBytes)
                    intervention = "ProcessMemoryLimit";
                else if (stopwatch.ElapsedMilliseconds >= WorkerTimeoutMs)
                    intervention = "ProcessTimeout";
                if (intervention != null)
                {
                    process.Kill(entireProcessTree: true);
                    break;
                }
                await Task.Delay(10);
            }
            await process.WaitForExitAsync();
            stopwatch.Stop();
            var output = await outputTask;
            var error = await errorTask;
            WorkerMeasurement? measurement = null;
            if (intervention == null && process.ExitCode == 0)
            {
                try { measurement = JsonSerializer.Deserialize<WorkerMeasurement>(output, Json); }
                catch (JsonException) { }
            }
            var outcome = intervention ?? measurement?.Execution.Outcome ?? "WorkerFailure";
            var diagnostic = measurement?.Execution.Message ?? (error.Length > 1500 ? error[..1500] : error);
            return new ProbeResult(runtime, probe.Name, Hash(Encoding.UTF8.GetBytes(probe.Source)), outcome,
                Array.Empty<string>(), false, stopwatch.Elapsed.TotalMilliseconds, peakPrivateBytes,
                process.ExitCode, diagnostic, measurement, false, 0, "");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            job?.Dispose();
        }
    }

    private static string Render(ComparisonReport report)
    {
        var text = new StringBuilder("# Isolated scripting prototype comparison\n\n");
        text.AppendLine($"Captured {report.CapturedAt:O}; {report.Framework}; {report.OS}; {report.Architecture}.");
        text.AppendLine($"\nMoonSharp loaded identity: `{report.MoonSharpIdentity.AssemblyIdentity}`; informational version `{report.MoonSharpIdentity.InformationalVersion}`; target `{report.MoonSharpIdentity.TargetFramework}`; SHA-256 `{report.MoonSharpIdentity.Sha256}`. Harness SHA-256 `{report.HarnessSha256}`.");
        text.AppendLine("\nThis is a standalone .NET 6 experiment, with an in-memory object API. No Unity/game execution, production transfer changes, or OS filesystem/network sandboxing occurred. Timing includes per-VM initialization/compilation, after trusted assembly/JIT warm-up, and is not a game performance benchmark.");
        text.AppendLine($"\nVM execution budget: {report.Limits.ExecutionTimeoutMs} ms; MoonSharp slices {report.Limits.InstructionSlice} × {report.Limits.MaxResumes}; Luau allocator cap {report.Limits.VmMemoryLimitBytes} bytes. Parent deadline {report.WorkerTimeoutMs} ms; sampled private-memory guard {report.SampledMemoryBytes} bytes; Windows Job Object committed-memory cap {report.JobMemoryBytes} bytes. These process guards protect the experiment and do not become interpreter containment claims.");
        text.AppendLine($"\nHost contract checks: {report.HostChecks.Count}. Script loopback connections: {report.ScriptLoopbackConnections}. A parent control verified the listener; every worker then successfully read the sentinel and connected to the loopback witness before script execution. These counted positive controls are excluded from script connection totals. Each row independently checked the task-owned sentinel after the worker exited.");
        text.AppendLine("\n| Runtime | Probe | Outcome | Expected | Exec ms | CPU ms | Sampled private peak MiB | VM peak KiB | Sentinel / network |\n|---|---|---|---|---:|---:|---:|---:|---|");
        foreach (var row in report.Results)
        {
            var worker = row.Measurement;
            text.AppendLine($"| {row.Runtime} | {row.Case} | {row.Outcome} | {(row.ExpectationMet ? "yes" : "NO")} | {worker?.ElapsedMs.ToString("F2") ?? "—"} | {worker?.CpuMs.ToString("F2") ?? "—"} | {row.SampledPeakPrivateBytes / 1048576.0:F2} | {(worker?.Execution.VmPeakBytes / 1024.0)?.ToString("F1") ?? "—"} | {(row.SentinelIntact ? "intact" : "CHANGED")} / {row.LoopbackConnections} | ");
        }
        text.AppendLine("\nProcessTimeout/ProcessMemoryLimit/WorkerMemoryFailure/WorkerFailure/NativeMemoryFailure are harness or process outcomes, not successful VM containment. Worker state/callback counts are unavailable when a process is killed or crashes; no staged objects can leave this in-memory helper. A successful string allocation larger than the Luau cap demonstrates MoonSharp's missing heap quota; a source or API rejection is not proof against interpreter vulnerabilities. Native allocator accounting excludes compiler allocations and host allocations. Sampled process peaks can miss short-lived allocations. Windows process CPU time is coarsely quantized; a reported zero does not mean no CPU work.");
        foreach (var row in report.Results.Where(r => !string.IsNullOrEmpty(r.Note)))
            text.AppendLine($"\n- {row.Runtime}/{row.Case}: {row.Note} Observed `{row.Outcome}`.");
        return text.ToString();
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException($"missing value after {name}");
        return args[index + 1];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Anomaly.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("could not locate Anomaly.slnx");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static RuntimeAssemblyIdentity ReadMoonSharpIdentity()
    {
        var assembly = typeof(MoonSharp.Interpreter.Script).Assembly;
        return new RuntimeAssemblyIdentity(assembly.GetName().FullName ?? "", assembly.Location,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
            assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "",
            assembly.ManifestModule.ModuleVersionId.ToString(), Hash(File.ReadAllBytes(assembly.Location)));
    }
}

internal sealed record WorkerMeasurement(RuntimeExecution Execution, double ElapsedMs, double CpuMs,
    long ManagedHeapGrowthBytes, long ManagedAllocatedBytes, int HostCalls, int PolicyViolations,
    int ObjectsBeforeDiscard, IReadOnlyList<ObjectSnapshot> Objects);

internal sealed record ProbeResult(string Runtime, string Case, string SourceSha256, string Outcome,
    IReadOnlyList<string> ExpectedOutcomes, bool ExpectationMet, double ParentElapsedMs, long SampledPeakPrivateBytes,
    int ExitCode, string Diagnostic, WorkerMeasurement? Measurement, bool SentinelIntact, int LoopbackConnections, string Note);

internal sealed record ComparisonReport(DateTimeOffset CapturedAt, string Framework, string OS, string Architecture,
    SandboxLimits Limits, int WorkerTimeoutMs, long SampledMemoryBytes, long JobMemoryBytes,
    IReadOnlyList<string> HostChecks, int ScriptLoopbackConnections, string? NativeDllSha256, IReadOnlyList<ProbeResult> Results,
    RuntimeAssemblyIdentity MoonSharpIdentity, string HarnessSha256);

internal sealed record RuntimeAssemblyIdentity(string AssemblyIdentity, string LoadedPath, string InformationalVersion,
    string TargetFramework, string ModuleVersionId, string Sha256);
