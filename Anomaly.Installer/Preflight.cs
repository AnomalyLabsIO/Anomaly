using Anomaly.Installer.Platform;
using System.Runtime.InteropServices;

namespace Anomaly.Installer;

/// <summary>
/// Fail-fast sanity checks run before any destructive step. Each check surfaces one
/// specific cause of mid-install failure (permission denied, disk full, missing Wine)
/// while the user can still abort cleanly.
/// </summary>
internal static class Preflight
{
    public static void Run(string installerDir, string vanillaDir, PlatformInfo platform, bool dryRun)
    {
        Console.WriteLine("Preflight checks:");

        CheckRuntime();
        CheckWritePermission(installerDir);
        CheckDiskSpace(installerDir, vanillaDir);
        CheckLinuxWindowsRuntime(platform);

        if (dryRun)
            Console.WriteLine("  [dry-run] Preflight complete.");
        else
            Console.WriteLine("  All preflight checks passed.");
        Console.WriteLine();
    }

    private static void CheckRuntime()
    {
        string version = Environment.Version.ToString();
        Console.WriteLine($"  .NET runtime: {version}");
    }

    private static void CheckWritePermission(string dir)
    {
        // Probe by writing and deleting a small sentinel file. Cheaper and more reliable
        // than poking at ACLs, and it catches read-only-disk cases too.
        string probe = Path.Combine(dir, $".anomaly-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InstallerException(
                $"Cannot write to installer directory {dir}: {ex.Message}. " +
                "Move the installer into a writable folder and retry.");
        }
        Console.WriteLine("  Write permission: OK");
    }

    private static void CheckDiskSpace(string installerDir, string vanillaDir)
    {
        long needed = EstimateCopyBytes(vanillaDir);

        // 500 MB headroom covers MelonLoader download + IL2CPP dump + log growth.
        long cushion = 500L * 1024 * 1024;
        long totalNeeded = needed + cushion;

        try
        {
            DiskSpaceInfo space = GetDiskSpaceInfo(installerDir);
            long free = space.AvailableFreeSpace;
            Console.WriteLine(
                $"  Disk space: {ConsoleUI.FormatBytes(free)} free at {space.DisplayName}, " +
                $"need ~{ConsoleUI.FormatBytes(totalNeeded)}");

            if (free < totalNeeded)
                throw new InstallerException(
                    $"Not enough free space at {space.DisplayName}. Need ~{ConsoleUI.FormatBytes(totalNeeded)}, " +
                    $"have {ConsoleUI.FormatBytes(free)}. Free up space and retry.");
        }
        catch (InstallerException) { throw; }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Could not determine disk space: {ex.Message}");
        }
    }

    private static long EstimateCopyBytes(string vanillaDir)
    {
        try
        {
            return new DirectoryInfo(vanillaDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            // Fall back to a conservative guess (4 GB) if we can't enumerate — better to
            // warn loud than skip the check entirely.
            return 4L * 1024 * 1024 * 1024;
        }
    }

    private static DiskSpaceInfo GetDiskSpaceInfo(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsLinux())
        {
            if (TryGetLinuxDiskSpace(fullPath, out long free))
                return new DiskSpaceInfo(fullPath, free);
        }

        var drive = new DriveInfo(Path.GetPathRoot(fullPath) ?? fullPath);
        return new DiskSpaceInfo(drive.Name, drive.AvailableFreeSpace);
    }

    private static bool TryGetLinuxDiskSpace(string path, out long free)
    {
        free = 0;

        if (statvfs(path, out Statvfs stat) != 0)
            return false;

        ulong fragmentSize = stat.f_frsize != 0 ? stat.f_frsize : stat.f_bsize;
        checked
        {
            free = (long)(stat.f_bavail * fragmentSize);
        }

        return true;
    }

    private readonly record struct DiskSpaceInfo(string DisplayName, long AvailableFreeSpace);

    [StructLayout(LayoutKind.Sequential)]
    private struct Statvfs
    {
        public ulong f_bsize;
        public ulong f_frsize;
        public ulong f_blocks;
        public ulong f_bfree;
        public ulong f_bavail;
        public ulong f_files;
        public ulong f_ffree;
        public ulong f_favail;
        public ulong f_fsid;
        public ulong f_flag;
        public ulong f_namemax;
        private int _f_spare0;
        private int _f_spare1;
        private int _f_spare2;
        private int _f_spare3;
        private int _f_spare4;
        private int _f_spare5;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statvfs(string path, out Statvfs buffer);

    private static void CheckLinuxWindowsRuntime(PlatformInfo platform)
    {
        if (!platform.IsLinux) return;

        LinuxRuntimeInfo? runtime = platform.LinuxRuntime;
        if (runtime == null)
            return;

        if (runtime.IsSteamOs)
            Console.WriteLine("  SteamOS: detected");
        else
            Console.WriteLine("  SteamOS: not detected");

        if (runtime.IsSteamDeckHardware)
            Console.WriteLine("  Steam Deck hardware: detected");

        if (!string.IsNullOrWhiteSpace(runtime.SteamRootPath))
            Console.WriteLine($"  Steam root: {runtime.SteamRootPath}");
        else
            ConsoleUI.PrintWarning("Steam root could not be auto-detected. Manual vanilla path selection may still work.");

        if (runtime.HasProton)
        {
            Console.WriteLine($"  Proton: {runtime.PreferredProton!.Name}");
        }
        else
        {
            ConsoleUI.PrintWarning(
                "No Proton tool was found under Steam. If this build needs the unpacker, install or run a Proton-enabled Steam title first.");
        }

        if (runtime.HasWine)
        {
            Console.WriteLine($"  Wine: {runtime.WineCommand}");
        }
        else
        {
            ConsoleUI.PrintWarning("Wine: not found");
        }

        if (!runtime.HasWindowsRuntime)
        {
            ConsoleUI.PrintWarning(
                "No Windows compatibility runtime was detected. Install may still succeed if GameAssembly.dll does not require the unpacker. " +
                "Themida-protected builds will fail later unless Proton or Wine is available.");
        }
    }
}
