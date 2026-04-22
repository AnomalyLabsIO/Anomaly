using Anomaly.InstallerCore.Patching;

namespace Anomaly.InstallerCore;

/// <summary>
/// Reverts an Anomaly install back to vanilla. Restores every <c>.bak</c> sidecar
/// created by <see cref="BackupManager"/>, then removes the files Anomaly added
/// (patched launcher, MelonLoader, mods, vanilla-path pointer). Intentionally
/// idempotent: running it on a partial install is safe.
/// </summary>
public static class Uninstaller
{
    public static int Run(string installerDir)
    {
        Console.WriteLine($"Uninstalling Anomaly from: {installerDir}");
        Console.WriteLine();

        if (!ConsoleUI.PromptYesNo("This will restore vanilla game files and delete Anomaly-added files. Continue?"))
        {
            Console.WriteLine("Uninstall cancelled.");
            return 0;
        }
        Console.WriteLine();

        int failures = 0;
        failures += RestoreIfBacked(Path.Combine(installerDir, "GameAssembly.dll"));
        failures += RestoreIfBacked(Path.Combine(installerDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
        failures += RestoreIfBacked(Path.Combine(installerDir, "SCPSL_Data", "globalgamemanagers"));

        // Anomaly-added directories.
        DeleteDirectory(Path.Combine(installerDir, "MelonLoader"));
        DeleteDirectory(Path.Combine(installerDir, "Mods"));
        DeleteDirectory(Path.Combine(installerDir, "UserLibs"));
        DeleteDirectory(Path.Combine(installerDir, "il2cppdumper"));

        // Our copy of the clean launcher (the original is under .bak handling above).
        DeleteFile(Path.Combine(installerDir, "SCPSL.exe"));

        // Clear the Launcher-visible vanilla-path pointer so a fresh install starts clean.
        string vanillaPathFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anomaly", "vanilla_path.txt");
        DeleteFile(vanillaPathFile);

        Console.WriteLine();
        if (failures == 0)
            ConsoleUI.PrintSuccess("Uninstall complete.");
        else
            ConsoleUI.PrintWarning($"Uninstall finished with {failures} warning(s). See above.");

        return failures == 0 ? 0 : 1;
    }

    private static int RestoreIfBacked(string filePath)
    {
        if (!BackupManager.HasBackup(filePath))
        {
            Console.WriteLine($"  No backup for {filePath} — skipping.");
            return 0;
        }

        try
        {
            BackupManager.RestoreFile(filePath);
            Console.WriteLine($"  Restored {filePath} from backup.");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"Failed to restore {filePath}: {ex.Message}");
            return 1;
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            Directory.Delete(path, recursive: true);
            Console.WriteLine($"  Deleted {path}");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Could not delete {path}: {ex.Message}");
        }
    }

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.Delete(path);
            Console.WriteLine($"  Deleted {path}");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Could not delete {path}: {ex.Message}");
        }
    }
}
