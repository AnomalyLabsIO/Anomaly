using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

internal static class CpuProbeProcess
{
    internal static async Task<CpuProbeResult> Run(string fixture)
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "CpuProbe", "Anomaly.ScriptingCpuProbe.dll");
        Assert.True(File.Exists(probe), $"The fixed CPU worker was not copied to {probe}.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(probe);
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add("--wait-for-parent");
        // Experiment safety only; this process/GC ceiling is not a guest quota.
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x8000000";
        start.Environment["DOTNET_EnableDiagnostics"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start fixed CPU worker.");
        CpuWorkerJob? job = null;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            if (OperatingSystem.IsWindows())
                job = new CpuWorkerJob(process, 256L * 1024 * 1024);
            await process.StandardInput.WriteLineAsync("start");
            process.StandardInput.Close();
            var exited = process.WaitForExitAsync();
            var finished = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5)));
            if (finished != exited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail($"CPU fixture '{fixture}' needed the external process watchdog; interpreter containment failed.");
            }
            await exited;
            var stdout = await output;
            var stderr = await errors;
            Assert.True(process.ExitCode == 0,
                $"CPU fixture '{fixture}' exited {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
            var result = JsonSerializer.Deserialize<CpuProbeResult>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return Assert.IsType<CpuProbeResult>(result);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            job?.Dispose();
        }
    }
}

internal sealed record CpuProbeResult(string Outcome, string? Reason, string? Error, bool Terminal,
    int Resumes, int HostCalls, int RejectedCalls, double? Number, long ElapsedMilliseconds);
