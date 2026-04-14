using System.Runtime.InteropServices;

namespace Anomaly.Installer.Platform;

public class PlatformInfo
{
    public bool IsWindows { get; }
    public bool IsLinux { get; }

    public PlatformInfo()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    /// <summary>
    /// Checks if Wine is available on Linux for running Windows executables.
    /// </summary>
    public bool IsWineAvailable()
    {
        if (IsWindows) return true;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("wine", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
