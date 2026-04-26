using Avalonia;
using Anomaly.InstallerCore.Platform;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Install;
using Anomaly.Launcher.Services.Platform;

namespace Anomaly.Launcher;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--modded"))
            return LaunchModdedHeadless();

        var smokeIdx = Array.IndexOf(args, "--smoke-install");
        if (smokeIdx >= 0 && smokeIdx + 1 < args.Length)
            return RunSmokeInstall(args[smokeIdx + 1]).GetAwaiter().GetResult();

        var smokeUninstallIdx = Array.IndexOf(args, "--smoke-uninstall");
        if (smokeUninstallIdx >= 0 && smokeUninstallIdx + 1 < args.Length)
            return RunSmokeUninstall(args[smokeUninstallIdx + 1]).GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static async Task<int> RunSmokeInstall(string modId)
    {
        var (paths, client, _, installer) = BuildHeadlessServices();
        try
        {
            Console.WriteLine($"smoke install {modId} from {client.Options.BaseUrl}");
            var progress = new Progress<ModInstaller.InstallProgress>(p =>
                Console.WriteLine($"  [{p.Fraction:P0}] {p.Stage}"));
            var result = await installer.InstallAsync(modId, gameVersion: null, progress);
            Console.WriteLine($"installed {result.Installed.Count} mod(s):");
            foreach (var m in result.Installed)
                Console.WriteLine($"  - {m.Id}@{m.Version} ({m.Files.Count} files)");
            foreach (var w in result.Warnings) Console.WriteLine($"  ! {w}");
            foreach (var s in result.Skipped) Console.WriteLine($"  ~ {s}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"install failed: {ex.Message}");
            return 1;
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task<int> RunSmokeUninstall(string modId)
    {
        var (paths, client, _, installer) = BuildHeadlessServices();
        try
        {
            Console.WriteLine($"smoke uninstall {modId}");
            var ok = await installer.UninstallAsync(modId);
            Console.WriteLine(ok ? "uninstalled" : "no record found");
            return ok ? 0 : 2;
        }
        finally
        {
            client.Dispose();
        }
    }

    private static (PathService paths, PlatformApiClient client, InstalledModsService state, ModInstaller installer) BuildHeadlessServices()
    {
        var paths = new PathService();
        var options = PlatformOptions.Load(paths);
        var client = new PlatformApiClient(options);
        var state = new InstalledModsService(paths);
        var installer = new ModInstaller(paths, client, state);
        return (paths, client, state, installer);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static int LaunchModdedHeadless()
    {
        var paths = new PathService();
        var platform = new PlatformInfo();
        var gameProcess = new GameProcessService(paths, platform);

        Console.WriteLine("Launching modded game...");
        gameProcess.LaunchModded();

        if (gameProcess.CurrentProcess is { } proc)
        {
            proc.WaitForExit();
            var code = proc.ExitCode;
            Console.WriteLine($"Game exited with code {code}");
            return code;
        }

        Console.Error.WriteLine("Failed to start game process.");
        return 1;
    }
}
