using System.Runtime.InteropServices;

namespace Anomaly.InstallerCore.Platform;

public class PlatformInfo
{
    public bool IsWindows { get; }
    public bool IsLinux { get; }
    public LinuxRuntimeInfo? LinuxRuntime { get; }

    public PlatformInfo()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        if (IsLinux)
            LinuxRuntime = LinuxRuntimeInfo.Detect();
    }

    public bool IsWineAvailable()
    {
        if (IsWindows)
            return true;

        return LinuxRuntime?.HasWine == true;
    }
}
