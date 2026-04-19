namespace Anomaly.Installer;

/// <summary>
/// Parsed CLI arguments for the Installer.
/// </summary>
internal sealed class InstallerArgs
{
    public bool ShowHelp { get; init; }
    public bool DryRun { get; init; }
    public bool Uninstall { get; init; }
    public string? VanillaPath { get; init; }

    /// <summary>Unpacker timeout in milliseconds. Null means "use Unpacker default".</summary>
    public int? UnpackTimeoutMs { get; init; }

    public static InstallerArgs Parse(string[] args)
    {
        bool showHelp = false;
        bool dryRun = false;
        bool uninstall = false;
        string? vanillaPath = null;
        int? unpackTimeoutMs = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                a == "/?")
            {
                showHelp = true;
            }
            else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
            }
            else if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                uninstall = true;
            }
            else if (string.Equals(a, "--vanilla-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                vanillaPath = args[++i];
            }
            else if (string.Equals(a, "--unpack-timeout", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out int seconds) || seconds <= 0)
                    throw new InstallerException($"--unpack-timeout expects a positive integer (seconds). Got: {args[i]}");
                unpackTimeoutMs = seconds * 1000;
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
            Uninstall = uninstall,
            VanillaPath = vanillaPath,
            UnpackTimeoutMs = unpackTimeoutMs,
        };
    }

    public static void PrintHelp()
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anomaly");

        Console.WriteLine("Anomaly Client Installer");
        Console.WriteLine();
        Console.WriteLine("Patches a vanilla copy of SCP: Secret Laboratory and installs MelonLoader");
        Console.WriteLine("+ the Anomaly framework into the directory containing this installer.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Anomaly.Installer [--vanilla-path <path>] [--dry-run] [--unpack-timeout <s>] [--help]");
        Console.WriteLine("  Anomaly.Installer --uninstall");
        Console.WriteLine("  Anomaly.Installer <path-to-SCPSL.exe>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --vanilla-path <path>  Path to the vanilla SCPSL.exe or its containing");
        Console.WriteLine("                         directory. If omitted, the installer tries to");
        Console.WriteLine("                         auto-detect via Steam, then prompts if needed.");
        Console.WriteLine("  --dry-run              Print every step and required path; skip all");
        Console.WriteLine("                         writes. Useful for validating paths before a real run.");
        Console.WriteLine("  --uninstall            Restore every vanilla .bak, then delete MelonLoader/");
        Console.WriteLine("                         Mods/, UserLibs/, and the patched launcher.");
        Console.WriteLine("  --unpack-timeout <s>   Seconds to wait for Themida unpacker before aborting");
        Console.WriteLine("                         (default: 45). Increase on very slow machines.");
        Console.WriteLine("  --help, -h, /?         Show this help text and exit.");
        Console.WriteLine();
        Console.WriteLine($"Logs: {Path.Combine(appDataDir, "Logs", "install-{timestamp}.log")}");
        Console.WriteLine($"State: {Path.Combine(appDataDir, "vanilla_path.txt")} (read by Anomaly.Launcher)");
    }
}
