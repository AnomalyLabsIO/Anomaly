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

    private const string BundleVersionRelativePath = @"SCPSL_Data\globalgamemanagers";

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
        Console.WriteLine($"Patch config loaded ({patchInfo.versionRanges.Length} version range(s) defined)");
        if (args.SkipPatches)
            Console.WriteLine("  --skip-patches: IL2CPP patches and bundle-version fix will be skipped.");
        if (args.NoAnomaly)
            Console.WriteLine("  --no-anomaly: Anomaly-specific content (Mods/UserLibs check) will be skipped.");
        if (args.NoMelon)
            Console.WriteLine("  --no-melon: MelonLoader installation will be skipped.");
        Console.WriteLine();

        // Get vanilla game path
        string vanillaPath = GetVanillaPath(args, platform);
        string vanillaDir = Path.GetDirectoryName(vanillaPath)!;
        Console.WriteLine($"Vanilla game directory: {vanillaDir}");
        Console.WriteLine();

        // Refuse to patch the vanilla install in place — the installer's parent directory
        // becomes the *modded* target, so if they're the same directory we'd overwrite the
        // user's Steam-managed SCPSL.exe with the patched copy. Steam's file verification
        // would later roll this back, but only after Anomaly has scrambled GameAssembly.dll.
        //
        // Exception: the launcher's SteamCMD download lands files directly in the installer
        // directory on purpose, then points --vanilla-path here. We detect that flow by the
        // .anomaly_source.json sidecar the launcher writes on successful download.
        if (PathsAreSame(installerDir, vanillaDir))
        {
            string sidecar = Path.Combine(installerDir, ".anomaly_source.json");
            if (!File.Exists(sidecar))
                throw new InstallerException(
                    "Installer is running inside the vanilla SCP:SL directory. Move Anomaly.Installer " +
                    "into a separate folder (this will become the modded game directory) and rerun.");

            Console.WriteLine("Same-path install: files were downloaded into this directory by the launcher.");
            Console.WriteLine();
        }

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

        // Step 2: Place the clean launcher matching the selected Unity version. Resolved below
        // once we've picked a VersionRange. For now, defer — we need the range first.
        // (We also want to skip this entirely in --no-anomaly mode and keep the vanilla SCPSL.exe.)
        Console.WriteLine();

        // Resolve the VersionRange to patch against. When --no-anomaly is set we don't need one;
        // otherwise we detect the game version (from the bundleVersion in globalgamemanagers or
        // from --target-version) and look it up in patchinfo.json.
        VersionRange? selectedRange = ResolveVersionRange(args, installerDir, patchInfo);

        // In dry-run we stop before the destructive steps (we can't load IL2CPP without a
        // real GameAssembly.dll in place, and users just want a path sanity check).
        // We do still exercise the read-only checks for steps 3-6 so path/config issues
        // surface here rather than mid-install.
        if (args.DryRun)
        {
            DryRunRemainingSteps(installerDir, vanillaDir, patchInfo, selectedRange, args);
            Console.WriteLine();
            Console.WriteLine("[dry-run] Dry run complete. Re-run without --dry-run to install.");
            Console.WriteLine();
            WaitForExit();
            return;
        }

        // Step 2 (deferred): Place the clean (Themida-free) launcher. This always runs when we
        // have a matching Unity version — MelonLoader and any other modloader need a Themida-free
        // exe. Only skipped when we have no VersionRange at all to source one from.
        ConsoleUI.PrintStep(2, TotalSteps, "Placing clean launcher...");
        if (selectedRange != null)
        {
            CleanLauncherResolver.PlaceCleanLauncher(selectedRange, installerDir);
            ConsoleUI.PrintSuccess("Clean SCPSL.exe in place.");
        }
        else
        {
            ConsoleUI.PrintWarning("No VersionRange resolved — keeping the vanilla SCPSL.exe. The original exe may be Themida-protected and incompatible with mod loaders.");
        }
        Console.WriteLine();

        string gameAssemblyPath = Path.Combine(installerDir, "GameAssembly.dll");
        string metadataPath = Path.Combine(installerDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string bundleVersionPath = Path.Combine(installerDir, "SCPSL_Data", "globalgamemanagers");

        if (!args.SkipPatches && selectedRange != null)
        {
            // Step 3: Unpack (if Themida-protected) and load IL2CPP
            ConsoleUI.PrintStep(3, TotalSteps, "Loading IL2CPP...");

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
                Patcher.FixMetadataVersion(selectedRange.metadataVersion, installerDir);

                var (unpackResult, wasUnpacked) = LoadAndUnpackGameAssembly(
                    gameAssemblyPath, metadataPath, platform, installerDir, selectedRange.metadataVersion, args.UnpackTimeoutMs);
                gameAssemblyData = unpackResult;

                il2CppManager = new Il2CppManager(gameAssemblyData, installerDir);

                // After an unpack, PlusSearch may still fail on older Unity versions even though
                // the DLL is no longer packed. Skip the protection-check shortcut so Il2CppDumper
                // falls through to Search/SymbolSearch instead of re-throwing FileIsProtectedException.
                il2CppManager.LoadIl2Cpp(metadataPath, selectedRange.metadataVersion, skipProtectionCheck: wasUnpacked);

                Console.WriteLine("  Dumping IL2CPP...");
                il2CppManager.DumpIl2Cpp();
                il2CppManager.GetScriptJson();
                il2CppManager.GetMethodsDictionary();
                ConsoleUI.PrintSuccess("IL2CPP loaded and parsed.");
                Console.WriteLine();

                // Step 4: Patch GameAssembly
                ConsoleUI.PrintStep(4, TotalSteps, $"Applying patches for {selectedRange.versionLabel}...");
                var patcher = new Patcher(gameAssemblyPath, gameAssemblyData, il2CppManager, selectedRange);
                patcher.PatchGameAssembly();
                patcher.SaveGameAssembly();
                patcher.VerifySavedAssembly();
                Console.WriteLine("  Re-parsing patched GameAssembly to verify structure...");
                patcher.VerifyPatchedStructure(metadataPath, selectedRange.metadataVersion, installerDir);
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

            // Cleanup dumper scratch
            il2CppManager.RemoveDumpFolder();
        }
        else
        {
            var reason = args.SkipPatches ? "--skip-patches" : "no version range resolved";
            ConsoleUI.PrintStep(3, TotalSteps, $"Skipping IL2CPP load ({reason}).");
            ConsoleUI.PrintStep(4, TotalSteps, $"Skipping patches ({reason}).");
            ConsoleUI.PrintStep(5, TotalSteps, $"Skipping bundle-version fix ({reason}).");
            Console.WriteLine();
        }

        // Step 6: Install MelonLoader
        if (!args.NoMelon)
        {
            ConsoleUI.PrintStep(6, TotalSteps, $"Installing MelonLoader {MelonLoaderInstaller.PinnedVersion()}...");
            MelonLoaderInstaller.Install(installerDir);
            ConsoleUI.PrintSuccess("MelonLoader installed.");
        }
        else
        {
            ConsoleUI.PrintStep(6, TotalSteps, "Skipping MelonLoader (--no-melon).");
        }
        Console.WriteLine();

        // Step 7: Verify mod files. Anomaly-specific — the Mods/ and UserLibs/ directories are
        // dropped in by the Anomaly build process, not by the installer. In --no-anomaly the user
        // is bringing their own modloader/content so there's nothing for us to verify.
        if (!args.NoAnomaly)
        {
            ConsoleUI.PrintStep(7, TotalSteps, "Verifying mod files...");
            VerifyModFiles(installerDir);
        }
        else
        {
            ConsoleUI.PrintStep(7, TotalSteps, "Skipping mod-file check (--no-anomaly).");
        }
        Console.WriteLine();

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

    private static bool PathsAreSame(string a, string b)
    {
        try
        {
            string normA = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Case-insensitive on Windows; Linux filesystems are typically case-sensitive,
            // but Steam installs live under paths that are usually canonical-cased so this
            // comparison is still fine either way.
            var cmp = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(normA, normB, cmp);
        }
        catch
        {
            return false;
        }
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
        // SL-AC.dll is Valve's anti-cheat: it would defeat our patches and is not needed in the
        // modded install. SCPSL.exe IS copied — the patch flow's Step 2 overwrites it with the
        // clean launcher, and --no-anomaly keeps it as vanilla (we need some SCPSL.exe either way).
        var skipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SL-AC.dll",
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

            // Source == destination happens when the launcher's SteamCMD download lands
            // files directly in the installer directory; nothing to copy in that case.
            bool samePath = destExists &&
                string.Equals(destFile.FullName, file.FullName, StringComparison.OrdinalIgnoreCase);

            if (samePath || (destExists && destFile.Length == file.Length && destFile.LastWriteTimeUtc == file.LastWriteTimeUtc))
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
    private static (byte[] data, bool wasUnpacked) LoadAndUnpackGameAssembly(string gameAssemblyPath, string metadataPath, PlatformInfo platform, string workingDir, int metadataVersion, int? unpackTimeoutMs)
    {
        if (!File.Exists(gameAssemblyPath))
            throw new InstallerException("GameAssembly.dll not found. Game files may not have copied correctly.");

        byte[] data = Patcher.GetGameAssemblyData(gameAssemblyPath);

        var testManager = new Il2CppManager(data, workingDir);
        try
        {
            testManager.LoadIl2Cpp(metadataPath, metadataVersion);
            testManager.RemoveDumpFolder();
            return (data, wasUnpacked: false);
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

                return (Patcher.GetGameAssemblyData(gameAssemblyPath), wasUnpacked: true);
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
        if (patchInfo == null || patchInfo.versionRanges.Length == 0)
            throw new InstallerException("Failed to load embedded patchinfo.json or no version ranges defined.");
        return patchInfo;
    }

    /// <summary>
    /// Picks the <see cref="VersionRange"/> to apply. Uses --target-version if given, otherwise
    /// reads the bundleVersion string from globalgamemanagers. Returns null when --no-anomaly is
    /// set (no range needed) or when detection failed but the user opted out of patching.
    /// Throws when a range is required but can't be resolved.
    /// </summary>
    private static VersionRange? ResolveVersionRange(InstallerArgs args, string installerDir, PatchInfo patchInfo)
    {
        GameVersion? detected = null;

        if (!string.IsNullOrWhiteSpace(args.TargetVersion))
        {
            if (!GameVersion.TryParse(args.TargetVersion, out var v))
                throw new InstallerException($"Invalid --target-version value: '{args.TargetVersion}' (expected X.Y.Z).");
            detected = v;
            Console.WriteLine($"Target version (from --target-version): {v}");
        }
        else
        {
            string bundleVersionPath = Path.Combine(installerDir, BundleVersionRelativePath);
            detected = Patcher.TryReadBundleVersionString(bundleVersionPath);
            if (detected.HasValue)
                Console.WriteLine($"Detected game version (from globalgamemanagers): {detected.Value}");
            else
                Console.WriteLine("Could not detect game version from globalgamemanagers.");
        }

        if (!detected.HasValue)
        {
            if (args.SkipPatches)
            {
                ConsoleUI.PrintWarning("Version unknown and --skip-patches set. Skipping clean-launcher placement too.");
                return null;
            }
            throw new InstallerException(
                "Could not detect the game version. Pass --target-version X.Y.Z, or re-run with " +
                "--skip-patches for an unsupported build.");
        }

        var range = patchInfo.versionRanges.FirstOrDefault(r => r.Contains(detected.Value));
        if (range == null)
        {
            var known = string.Join(", ", patchInfo.versionRanges.Select(r => r.versionLabel));
            if (args.SkipPatches)
            {
                ConsoleUI.PrintWarning(
                    $"No VersionRange brackets {detected.Value}. Known ranges: {known}. " +
                    "Clean-launcher placement will be skipped as well.");
                return null;
            }
            throw new InstallerException(
                $"No Anomaly patches available for game version {detected.Value}. " +
                $"Supported ranges: {known}. " +
                "Re-run with --skip-patches to keep the game files and clean launcher without patching.");
        }

        if (args.SkipPatches)
            Console.WriteLine($"Matched range: {range.versionLabel} (Unity {range.unityVersion}) — using its clean launcher, skipping patches.");
        else
            Console.WriteLine($"Selected patch range: {range.versionLabel} (metadata {range.metadataVersion}, {range.patchMethods.Length} patches)");
        Console.WriteLine();
        return range;
    }

    /// <summary>
    /// Read-only previews of steps 3-6 for dry-run: we can't actually load IL2CPP or
    /// download MelonLoader, but we can verify required source files exist in the vanilla
    /// dir and that the MelonLoader download URL resolves. Anything that would fail during
    /// a real run here is better caught now.
    /// </summary>
    private static void DryRunRemainingSteps(string installerDir, string vanillaDir, PatchInfo patchInfo, VersionRange? selectedRange, InstallerArgs args)
    {
        string gaPreview = Path.Combine(vanillaDir, "GameAssembly.dll");
        string mdPreview = Path.Combine(vanillaDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string ggmPreview = Path.Combine(vanillaDir, "SCPSL_Data", "globalgamemanagers");

        Console.WriteLine("[dry-run] Checking sources for steps 3-6:");
        DryRunCheckFile("  GameAssembly.dll", gaPreview);
        DryRunCheckFile("  global-metadata.dat", mdPreview);
        DryRunCheckFile("  globalgamemanagers", ggmPreview);

        if (selectedRange != null)
            Console.WriteLine($"[dry-run]   Selected range: {selectedRange.versionLabel} (metadata {selectedRange.metadataVersion}, {selectedRange.patchMethods.Length} methods)");
        else if (args.NoAnomaly)
            Console.WriteLine("[dry-run]   --no-anomaly: patching would be skipped.");
        else
            Console.WriteLine("[dry-run]   (no range resolved; would fail a real run)");

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
