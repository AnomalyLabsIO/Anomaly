using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Anomaly.Launcher.Platform;

/// <summary>
/// OS + Wine detection used by the Launcher. Mirror of Anomaly.Installer.Platform.PlatformInfo
/// (same logic, different shape — instance class there so consumers can mock/inject).
/// Kept in sync manually; if you change the Wine detection probe here, update the Installer copy.
/// </summary>
public static class PlatformHelper
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsWineAvailable()
    {
        if (IsWindows) return true;
        try
        {
            var psi = new ProcessStartInfo("wine", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
