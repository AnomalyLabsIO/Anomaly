using Anomaly.Installer;
using Anomaly.InstallerCore;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var parsedArgs = InstallerArgs.Parse(args);

        if (parsedArgs.ShowHelp)
        {
            InstallerArgs.PrintHelp();
            return 0;
        }

        string? logPath = InstallLogger.Init();

        try
        {
            ConsoleUI.PrintBanner();
            if (logPath != null)
                Console.WriteLine($"Log file: {logPath}");
            if (parsedArgs.DryRun)
                ConsoleUI.PrintWarning("DRY RUN — no files will be written.");
            Console.WriteLine();

            string installerDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

            if (parsedArgs.Uninstall)
            {
                int code = InstallerService.Uninstall(installerDir);
                WaitForExit();
                return code;
            }

            var options = new InstallOptions
            {
                InstallerDir = installerDir,
                VanillaPath = parsedArgs.VanillaPath,
                TargetVersion = parsedArgs.TargetVersion,
                DryRun = parsedArgs.DryRun,
                SkipPatches = parsedArgs.SkipPatches,
                NoAnomaly = parsedArgs.NoAnomaly,
                NoMelon = parsedArgs.NoMelon,
                UnpackTimeoutMs = parsedArgs.UnpackTimeoutMs,
            };

            InstallerService.Install(options);
            WaitForExit();
            return 0;
        }
        catch (InstallerException ex)
        {
            ConsoleUI.PrintError(ex.Message);
            PrintLogPathHint(logPath);
            WaitForExit();
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"Unexpected error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            PrintLogPathHint(logPath);
            WaitForExit();
            return 1;
        }
        finally
        {
            InstallLogger.Shutdown();
        }
    }

    private static void WaitForExit()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(true); }
        catch (InvalidOperationException) { /* stdin redirected, just exit */ }
    }

    private static void PrintLogPathHint(string? logPath)
    {
        if (string.IsNullOrEmpty(logPath)) return;
        Console.WriteLine();
        Console.WriteLine($"Full log: {logPath}");
    }
}
