using Anomaly.Installer.Platform;

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
        CheckWine(platform);

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
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(installerDir)) ?? installerDir);
            long free = drive.AvailableFreeSpace;
            Console.WriteLine(
                $"  Disk space: {ConsoleUI.FormatBytes(free)} free on {drive.Name}, " +
                $"need ~{ConsoleUI.FormatBytes(totalNeeded)}");

            if (free < totalNeeded)
                throw new InstallerException(
                    $"Not enough free space on {drive.Name}. Need ~{ConsoleUI.FormatBytes(totalNeeded)}, " +
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

    private static void CheckWine(PlatformInfo platform)
    {
        if (!platform.IsLinux) return;

        if (platform.IsWineAvailable())
        {
            Console.WriteLine("  Wine: available");
        }
        else
        {
            throw new InstallerException(
                "Wine is required to run the embedded unlicense.exe on Linux but was not found. " +
                "Install Wine (e.g. `sudo apt install wine`) and retry.");
        }
    }
}
