using Anomaly.Installer;
using Anomaly.Installer.IL2Cpp;
using Anomaly.Installer.MelonLoader;
using Anomaly.Installer.Patching;
using Anomaly.Installer.Platform;
using Anomaly.Installer.Unpacking;
using Newtonsoft.Json;

public static class Program
{
    private const int TotalSteps = 7;

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

            if (parsedArgs.Uninstall)
            {
                string installerDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                int code = Uninstaller.Run(installerDir);
                WaitForExit();
                return code;
            }

            Run(parsedArgs);
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

    private static void Run(InstallerArgs args)
    {
        var platform = new PlatformInfo();
        string installerDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        Console.WriteLine($"Platform: {(platform.IsWindows ? "Windows" : platform.IsLinux ? "Linux" : "Unknown")}");
        PrintPlatformSummary(platform);
        Console.WriteLine($"Install directory: {installerDir}");
        Console.WriteLine();

        // Load patch config from embedded resource
        PatchInfo patchInfo = LoadPatchInfo();
        Console.WriteLine($"Patch config loaded (metadata version: {patchInfo.metadataVersion}, {patchInfo.patchMethods.Length} patch methods)");
        Console.WriteLine();

        // Get vanilla game path
        string vanillaPath = GetVanillaPath(args, platform);
        string vanillaDir = Path.GetDirectoryName(vanillaPath)!;
        Console.WriteLine($"Vanilla game directory: {vanillaDir}");
        Console.WriteLine();

        // Preflight before anything destructive — fail loud if we can't write or don't
        // have disk space, and surface Linux runtime availability before patching begins.
        Preflight.Run(installerDir, vanillaDir, platform, args.DryRun);

        // Save vanilla path to the canonical Launcher-visible location (%APPDATA%/Anomaly/).
        // Shared schema: the Launcher reads this same file via PathService.VanillaPathFile.
        string anomalyDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anomaly");
        string vanillaPathFile = Path.Combine(anomalyDataDir, "vanilla_path.txt");
        if (!args.DryRun)
        {
            Directory.CreateDirectory(anomalyDataDir);
            File.WriteAllText(vanillaPathFile, vanillaDir);
            Console.WriteLine($"Vanilla path saved to {vanillaPathFile}");
        }
        else
        {
            Console.WriteLine($"[dry-run] Would write vanilla path to {vanillaPathFile}");
        }
        Console.WriteLine();

        // Step 1: Copy game files
        ConsoleUI.PrintStep(1, TotalSteps, "Copying game files...");
        if (!args.DryRun)
        {
            CopyGameFiles(vanillaDir, installerDir);
            ConsoleUI.PrintSuccess("Game files copied.");
        }
        else
        {
            Console.WriteLine($"[dry-run] Would copy game files from {vanillaDir} to {installerDir}");
        }
        Console.WriteLine();

        // Step 2: Extract clean launcher from embedded resources
        ConsoleUI.PrintStep(2, TotalSteps, "Extracting clean launcher...");
        string cleanLauncherPath = Path.Combine(installerDir, "SCPSL.exe");
        if (!args.DryRun)
        {
            EmbeddedResources.ExtractToFile("SCPSL.exe", cleanLauncherPath);
            ConsoleUI.PrintSuccess("Clean SCPSL.exe extracted.");
        }
        else
        {
            Console.WriteLine($"[dry-run] Would extract embedded SCPSL.exe to {cleanLauncherPath}");
        }
        Console.WriteLine();

        // In dry-run we stop before the destructive steps (we can't load IL2CPP without a
        // real GameAssembly.dll in place, and users just want a path sanity check).
        // We do still exercise the read-only checks for steps 3-6 so path/config issues
        // surface here rather than mid-install.
        if (args.DryRun)
        {
            DryRunRemainingSteps(installerDir, vanillaDir, patchInfo);
            Console.WriteLine();
            Console.WriteLine("[dry-run] Dry run complete. Re-run without --dry-run to install.");
            Console.WriteLine();
            WaitForExit();
            return;
        }

        // Step 3: Unpack (if Themida-protected) and load IL2CPP
        ConsoleUI.PrintStep(3, TotalSteps, "Loading IL2CPP...");
        string gameAssemblyPath = Path.Combine(installerDir, "GameAssembly.dll");
        string metadataPath = Path.Combine(installerDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string bundleVersionPath = Path.Combine(installerDir, "SCPSL_Data", "globalgamemanagers");

        // Backup vanilla-copies of files we're about to mutate. First successful install
        // captures the vanilla snapshot; subsequent re-runs reuse the existing .bak.
        // On failure further down this method, we restore from these backups.
        BackupManager.BackupFile(gameAssemblyPath);
        BackupManager.BackupFile(metadataPath);
        BackupManager.BackupFile(bundleVersionPath);

        Il2CppManager il2CppManager;
        byte[] gameAssemblyData;
        try
        {
            // Fix metadata version BEFORE IL2CPP loading
            Patcher.FixMetadataVersion(patchInfo.metadataVersion, installerDir);

            gameAssemblyData = LoadAndUnpackGameAssembly(
                gameAssemblyPath, metadataPath, platform, installerDir, patchInfo.metadataVersion, args.UnpackTimeoutMs);

            il2CppManager = new Il2CppManager(gameAssemblyData, installerDir);
            il2CppManager.LoadIl2Cpp(metadataPath, patchInfo.metadataVersion);

            Console.WriteLine("  Dumping IL2CPP...");
            il2CppManager.DumpIl2Cpp();
            il2CppManager.GetScriptJson();
            il2CppManager.GetMethodsDictionary();
            ConsoleUI.PrintSuccess("IL2CPP loaded and parsed.");
            Console.WriteLine();

            // Step 4: Patch GameAssembly
            ConsoleUI.PrintStep(4, TotalSteps, "Applying patches...");
            var patcher = new Patcher(gameAssemblyPath, gameAssemblyData, il2CppManager, patchInfo);
            patcher.PatchGameAssembly();
            patcher.SaveGameAssembly();
            patcher.VerifySavedAssembly();
            Console.WriteLine("  Re-parsing patched GameAssembly to verify structure...");
            patcher.VerifyPatchedStructure(metadataPath, patchInfo.metadataVersion, installerDir);
            ConsoleUI.PrintSuccess("GameAssembly.dll patched.");
            Console.WriteLine();

            // Step 5: Fix game version in Unity metadata
            ConsoleUI.PrintStep(5, TotalSteps, "Fixing game version...");
            var version = Patcher.ReadGameVersion(gameAssemblyData, il2CppManager);
            if (version.HasValue)
            {
                var (major, minor, revision) = version.Value;
                Console.WriteLine($"  Detected game version: {major}.{minor}.{revision}");
                Patcher.FixBundleVersion(installerDir, major, minor, revision);
                ConsoleUI.PrintSuccess("Game version fixed.");
            }
            else
            {
                ConsoleUI.PrintWarning("Could not detect game version — bundle version unchanged.");
            }
            Console.WriteLine();
        }
        catch
        {
            ConsoleUI.PrintError("Patching failed — restoring vanilla files from backup.");
            SafeRestore(gameAssemblyPath);
            SafeRestore(metadataPath);
            SafeRestore(bundleVersionPath);
            throw;
        }

        // Step 6: Install MelonLoader
        ConsoleUI.PrintStep(6, TotalSteps, $"Installing MelonLoader {MelonLoaderInstaller.PinnedVersion()}...");
        MelonLoaderInstaller.Install(installerDir);
        ConsoleUI.PrintSuccess("MelonLoader installed.");
        Console.WriteLine();

        // Step 7: Verify mod files
        ConsoleUI.PrintStep(7, TotalSteps, "Verifying mod files...");
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
        PrintPostInstallInstructions(platform, installerDir);
        Console.WriteLine();
        WaitForExit();
    }

    private static void PrintPlatformSummary(PlatformInfo platform)
    {
        if (!platform.IsLinux || platform.LinuxRuntime == null)
            return;

        LinuxRuntimeInfo runtime = platform.LinuxRuntime;

        if (runtime.IsSteamOs)
            Console.WriteLine("Linux environment: SteamOS detected");
        else if (runtime.IsSteamDeckHardware)
            Console.WriteLine("Linux environment: Steam Deck hardware detected");

        if (runtime.HasProton)
            Console.WriteLine($"Preferred Linux runtime: Proton ({runtime.PreferredProton!.Name})");
        else if (runtime.HasWine)
            Console.WriteLine($"Preferred Linux runtime: Wine ({runtime.WineCommand})");
        else
            Console.WriteLine("Preferred Linux runtime: none detected yet");
    }

    private static void PrintPostInstallInstructions(PlatformInfo platform, string installerDir)
    {
        string moddedExe = Path.Combine(installerDir, "SCPSL.exe");

        if (platform.IsLinux)
        {
            Console.WriteLine("Linux next steps:");
            Console.WriteLine($"  1. Add this modded build to Steam as a non-Steam game: {moddedExe}");
            Console.WriteLine("  2. In the shortcut properties, force a Steam Play compatibility tool (Proton).");
            Console.WriteLine("  3. Set Steam launch options to: WINEDLLOVERRIDES=\"version=n,b\" %command%");
            Console.WriteLine("  4. Launch it once through Steam to let MelonLoader generate its files.");
            Console.WriteLine("  5. If MelonLoader still does not start, install the Windows .NET 6 Desktop Runtime into the Proton prefix used by that shortcut.");
            Console.WriteLine("  6. The first launch will take longer than usual.");
            return;
        }

        Console.WriteLine("Please launch SCPSL.exe once to generate IL2CPP assemblies.");
        Console.WriteLine("The first launch will take longer than usual.");
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

    /// <summary>
    /// Restores a backup file, converting any restore-time failure into a clear error message.
    /// We're already unwinding from a patch failure; a failed restore is bad news that the
    /// user needs to see — point them at the .bak they can copy back manually.
    /// </summary>
    private static void SafeRestore(string filePath)
    {
        try
        {
            if (!BackupManager.RestoreFile(filePath))
                ConsoleUI.PrintWarning($"No backup found for {filePath} — nothing to restore.");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError(
                $"Failed to restore {filePath} from backup: {ex.Message} " +
                $"The original is at {filePath}{BackupManager.BackupExtension} — copy it manually.");
        }
    }

    private static string GetVanillaPath(InstallerArgs args, PlatformInfo platform)
    {
        // If --vanilla-path was a directory, look up SCPSL.exe in it.
        if (!string.IsNullOrEmpty(args.VanillaPath))
        {
            string provided = args.VanillaPath;
            if (File.Exists(provided) && provided.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
                return provided;

            if (Directory.Exists(provided))
            {
                string exe = Path.Combine(provided, "SCPSL.exe");
                if (File.Exists(exe)) return exe;
                throw new InstallerException($"SCPSL.exe not found in directory: {provided}");
            }

            throw new InstallerException($"Vanilla path not found: {provided}");
        }

        // Auto-detect: offer a remembered/Steam path as the default so the user can just hit Enter.
        string? detected = GameDetector.TryFindVanilla(platform);
        if (detected != null)
        {
            Console.WriteLine($"Auto-detected vanilla SCPSL.exe at: {detected}");
            if (ConsoleUI.PromptYesNo("Use this path?"))
                return detected;
        }

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
            var destFile = new FileInfo(destPath);

            // FileInfo caches attributes on first property access — read Exists once,
            // reuse Length/LastWriteTimeUtc from the same snapshot instead of issuing
            // a fresh stat call per property.
            bool destExists = destFile.Exists;
            if (destExists && destFile.Length == file.Length && destFile.LastWriteTimeUtc == file.LastWriteTimeUtc)
            {
                copiedFiles++;
                copiedBytes += file.Length;
                ConsoleUI.PrintProgressBar("Copying files",
                    copiedBytes, totalBytes,
                    $"({copiedFiles:N0} / {totalFiles:N0})");
                continue;
            }

            file.CopyTo(destPath, overwrite: destExists);

            copiedFiles++;
            copiedBytes += file.Length;
            ConsoleUI.PrintProgressBar("Copying files",
                copiedBytes, totalBytes,
                $"({copiedFiles:N0} / {totalFiles:N0})");
        }
    }

    /// <summary>
    /// Attempts to load GameAssembly.dll via Il2CppDumper to check for Themida protection.
    /// If protected, extracts unlicense.exe from embedded resources, unpacks, and returns fresh bytes.
    /// </summary>
    private static byte[] LoadAndUnpackGameAssembly(string gameAssemblyPath, string metadataPath, PlatformInfo platform, string workingDir, int metadataVersion, int? unpackTimeoutMs)
    {
        if (!File.Exists(gameAssemblyPath))
            throw new InstallerException("GameAssembly.dll not found. Game files may not have copied correctly.");

        byte[] data = Patcher.GetGameAssemblyData(gameAssemblyPath);

        var testManager = new Il2CppManager(data, workingDir);
        try
        {
            testManager.LoadIl2Cpp(metadataPath, metadataVersion);
            testManager.RemoveDumpFolder();
            return data;
        }
        catch (FileIsProtectedException)
        {
            Console.WriteLine("  Themida protection detected. Extracting unpacker...");

            // Extract unlicense.exe to temp file
            string tempUnlicense = Path.Combine(Path.GetTempPath(), "anomaly_unlicense.exe");
            try
            {
                EmbeddedResources.ExtractToFile("unlicense.exe", tempUnlicense);

                Unpacker unpacker = unpackTimeoutMs is int ms
                    ? new Unpacker(tempUnlicense, gameAssemblyPath, platform, ms)
                    : new Unpacker(tempUnlicense, gameAssemblyPath, platform);
                unpacker.UnpackGameAssembly();

                return Patcher.GetGameAssemblyData(gameAssemblyPath);
            }
            finally
            {
                // Clean up temp file
                try { if (File.Exists(tempUnlicense)) File.Delete(tempUnlicense); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    private static PatchInfo LoadPatchInfo()
    {
        string json = EmbeddedResources.ReadText("patchinfo.json");
        PatchInfo? patchInfo = JsonConvert.DeserializeObject<PatchInfo>(json);
        if (patchInfo == null || patchInfo.patchMethods.Length == 0)
            throw new InstallerException("Failed to load embedded patchinfo.json or no patch methods defined.");
        return patchInfo;
    }

    /// <summary>
    /// Read-only previews of steps 3-6 for dry-run: we can't actually load IL2CPP or
    /// download MelonLoader, but we can verify required source files exist in the vanilla
    /// dir and that the MelonLoader download URL resolves. Anything that would fail during
    /// a real run here is better caught now.
    /// </summary>
    private static void DryRunRemainingSteps(string installerDir, string vanillaDir, PatchInfo patchInfo)
    {
        string gaPreview = Path.Combine(vanillaDir, "GameAssembly.dll");
        string mdPreview = Path.Combine(vanillaDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string ggmPreview = Path.Combine(vanillaDir, "SCPSL_Data", "globalgamemanagers");

        Console.WriteLine("[dry-run] Checking sources for steps 3-6:");
        DryRunCheckFile("  GameAssembly.dll", gaPreview);
        DryRunCheckFile("  global-metadata.dat", mdPreview);
        DryRunCheckFile("  globalgamemanagers", ggmPreview);

        Console.WriteLine($"[dry-run]   Patch config: metadataVersion={patchInfo.metadataVersion}, {patchInfo.patchMethods.Length} methods");

        MelonLoaderConfig mlConfig;
        try
        {
            string json = EmbeddedResources.ReadText("melonloader.json");
            mlConfig = JsonConvert.DeserializeObject<MelonLoaderConfig>(json)
                ?? throw new InstallerException("melonloader.json deserialised to null.");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"[dry-run] melonloader.json invalid: {ex.Message}");
            return;
        }

        Console.WriteLine($"[dry-run]   MelonLoader: v{mlConfig.version} from {mlConfig.downloadUrl}");
        if (string.IsNullOrWhiteSpace(mlConfig.sha256))
            ConsoleUI.PrintWarning("[dry-run] No pinned SHA-256 for the MelonLoader release.");
        else
            Console.WriteLine($"[dry-run]   SHA-256: {mlConfig.sha256}");

        Console.WriteLine();
        Console.WriteLine("[dry-run] Steps that WOULD run on a real install:");
        Console.WriteLine($"[dry-run]   3. Load IL2CPP from {Path.Combine(installerDir, "GameAssembly.dll")}");
        Console.WriteLine($"[dry-run]   4. Backup + patch GameAssembly.dll, global-metadata.dat, globalgamemanagers");
        Console.WriteLine($"[dry-run]   5. Fix bundle version in globalgamemanagers");
        Console.WriteLine($"[dry-run]   6. Download MelonLoader {mlConfig.version} into {installerDir}");
        Console.WriteLine($"[dry-run]   7. Verify Mods/ and UserLibs/");
    }

    private static void DryRunCheckFile(string label, string path)
    {
        if (File.Exists(path))
        {
            long size = new FileInfo(path).Length;
            Console.WriteLine($"[dry-run] {label}: OK ({ConsoleUI.FormatBytes(size)}) at {path}");
        }
        else
        {
            ConsoleUI.PrintError($"[dry-run] {label}: MISSING at {path} — real install would fail here.");
        }
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
