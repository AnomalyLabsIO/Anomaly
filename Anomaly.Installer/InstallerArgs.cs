namespace Anomaly.Installer;

/// <summary>
/// Parsed CLI arguments for the Installer.
/// </summary>
internal sealed class InstallerArgs
{
    public bool ShowHelp { get; init; }
    public bool DryRun { get; init; }
    public string? VanillaPath { get; init; }

    public static InstallerArgs Parse(string[] args)
    {
        bool showHelp = false;
        bool dryRun = false;
        string? vanillaPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a is "--help" or "-h" or "/?")
            {
                showHelp = true;
            }
            else if (a is "--dry-run")
            {
                dryRun = true;
            }
            else if (a == "--vanilla-path" && i + 1 < args.Length)
            {
                vanillaPath = args[++i];
            }
            else if (vanillaPath is null
                     && File.Exists(a)
                     && a.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Positional convenience: drag-and-drop an SCPSL.exe onto the installer.
                vanillaPath = a;
            }
        }

        return new InstallerArgs
        {
            ShowHelp = showHelp,
            DryRun = dryRun,
            VanillaPath = vanillaPath,
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Anomaly Client Installer");
        Console.WriteLine();
        Console.WriteLine("Patches a vanilla copy of SCP: Secret Laboratory and installs MelonLoader");
        Console.WriteLine("+ the Anomaly framework into the directory containing this installer.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Anomaly.Installer [--vanilla-path <path>] [--dry-run] [--help]");
        Console.WriteLine("  Anomaly.Installer <path-to-SCPSL.exe>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --vanilla-path <path>  Path to the vanilla SCPSL.exe or its containing");
        Console.WriteLine("                         directory. If omitted, you'll be prompted.");
        Console.WriteLine("  --dry-run              Print every step and required path; skip all");
        Console.WriteLine("                         writes. Useful for validating paths before a real run.");
        Console.WriteLine("  --help, -h, /?         Show this help text and exit.");
        Console.WriteLine();
        Console.WriteLine("Logs: %APPDATA%/Anomaly/Logs/install-{timestamp}.log");
        Console.WriteLine("State: %APPDATA%/Anomaly/vanilla_path.txt (read by Anomaly.Launcher)");
    }
}
