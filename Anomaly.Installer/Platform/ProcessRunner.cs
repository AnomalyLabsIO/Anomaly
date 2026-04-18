using System.Diagnostics;

namespace Anomaly.Installer.Platform;

public static class ProcessRunner
{
    /// <summary>
    /// Runs a Windows executable, using Wine on Linux if needed. Forwards stdout and
    /// stderr to the console (and thus to the install log) as the process produces them,
    /// so failures in the subprocess are visible in real time.
    /// </summary>
    public static int RunExe(string exePath, string arguments, PlatformInfo platform, int timeoutMs = 120000)
    {
        ProcessStartInfo psi;

        if (platform.IsWindows)
        {
            psi = new ProcessStartInfo(exePath, arguments);
        }
        else if (platform.IsLinux)
        {
            if (!platform.IsWineAvailable())
                throw new InstallerException("Wine is required to run Windows executables on Linux. Please install Wine.");

            psi = new ProcessStartInfo("wine", $"\"{exePath}\" {arguments}");
        }
        else
        {
            throw new InstallerException("Unsupported operating system.");
        }

        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

        using var process = new Process { StartInfo = psi };

        // Forward stdout/stderr line-by-line with a [exe] prefix so the install log can be
        // grepped by origin. Without this, the user saw nothing if the subprocess failed.
        string exeLabel = Path.GetFileNameWithoutExtension(exePath);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"    [{exeLabel}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.Error.WriteLine($"    [{exeLabel}] {e.Data}");
        };

        if (!process.Start())
            throw new InstallerException($"Failed to start process: {exePath}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(); } catch { /* best-effort */ }
            throw new InstallerException($"Process timed out after {timeoutMs / 1000} seconds: {exePath}");
        }

        // Drain remaining async output before returning.
        process.WaitForExit();

        return process.ExitCode;
    }
}
