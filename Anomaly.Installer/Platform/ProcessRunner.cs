using System.Diagnostics;

namespace Anomaly.Installer.Platform;

public static class ProcessRunner
{
    /// <summary>
    /// Runs a Windows executable, using Wine on Linux if needed.
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

        using var process = Process.Start(psi);
        if (process == null)
            throw new InstallerException($"Failed to start process: {exePath}");

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill();
            throw new InstallerException($"Process timed out after {timeoutMs / 1000} seconds: {exePath}");
        }

        return process.ExitCode;
    }
}
