using Avalonia;
using Anomaly.InstallerCore.Platform;
using Anomaly.Launcher.Services;

namespace Anomaly.Launcher;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--modded"))
            return LaunchModdedHeadless();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
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
