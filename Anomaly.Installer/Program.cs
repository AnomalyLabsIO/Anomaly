using Anomaly.Installer;
using Anomaly.Installer.IL2Cpp;
using Anomaly.Installer.MelonLoader;
using Anomaly.Installer.Patching;
using Anomaly.Installer.Platform;
using Anomaly.Installer.Unpacking;
using Newtonsoft.Json;

public static class Program
{
    private const int TotalSteps = 6;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            ConsoleUI.PrintBanner();
            Run(args);
            return 0;
        }
        catch (InstallerException ex)
        {
            ConsoleUI.PrintError(ex.Message);
            WaitForExit();
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"Unexpected error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            WaitForExit();
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        var platform = new PlatformInfo();
        // Use the exe's actual directory, not the single-file extraction temp dir
        string installerDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        Console.WriteLine($"Platform: {(platform.IsWindows ? "Windows" : platform.IsLinux ? "Linux" : "Unknown")}");
        Console.WriteLine($"Install directory: {installerDir}");
        Console.WriteLine();

        // Validate bundle contents
        string patchInfoPath = Path.Combine(installerDir, "patchinfo.json");
        string unlicensePath = Path.Combine(installerDir, "unlicense.exe");
        string cleanLauncherPath = Path.Combine(installerDir, "SCPSL.exe");

        ValidateBundle(patchInfoPath, unlicensePath, cleanLauncherPath);

        // Load patch config up front
        PatchInfo patchInfo = LoadPatchInfo(patchInfoPath);
        Console.WriteLine($"Patch config loaded (metadata version: {patchInfo.metadataVersion}, {patchInfo.patchMethods.Length} patch methods)");
        Console.WriteLine();

        // Get vanilla game path
        string vanillaPath = GetVanillaPath(args);
        string vanillaDir = Path.GetDirectoryName(vanillaPath)!;
        Console.WriteLine($"Vanilla game directory: {vanillaDir}");
        Console.WriteLine();

        // Step 1: Copy game files
        ConsoleUI.PrintStep(1, TotalSteps, "Copying game files...");
        CopyGameFiles(vanillaDir, installerDir);
        ConsoleUI.PrintSuccess("Game files copied.");
        Console.WriteLine();

        // Step 2: Unpack (if Themida-protected) and load IL2CPP
        ConsoleUI.PrintStep(2, TotalSteps, "Loading IL2CPP...");
        string gameAssemblyPath = Path.Combine(installerDir, "GameAssembly.dll");
        string metadataPath = Path.Combine(installerDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        // Fix metadata version BEFORE IL2CPP loading — Il2CppDumper needs a
        // compatible version byte to parse the file. The game's original metadata
        // may have a version that Il2CppDumper doesn't recognize.
        Patcher.FixMetadataVersion(patchInfo.metadataVersion, installerDir);

        byte[] gameAssemblyData = LoadAndUnpackGameAssembly(
            gameAssemblyPath, metadataPath, unlicensePath, platform, installerDir, patchInfo.metadataVersion);

        var il2CppManager = new Il2CppManager(gameAssemblyData, installerDir);
        il2CppManager.LoadIl2Cpp(metadataPath, patchInfo.metadataVersion);

        Console.WriteLine("Dumping IL2CPP...");
        il2CppManager.DumpIl2Cpp();
        il2CppManager.GetScriptJson();
        il2CppManager.GetMethodsDictionary();
        ConsoleUI.PrintSuccess("IL2CPP loaded and parsed.");
        Console.WriteLine();

        // Step 3: Patch GameAssembly
        ConsoleUI.PrintStep(3, TotalSteps, "Applying patches...");
        var patcher = new Patcher(gameAssemblyPath, gameAssemblyData, il2CppManager, patchInfo);
        patcher.PatchGameAssembly();
        patcher.SaveGameAssembly();
        ConsoleUI.PrintSuccess("GameAssembly.dll patched.");
        Console.WriteLine();

        // Step 4: Clean launcher is already in place from the bundle
        ConsoleUI.PrintStep(4, TotalSteps, "Verifying clean launcher...");
        if (File.Exists(cleanLauncherPath))
            ConsoleUI.PrintSuccess("Clean SCPSL.exe launcher is in place.");
        else
            ConsoleUI.PrintWarning("Clean SCPSL.exe not found in bundle directory. You may need to add it manually.");
        Console.WriteLine();

        // Step 5: Install MelonLoader
        ConsoleUI.PrintStep(5, TotalSteps, "Installing MelonLoader 0.7.2...");
        MelonLoaderInstaller.Install(installerDir);
        ConsoleUI.PrintSuccess("MelonLoader installed.");
        Console.WriteLine();

        // Step 6: Verify mod files
        ConsoleUI.PrintStep(6, TotalSteps, "Verifying mod files...");
        VerifyModFiles(installerDir);
        Console.WriteLine();

        // Cleanup
        il2CppManager.RemoveDumpFolder();

        // Done
        Console.WriteLine();
        ConsoleUI.PrintSuccess("========================================");
        ConsoleUI.PrintSuccess("  Installation complete!");
        ConsoleUI.PrintSuccess("========================================");
        Console.WriteLine();
        Console.WriteLine("Please launch SCPSL.exe once to generate IL2CPP assemblies.");
        Console.WriteLine("The first launch will take longer than usual.");
        Console.WriteLine();
        WaitForExit();
    }

    private static void WaitForExit()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(true); }
        catch (InvalidOperationException) { /* stdin redirected, just exit */ }
    }

    private static void ValidateBundle(string patchInfoPath, string unlicensePath, string cleanLauncherPath)
    {
        if (!File.Exists(patchInfoPath))
            throw new InstallerException($"patchinfo.json not found at: {patchInfoPath}");
        if (!File.Exists(unlicensePath))
            throw new InstallerException($"unlicense.exe not found at: {unlicensePath}");
        if (!File.Exists(cleanLauncherPath))
            ConsoleUI.PrintWarning("Clean SCPSL.exe launcher not found in bundle. It will not be replaced.");
    }

    private static string GetVanillaPath(string[] args)
    {
        // Check for --vanilla-path command line argument
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--vanilla-path")
                return args[i + 1];
        }

        // Also accept as first positional argument
        if (args.Length > 0 && File.Exists(args[0]) && args[0].EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
            return args[0];

        // Interactive prompt
        Console.WriteLine("Please enter the path to your vanilla SCPSL.exe:");
        Console.WriteLine("(e.g., C:\\Program Files\\Steam\\steamapps\\common\\SCP Secret Laboratory\\SCPSL.exe)");
        string path = ConsoleUI.PromptPath("Path to SCPSL.exe");

        if (!File.Exists(path))
            throw new InstallerException($"File not found: {path}");
        if (!path.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("Please select the SCPSL.exe file.");

        return path;
    }

    private static void CopyGameFiles(string sourceDir, string targetDir)
    {
        var skipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SL-AC.dll",
            "SCPSL.exe"
        };

        // Count total files and bytes for the progress bar
        var sourceInfo = new DirectoryInfo(sourceDir);
        var allFiles = sourceInfo.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => !skipFiles.Contains(f.Name))
            .ToArray();
        long totalBytes = allFiles.Sum(f => f.Length);
        int totalFiles = allFiles.Length;

        Console.WriteLine($"  {totalFiles} files ({ConsoleUI.FormatBytes(totalBytes)})");

        long copiedBytes = 0;
        int copiedFiles = 0;
        CopyFilesRecursively(sourceInfo, new DirectoryInfo(targetDir), skipFiles,
            ref copiedFiles, totalFiles, ref copiedBytes, totalBytes);
    }

    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target, HashSet<string> skipFiles,
        ref int copiedFiles, int totalFiles, ref long copiedBytes, long totalBytes)
    {
        foreach (DirectoryInfo dir in source.GetDirectories())
        {
            string targetSubDir = Path.Combine(target.FullName, dir.Name);
            Directory.CreateDirectory(targetSubDir);
            CopyFilesRecursively(dir, new DirectoryInfo(targetSubDir), skipFiles,
                ref copiedFiles, totalFiles, ref copiedBytes, totalBytes);
        }

        foreach (FileInfo file in source.GetFiles())
        {
            if (skipFiles.Contains(file.Name))
                continue;

            string destPath = Path.Combine(target.FullName, file.Name);
            file.CopyTo(destPath, true);

            copiedFiles++;
            copiedBytes += file.Length;
            ConsoleUI.PrintProgressBar("Copying files",
                copiedBytes, totalBytes,
                $"({copiedFiles:N0} / {totalFiles:N0})");
        }
    }

    /// <summary>
    /// Attempts to load GameAssembly.dll via Il2CppDumper to check for Themida protection.
    /// If protected, unpacks it with unlicense.exe and returns the fresh bytes.
    /// If not protected (or already unpacked), returns the original bytes.
    /// </summary>
    private static byte[] LoadAndUnpackGameAssembly(string gameAssemblyPath, string metadataPath, string unlicensePath, PlatformInfo platform, string workingDir, int metadataVersion)
    {
        if (!File.Exists(gameAssemblyPath))
            throw new InstallerException("GameAssembly.dll not found. Game files may not have copied correctly.");

        byte[] data = Patcher.GetGameAssemblyData(gameAssemblyPath);

        // Try loading to detect if it's Themida-protected
        var testManager = new Il2CppManager(data, workingDir);
        try
        {
            testManager.LoadIl2Cpp(metadataPath, metadataVersion);
            // Success — file is not protected, return the data as-is
            testManager.RemoveDumpFolder();
            return data;
        }
        catch (FileIsProtectedException)
        {
            Console.WriteLine("Themida protection detected. Running unpacker...");
            var unpacker = new Unpacker(unlicensePath, gameAssemblyPath, platform);
            unpacker.UnpackGameAssembly();

            // Return fresh bytes from the unpacked file
            return Patcher.GetGameAssemblyData(gameAssemblyPath);
        }
    }

    private static PatchInfo LoadPatchInfo(string path)
    {
        string json = File.ReadAllText(path);
        PatchInfo? patchInfo = JsonConvert.DeserializeObject<PatchInfo>(json);
        if (patchInfo == null || patchInfo.patchMethods.Length == 0)
            throw new InstallerException("Failed to load patchinfo.json or no patch methods defined.");
        return patchInfo;
    }

    private static void VerifyModFiles(string installerDir)
    {
        string modsDir = Path.Combine(installerDir, "Mods");
        string userLibsDir = Path.Combine(installerDir, "UserLibs");

        if (Directory.Exists(modsDir))
        {
            string[] mods = Directory.GetFiles(modsDir, "*.dll");
            Console.WriteLine($"  Mods directory: {mods.Length} DLL(s) found");
            foreach (string mod in mods)
                Console.WriteLine($"    - {Path.GetFileName(mod)}");
        }
        else
        {
            ConsoleUI.PrintWarning("Mods directory not found. No mods will be loaded.");
        }

        if (Directory.Exists(userLibsDir))
        {
            string[] libs = Directory.GetFiles(userLibsDir, "*.dll");
            Console.WriteLine($"  UserLibs directory: {libs.Length} DLL(s) found");
            foreach (string lib in libs)
                Console.WriteLine($"    - {Path.GetFileName(lib)}");
        }
        else
        {
            ConsoleUI.PrintWarning("UserLibs directory not found.");
        }
    }
}
