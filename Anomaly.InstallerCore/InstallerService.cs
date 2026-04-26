using System.Diagnostics;
using System.Text;
using Anomaly.InstallerCore.IL2Cpp;
using Anomaly.InstallerCore.MelonLoader;
using Anomaly.InstallerCore.Patching;
using Anomaly.InstallerCore.Platform;
using Anomaly.InstallerCore.Resources;
using Anomaly.InstallerCore.Unpacking;
using Newtonsoft.Json;

namespace Anomaly.InstallerCore;

/// <summary>
/// Host-agnostic installer engine. Routes all user-facing output through the
/// <see cref="ConsoleUI"/> sink, which any host (CLI or Launcher) can swap via
/// <see cref="ConsoleUI.SetSink"/>.
/// </summary>
public static class InstallerService
{
    public const int TotalSteps = 8;
    private const string BundleVersionRelativePath = @"SCPSL_Data\globalgamemanagers";

    // Mirror of Anomaly.Client.Core.FirstRunArg. The final install step launches SCPSL.exe
    // with this arg so MelonLoader runs the Il2Cpp Assembly Generator — Anomaly.Client sees
    // the arg and Environment.Exits before doing real init. Installer-only; do not surface
    // through the Launcher's normal Launch flow.
    private const string FirstRunArg = "--anomaly-firstrun";

    private static readonly TimeSpan FirstRunTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs the full install flow end to end. Throws <see cref="InstallerException"/> for
    /// user-actionable failures; propagates other exceptions unchanged. On any failure after
    /// step 3 has started, mutated vanilla files are restored from their .bak sidecars.
    ///
    /// Cancellation is cooperative: checked between numbered steps and between files in the
    /// copy loop. Individual mutations (patch save, bundle-version write) run to completion
    /// once started; BackupManager covers the rollback if an exception escapes.
    /// </summary>
    public static void Install(InstallOptions options, CancellationToken ct = default)
    {
        var platform = new PlatformInfo();
        string installerDir = options.InstallerDir;
        ct.ThrowIfCancellationRequested();

        ConsoleUI.PrintInfo($"Platform: {(platform.IsWindows ? "Windows" : platform.IsLinux ? "Linux" : "Unknown")}");
        PrintPlatformSummary(platform);
        ConsoleUI.PrintInfo($"Install directory: {installerDir}");
        ConsoleUI.PrintInfo("");

        var resourcesClient = new AnomalyResourcesClient();
        RemoteManifest manifest = RemoteManifest.LoadAsync(resourcesClient, ct).GetAwaiter().GetResult();
        PatchInfo patchInfo = LoadPatchInfo(resourcesClient, manifest, ct);
        ConsoleUI.PrintInfo($"Patch config loaded ({patchInfo.versionRanges.Length} version range(s) defined)");
        if (options.SkipPatches)
            ConsoleUI.PrintInfo("  --skip-patches: IL2CPP patches and bundle-version fix will be skipped.");
        if (options.NoAnomaly)
            ConsoleUI.PrintInfo("  --no-anomaly: Anomaly-specific content (Mods/UserLibs check) will be skipped.");
        if (options.NoMelon)
            ConsoleUI.PrintInfo("  --no-melon: MelonLoader installation will be skipped.");
        ConsoleUI.PrintInfo("");

        string vanillaPath = GetVanillaPath(options, platform);
        string vanillaDir = Path.GetDirectoryName(vanillaPath)!;
        ConsoleUI.PrintInfo($"Vanilla game directory: {vanillaDir}");
        ConsoleUI.PrintInfo("");

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
                    "Installer is running inside the vanilla SCP:SL directory. Move the install target " +
                    "into a separate folder (this will become the modded game directory) and rerun.");

            ConsoleUI.PrintInfo("Same-path install: files were downloaded into this directory by the launcher.");
            ConsoleUI.PrintInfo("");
        }

        Preflight.Run(installerDir, vanillaDir, platform, options.DryRun);

        // Step 1: Copy game files
        ct.ThrowIfCancellationRequested();
        ConsoleUI.PrintStep(1, TotalSteps, "Copying game files...");
        if (!options.DryRun)
        {
            CopyGameFiles(vanillaDir, installerDir, ct);
            ConsoleUI.PrintSuccess("Game files copied.");
        }
        else
        {
            ConsoleUI.PrintInfo($"[dry-run] Would copy game files from {vanillaDir} to {installerDir}");
        }
        ConsoleUI.PrintInfo("");

        VersionRange? selectedRange = ResolveVersionRange(options, installerDir, patchInfo, out var resolvedGameVersion);

        // Final detected game version (X.Y.Z), captured for the post-install sidecar.
        // Set in step 5; remains null when patches are skipped or version detection fails.
        GameVersion? finalGameVersion = null;

        if (options.DryRun)
        {
            DryRunRemainingSteps(installerDir, vanillaDir, patchInfo, selectedRange, options);
            ConsoleUI.PrintInfo("");
            ConsoleUI.PrintInfo("[dry-run] Dry run complete. Re-run without --dry-run to install.");
            ConsoleUI.PrintInfo("");
            return;
        }

        // Step 2: Place clean (Themida-free) launcher
        ct.ThrowIfCancellationRequested();
        ConsoleUI.PrintStep(2, TotalSteps, "Placing clean launcher...");
        if (selectedRange != null)
        {
            CleanLauncherResolver.PlaceCleanLauncher(selectedRange, installerDir, resourcesClient, manifest, ct);
            ConsoleUI.PrintSuccess("Clean SCPSL.exe in place.");
        }
        else
        {
            ConsoleUI.PrintWarning("No VersionRange resolved — keeping the vanilla SCPSL.exe. The original exe may be Themida-protected and incompatible with mod loaders.");
        }
        ConsoleUI.PrintInfo("");

        string gameAssemblyPath = Path.Combine(installerDir, "GameAssembly.dll");
        string metadataPath = Path.Combine(installerDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string bundleVersionPath = Path.Combine(installerDir, "SCPSL_Data", "globalgamemanagers");

        if (!options.SkipPatches && selectedRange != null)
        {
            // Step 3: Unpack (if Themida-protected) and load IL2CPP
            ct.ThrowIfCancellationRequested();
            ConsoleUI.PrintStep(3, TotalSteps, "Loading IL2CPP...");

            BackupManager.BackupFile(gameAssemblyPath);
            BackupManager.BackupFile(metadataPath);
            BackupManager.BackupFile(bundleVersionPath);

            Il2CppManager il2CppManager;
            byte[] gameAssemblyData;
            try
            {
                Patcher.FixMetadataVersion(selectedRange.metadataVersion, installerDir);

                var (unpackResult, wasUnpacked) = LoadAndUnpackGameAssembly(
                    gameAssemblyPath, metadataPath, platform, installerDir, selectedRange.metadataVersion,
                    options.UnpackTimeoutMs, resourcesClient, manifest, ct);
                gameAssemblyData = unpackResult;

                il2CppManager = new Il2CppManager(gameAssemblyData, installerDir);
                il2CppManager.LoadIl2Cpp(metadataPath, selectedRange.metadataVersion, skipProtectionCheck: wasUnpacked);

                ConsoleUI.PrintInfo("  Dumping IL2CPP...");
                il2CppManager.DumpIl2Cpp();
                il2CppManager.GetScriptJson();
                il2CppManager.GetMethodsDictionary();
                ConsoleUI.PrintSuccess("IL2CPP loaded and parsed.");
                ConsoleUI.PrintInfo("");

                // Step 4: Patch GameAssembly
                ConsoleUI.PrintStep(4, TotalSteps, $"Applying patches for {selectedRange.versionLabel}...");
                var patcher = new Patcher(gameAssemblyPath, gameAssemblyData, il2CppManager, selectedRange);
                patcher.PatchGameAssembly();
                patcher.SaveGameAssembly();
                patcher.VerifySavedAssembly();
                ConsoleUI.PrintInfo("  Re-parsing patched GameAssembly to verify structure...");
                patcher.VerifyPatchedStructure(metadataPath, selectedRange.metadataVersion, installerDir);
                ConsoleUI.PrintSuccess("GameAssembly.dll patched.");
                ConsoleUI.PrintInfo("");

                // Step 5: Fix game version in Unity metadata
                ConsoleUI.PrintStep(5, TotalSteps, "Fixing game version...");
                var version = Patcher.ReadGameVersion(gameAssemblyData, il2CppManager);
                if (version.HasValue)
                {
                    var (major, minor, revision) = version.Value;
                    ConsoleUI.PrintInfo($"  Detected game version: {major}.{minor}.{revision}");
                    Patcher.FixBundleVersion(installerDir, major, minor, revision);
                    finalGameVersion = new GameVersion(major, minor, revision);
                    ConsoleUI.PrintSuccess("Game version fixed.");
                }
                else
                {
                    ConsoleUI.PrintWarning("Could not detect game version — bundle version unchanged.");
                }
                ConsoleUI.PrintInfo("");
            }
            catch
            {
                ConsoleUI.PrintError("Patching failed — restoring vanilla files from backup.");
                SafeRestore(gameAssemblyPath);
                SafeRestore(metadataPath);
                SafeRestore(bundleVersionPath);
                throw;
            }

            il2CppManager.RemoveDumpFolder();
        }
        else
        {
            var reason = options.SkipPatches ? "--skip-patches" : "no version range resolved";
            ConsoleUI.PrintStep(3, TotalSteps, $"Skipping IL2CPP load ({reason}).");
            ConsoleUI.PrintStep(4, TotalSteps, $"Skipping patches ({reason}).");
            ConsoleUI.PrintStep(5, TotalSteps, $"Skipping bundle-version fix ({reason}).");
            ConsoleUI.PrintInfo("");
        }

        WriteUnityBuiltinDataMarkerIfNeeded(installerDir, options, finalGameVersion ?? resolvedGameVersion);

        // Step 6: Install MelonLoader
        ct.ThrowIfCancellationRequested();
        if (!options.NoMelon)
        {
            ConsoleUI.PrintStep(6, TotalSteps, $"Installing MelonLoader {MelonLoaderInstaller.PinnedVersion()}...");
            MelonLoaderInstaller.Install(installerDir);
            ConsoleUI.PrintSuccess("MelonLoader installed.");
        }
        else
        {
            ConsoleUI.PrintStep(6, TotalSteps, "Skipping MelonLoader (--no-melon).");
        }
        ConsoleUI.PrintInfo("");

        // Step 7: Verify mod files
        ct.ThrowIfCancellationRequested();
        if (!options.NoAnomaly)
        {
            ConsoleUI.PrintStep(7, TotalSteps, "Verifying mod files...");
            VerifyModFiles(installerDir);
        }
        else
        {
            ConsoleUI.PrintStep(7, TotalSteps, "Skipping mod-file check (--no-anomaly).");
        }
        ConsoleUI.PrintInfo("");

        // Step 8: Trigger first-run so MelonLoader dumps Il2Cpp managed assemblies.
        // Skipped when MelonLoader isn't installed (--no-melon) or when patches are off
        // (--skip-patches), since neither path can produce a clean boot to a scene.
        ct.ThrowIfCancellationRequested();
        bool firstRunGenerated = false;
        if (options.NoMelon)
        {
            ConsoleUI.PrintStep(8, TotalSteps, "Skipping first-run assembly dump (--no-melon).");
        }
        else if (options.SkipPatches)
        {
            ConsoleUI.PrintStep(8, TotalSteps, "Skipping first-run assembly dump (--skip-patches).");
        }
        else
        {
            ConsoleUI.PrintStep(8, TotalSteps, "Generating Il2Cpp managed assemblies (first-run)...");
            firstRunGenerated = TriggerFirstRun(installerDir, ct);
        }
        ConsoleUI.PrintInfo("");

        WriteInstallStateSidecar(installerDir, options, finalGameVersion);

        ConsoleUI.PrintInfo("");
        ConsoleUI.PrintSuccess("========================================");
        ConsoleUI.PrintSuccess("  Installation complete!");
        ConsoleUI.PrintSuccess("========================================");
        ConsoleUI.PrintInfo("");
        PrintPostInstallInstructions(platform, installerDir, firstRunGenerated);
        ConsoleUI.PrintInfo("");
    }

    private static void WriteInstallStateSidecar(string installerDir, InstallOptions options, GameVersion? gameVersion)
    {
        string? buildId = TryReadBuildIdFromAnomalySource(installerDir);
        var state = new InstallState(
            SchemaVersion: InstallStateSidecar.CurrentSchemaVersion,
            Source: options.SourceKind ?? "unknown",
            ManifestId: options.SourceManifestId,
            BuildId: buildId,
            GameVersion: gameVersion?.ToString(),
            InstalledAt: DateTime.UtcNow.ToString("O"));

        try
        {
            InstallStateSidecar.Write(installerDir, state);
            ConsoleUI.PrintInfo($"Wrote install-state sidecar ({InstallStateSidecar.FileName}).");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Could not write {InstallStateSidecar.FileName}: {ex.Message}");
        }
    }

    private static string? TryReadBuildIdFromAnomalySource(string installerDir)
    {
        var path = Path.Combine(installerDir, ".anomaly_source.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var json = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(path));
            var buildId = (string?)json?["buildId"];
            return string.IsNullOrEmpty(buildId) ? null : buildId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launcher-side teardown: removes game files (SCPSL.exe, GameAssembly.dll, SCPSL_Data,
    /// MelonLoader runtime + Il2Cpp assemblies) but PRESERVES Mods/, UserLibs/, and
    /// MelonLoader/UserData/ so the user's mod setup and configs survive across reinstalls.
    /// Idempotent and best-effort: locked files are skipped, others continue.
    /// </summary>
    public static int RemoveGameFiles(string installerDir)
    {
        ConsoleUI.PrintInfo($"Removing game files from: {installerDir}");
        ConsoleUI.PrintInfo("");

        int failures = 0;

        string[] filesToDelete = {
            Path.Combine(installerDir, "SCPSL.exe"),
            Path.Combine(installerDir, "GameAssembly.dll"),
            Path.Combine(installerDir, "UnityCrashHandler64.exe"),
            Path.Combine(installerDir, "UnityPlayer.dll"),
            Path.Combine(installerDir, "baselib.dll"),
            Path.Combine(installerDir, "MelonLoader", "version.dll"),
            Path.Combine(installerDir, ".anomaly_source.json"),
            InstallStateSidecar.PathFor(installerDir),
        };

        string[] dirsToDelete = {
            Path.Combine(installerDir, "SCPSL_Data"),
            Path.Combine(installerDir, "MelonLoader", "Dependencies"),
            Path.Combine(installerDir, "MelonLoader", "net6"),
            Path.Combine(installerDir, "MelonLoader", "Documentation"),
            Path.Combine(installerDir, "MelonLoader", "Il2CppAssemblies"),
            Path.Combine(installerDir, "il2cppdumper"),
        };

        foreach (var f in filesToDelete)
            failures += DeleteFileBestEffort(f);

        foreach (var d in dirsToDelete)
            failures += DeleteDirectoryBestEffort(d);

        // Restore .bak files in case the user later wants to layer a fresh install on top of
        // the same source. Then sweep stale .bak files left behind.
        foreach (var bak in EnumerateBackups(installerDir))
            failures += DeleteFileBestEffort(bak);

        ConsoleUI.PrintInfo("");
        if (failures == 0)
            ConsoleUI.PrintSuccess("Game files removed. Mods/, UserLibs/, and MelonLoader/UserData/ kept.");
        else
            ConsoleUI.PrintWarning($"Removed game files with {failures} warning(s). See above.");
        return failures == 0 ? 0 : 1;
    }

    private static int DeleteFileBestEffort(string path)
    {
        if (!File.Exists(path))
            return 0;
        try
        {
            File.Delete(path);
            ConsoleUI.PrintInfo($"  Deleted {path}");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"  Could not delete {path}: {ex.Message}");
            return 1;
        }
    }

    private static int DeleteDirectoryBestEffort(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        try
        {
            Directory.Delete(path, recursive: true);
            ConsoleUI.PrintInfo($"  Deleted {path}");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"  Could not delete {path}: {ex.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> EnumerateBackups(string installerDir)
    {
        if (!Directory.Exists(installerDir))
            return Array.Empty<string>();
        try
        {
            return Directory.GetFiles(installerDir, "*.bak", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reverses a prior install: restores every vanilla .bak, removes MelonLoader/Mods/UserLibs,
    /// and clears the vanilla-path pointer. Safe to run against a partially-installed directory.
    /// </summary>
    public static int Uninstall(string installerDir) => Uninstaller.Run(installerDir);

    // ========================================================================
    // Helpers (extracted from the former Program.cs Run flow)
    // ========================================================================

    private static bool PathsAreSame(string a, string b)
    {
        try
        {
            string normA = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            ConsoleUI.PrintInfo("Linux environment: SteamOS detected");
        else if (runtime.IsSteamDeckHardware)
            ConsoleUI.PrintInfo("Linux environment: Steam Deck hardware detected");

        if (runtime.HasProton)
            ConsoleUI.PrintInfo($"Preferred Linux runtime: Proton ({runtime.PreferredProton!.Name})");
        else if (runtime.HasWine)
            ConsoleUI.PrintInfo($"Preferred Linux runtime: Wine ({runtime.WineCommand})");
        else
            ConsoleUI.PrintInfo("Preferred Linux runtime: none detected yet");
    }

    private static void PrintPostInstallInstructions(PlatformInfo platform, string installerDir, bool firstRunGenerated)
    {
        string moddedExe = Path.Combine(installerDir, "SCPSL.exe");

        if (platform.IsLinux)
        {
            // Linux still requires the Steam-shortcut + Proton dance regardless of whether
            // first-run completed: MelonLoader needs to be primed in the Proton prefix.
            ConsoleUI.PrintInfo("Linux next steps:");
            ConsoleUI.PrintInfo($"  1. Add this modded build to Steam as a non-Steam game: {moddedExe}");
            ConsoleUI.PrintInfo("  2. In the shortcut properties, force a Steam Play compatibility tool (Proton).");
            ConsoleUI.PrintInfo("  3. Set Steam launch options to: WINEDLLOVERRIDES=\"version=n,b\" %command%");
            ConsoleUI.PrintInfo("  4. Launch it once through Steam to let MelonLoader generate its files.");
            ConsoleUI.PrintInfo("  5. If MelonLoader still does not start, install the Windows .NET 6 Desktop Runtime into the Proton prefix used by that shortcut.");
            ConsoleUI.PrintInfo("  6. The first launch will take longer than usual.");
            return;
        }

        if (firstRunGenerated)
        {
            ConsoleUI.PrintInfo("Ready to launch — IL2CPP assemblies are already generated.");
            return;
        }

        ConsoleUI.PrintInfo("Please launch SCPSL.exe once to generate IL2CPP assemblies.");
        ConsoleUI.PrintInfo("The first launch will take longer than usual.");
    }

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

    private static string GetVanillaPath(InstallOptions options, PlatformInfo platform)
    {
        if (!string.IsNullOrEmpty(options.VanillaPath))
        {
            string provided = options.VanillaPath;
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

        string? detected = GameDetector.TryFindVanilla(platform);
        if (detected != null)
        {
            ConsoleUI.PrintInfo($"Auto-detected vanilla SCPSL.exe at: {detected}");
            if (ConsoleUI.PromptYesNo("Use this path?"))
                return detected;
        }

        ConsoleUI.PrintInfo("Please enter the path to your vanilla SCPSL.exe:");
        ConsoleUI.PrintInfo("(e.g., C:\\Program Files\\Steam\\steamapps\\common\\SCP Secret Laboratory\\SCPSL.exe)");
        string path = ConsoleUI.PromptPath("Path to SCPSL.exe");

        if (!File.Exists(path))
            throw new InstallerException($"File not found: {path}");
        if (!path.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
            throw new InstallerException("Please select the SCPSL.exe file.");

        return path;
    }

    private static void CopyGameFiles(string sourceDir, string targetDir, CancellationToken ct)
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

        ConsoleUI.PrintInfo($"  {totalFiles} files ({ConsoleUI.FormatBytes(totalBytes)})");

        long copiedBytes = 0;
        int copiedFiles = 0;
        CopyFilesRecursively(sourceInfo, new DirectoryInfo(targetDir), skipFiles,
            ref copiedFiles, totalFiles, ref copiedBytes, totalBytes, ct);
    }

    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target, HashSet<string> skipFiles,
        ref int copiedFiles, int totalFiles, ref long copiedBytes, long totalBytes, CancellationToken ct)
    {
        foreach (DirectoryInfo dir in source.GetDirectories())
        {
            string targetSubDir = Path.Combine(target.FullName, dir.Name);
            Directory.CreateDirectory(targetSubDir);
            CopyFilesRecursively(dir, new DirectoryInfo(targetSubDir), skipFiles,
                ref copiedFiles, totalFiles, ref copiedBytes, totalBytes, ct);
        }

        foreach (FileInfo file in source.GetFiles())
        {
            ct.ThrowIfCancellationRequested();
            if (skipFiles.Contains(file.Name))
                continue;

            string destPath = Path.Combine(target.FullName, file.Name);
            var destFile = new FileInfo(destPath);

            bool destExists = destFile.Exists;

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

    private static (byte[] data, bool wasUnpacked) LoadAndUnpackGameAssembly(
        string gameAssemblyPath,
        string metadataPath,
        PlatformInfo platform,
        string workingDir,
        int metadataVersion,
        int? unpackTimeoutMs,
        AnomalyResourcesClient resourcesClient,
        RemoteManifest manifest,
        CancellationToken ct)
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
            ConsoleUI.PrintInfo("  Themida protection detected. Resolving unpacker...");

            // unlicense.exe lives in the AnomalyLabs resource repo and is downloaded on demand.
            // The cache (%APPDATA%/Anomaly/cache/patching/unlicense.exe) means we only pay the
            // 49 MB transfer once, even across multiple installs.
            string unlicensePath = UnlicenseResolver.GetOrDownload(resourcesClient, manifest, ct);

            Unpacker unpacker = unpackTimeoutMs is int ms
                ? new Unpacker(unlicensePath, gameAssemblyPath, platform, ms)
                : new Unpacker(unlicensePath, gameAssemblyPath, platform);
            unpacker.UnpackGameAssembly();

            return (Patcher.GetGameAssemblyData(gameAssemblyPath), wasUnpacked: true);
        }
    }

    private const string PatchInfoRelativePath = "patching/patchinfo.json";

    /// <summary>
    /// Loads the active <see cref="PatchInfo"/>. Tries the live copy from anomaly-resources
    /// first (so we can ship patch hotfixes without releasing a new launcher); on any failure
    /// — no network, hash mismatch, malformed JSON — falls back to the embedded copy.
    /// </summary>
    private static PatchInfo LoadPatchInfo(AnomalyResourcesClient client, RemoteManifest manifest, CancellationToken ct)
    {
        string? expectedSha = manifest.GetSha256(PatchInfoRelativePath);

        try
        {
            string livePath = client.GetOrDownloadAsync(
                    PatchInfoRelativePath,
                    expectedSha,
                    timeout: TimeSpan.FromSeconds(10),
                    progressLabel: null,
                    ct)
                .GetAwaiter().GetResult();

            string liveJson = File.ReadAllText(livePath);
            var live = JsonConvert.DeserializeObject<PatchInfo>(liveJson);
            if (live != null && live.versionRanges.Length > 0)
            {
                ConsoleUI.PrintInfo("  Patch config: using live copy from anomaly-resources.");
                return live;
            }

            ConsoleUI.PrintWarning("Live patchinfo.json was empty. Falling back to embedded copy.");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning(
                $"Could not fetch live patchinfo.json ({ex.Message}). " +
                "Falling back to the patch set this launcher was built with.");
        }

        string json = EmbeddedResources.ReadText("patchinfo.json");
        PatchInfo? patchInfo = JsonConvert.DeserializeObject<PatchInfo>(json);
        if (patchInfo == null || patchInfo.versionRanges.Length == 0)
            throw new InstallerException("Failed to load embedded patchinfo.json or no version ranges defined.");
        return patchInfo;
    }

    private static VersionRange? ResolveVersionRange(InstallOptions options, string installerDir, PatchInfo patchInfo, out GameVersion? detected)
    {
        detected = null;

        if (!string.IsNullOrWhiteSpace(options.TargetVersion))
        {
            if (!GameVersion.TryParse(options.TargetVersion, out var v))
                throw new InstallerException($"Invalid target-version value: '{options.TargetVersion}' (expected X.Y.Z).");
            detected = v;
            ConsoleUI.PrintInfo($"Target version (from --target-version): {v}");
        }
        else
        {
            string bundleVersionPath = Path.Combine(installerDir, BundleVersionRelativePath);
            detected = Patcher.TryReadBundleVersionString(bundleVersionPath);
            if (detected.HasValue)
                ConsoleUI.PrintInfo($"Detected game version (from globalgamemanagers): {detected.Value}");
            else
                ConsoleUI.PrintInfo("Could not detect game version from globalgamemanagers.");
        }

        if (!detected.HasValue)
        {
            if (options.SkipPatches)
            {
                ConsoleUI.PrintWarning("Version unknown and --skip-patches set. Skipping clean-launcher placement too.");
                return null;
            }
            throw new InstallerException(
                "Could not detect the game version. Pass --target-version X.Y.Z, or re-run with " +
                "--skip-patches for an unsupported build.");
        }

        var detectedVersion = detected.Value;
        var range = patchInfo.versionRanges.FirstOrDefault(r => r.Contains(detectedVersion));
        if (range == null)
        {
            var known = string.Join(", ", patchInfo.versionRanges.Select(r => r.versionLabel));
            if (options.SkipPatches)
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

        if (options.SkipPatches)
            ConsoleUI.PrintInfo($"Matched range: {range.versionLabel} (Unity {range.unityVersion}) — using its clean launcher, skipping patches.");
        else
            ConsoleUI.PrintInfo($"Selected patch range: {range.versionLabel} (metadata {range.metadataVersion}, {range.patchMethods.Length} patches)");
        ConsoleUI.PrintInfo("");
        return range;
    }

    private static void WriteUnityBuiltinDataMarkerIfNeeded(string installerDir, InstallOptions options, GameVersion? gameVersion)
    {
        if (!options.SourceSteamId.HasValue || options.SourceSteamId.Value == 0 || !gameVersion.HasValue)
            return;

        if (gameVersion.Value == GameVersion.Max)
            return;

        try
        {
            string markerPath = Path.Combine(installerDir, "SCPSL_Data", "Resources", "unity_builtin_data");
            string plain = $"{gameVersion.Value};{options.SourceSteamId.Value};-";
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            string encoded = InsertDashEveryFiveChars(base64);

            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllBytes(markerPath, Encoding.UTF8.GetBytes(encoded));
            ConsoleUI.PrintInfo("Wrote Steam Unity bootstrap marker (unity_builtin_data).");
            ConsoleUI.PrintInfo("");
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Could not write unity_builtin_data marker: {ex.Message}");
            ConsoleUI.PrintInfo("");
        }
    }

    private static string InsertDashEveryFiveChars(string value)
    {
        var sb = new StringBuilder(value.Length + value.Length / 5);
        for (int i = 0; i < value.Length; i++)
        {
            sb.Append(value[i]);
            if ((i + 1) % 5 == 0 && i + 1 < value.Length)
                sb.Append('-');
        }

        return sb.ToString();
    }

    private static void DryRunRemainingSteps(string installerDir, string vanillaDir, PatchInfo patchInfo, VersionRange? selectedRange, InstallOptions options)
    {
        string gaPreview = Path.Combine(vanillaDir, "GameAssembly.dll");
        string mdPreview = Path.Combine(vanillaDir, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        string ggmPreview = Path.Combine(vanillaDir, "SCPSL_Data", "globalgamemanagers");

        ConsoleUI.PrintInfo("[dry-run] Checking sources for steps 3-6:");
        DryRunCheckFile("  GameAssembly.dll", gaPreview);
        DryRunCheckFile("  global-metadata.dat", mdPreview);
        DryRunCheckFile("  globalgamemanagers", ggmPreview);

        if (selectedRange != null)
            ConsoleUI.PrintInfo($"[dry-run]   Selected range: {selectedRange.versionLabel} (metadata {selectedRange.metadataVersion}, {selectedRange.patchMethods.Length} methods)");
        else if (options.NoAnomaly)
            ConsoleUI.PrintInfo("[dry-run]   --no-anomaly: patching would be skipped.");
        else
            ConsoleUI.PrintInfo("[dry-run]   (no range resolved; would fail a real run)");

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

        ConsoleUI.PrintInfo($"[dry-run]   MelonLoader: v{mlConfig.version} from {mlConfig.downloadUrl}");
        if (string.IsNullOrWhiteSpace(mlConfig.sha256))
            ConsoleUI.PrintWarning("[dry-run] No pinned SHA-256 for the MelonLoader release.");
        else
            ConsoleUI.PrintInfo($"[dry-run]   SHA-256: {mlConfig.sha256}");

        ConsoleUI.PrintInfo("");
        ConsoleUI.PrintInfo("[dry-run] Steps that WOULD run on a real install:");
        ConsoleUI.PrintInfo($"[dry-run]   3. Load IL2CPP from {Path.Combine(installerDir, "GameAssembly.dll")}");
        ConsoleUI.PrintInfo($"[dry-run]   4. Backup + patch GameAssembly.dll, global-metadata.dat, globalgamemanagers");
        ConsoleUI.PrintInfo($"[dry-run]   5. Fix bundle version in globalgamemanagers");
        ConsoleUI.PrintInfo($"[dry-run]   6. Download MelonLoader {mlConfig.version} into {installerDir}");
        ConsoleUI.PrintInfo($"[dry-run]   7. Verify Mods/ and UserLibs/");
        ConsoleUI.PrintInfo($"[dry-run]   8. Launch SCPSL.exe {FirstRunArg} to dump Il2Cpp assemblies (skipped if --no-melon, --skip-patches, or Mods/Anomaly.Client.dll is missing)");
    }

    private static void DryRunCheckFile(string label, string path)
    {
        if (File.Exists(path))
        {
            long size = new FileInfo(path).Length;
            ConsoleUI.PrintInfo($"[dry-run] {label}: OK ({ConsoleUI.FormatBytes(size)}) at {path}");
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
            ConsoleUI.PrintInfo($"  Mods directory: {mods.Length} DLL(s) found");
            foreach (string mod in mods)
                ConsoleUI.PrintInfo($"    - {Path.GetFileName(mod)}");
        }
        else
        {
            ConsoleUI.PrintWarning("Mods directory not found. No mods will be loaded.");
        }

        if (Directory.Exists(userLibsDir))
        {
            string[] libs = Directory.GetFiles(userLibsDir, "*.dll");
            ConsoleUI.PrintInfo($"  UserLibs directory: {libs.Length} DLL(s) found");
            foreach (string lib in libs)
                ConsoleUI.PrintInfo($"    - {Path.GetFileName(lib)}");
        }
        else
        {
            ConsoleUI.PrintWarning("UserLibs directory not found.");
        }
    }

    /// <summary>
    /// Launches the modded SCPSL.exe with <c>--anomaly-firstrun</c> so MelonLoader's Il2Cpp
    /// Assembly Generator runs and writes the managed assemblies to disk. Anomaly.Client
    /// detects the arg in OnInitializeMelon and Environment.Exits before doing real init,
    /// which is the exit signal we wait on. Skipped (with a warning) when Anomaly.Client.dll
    /// isn't in Mods/ — without it the game would boot to the menu and the installer would
    /// hang until the timeout. Times out at <see cref="FirstRunTimeout"/>.
    /// </summary>
    /// <summary>
    /// Returns true when the game process exited cleanly (code 0) and assemblies are presumed
    /// generated. False on any skip/failure/timeout path — callers use this to decide whether
    /// to instruct the user to launch the game manually.
    /// </summary>
    private static bool TriggerFirstRun(string installerDir, CancellationToken ct)
    {
        string clientDll = Path.Combine(installerDir, "Mods", "Anomaly.Client.dll");
        if (!File.Exists(clientDll))
        {
            ConsoleUI.PrintWarning(
                "  Anomaly.Client.dll not found in Mods/ — skipping first-run. " +
                "MelonLoader will dump assemblies on the user's first manual launch instead.");
            return false;
        }

        string moddedExe = Path.Combine(installerDir, "SCPSL.exe");
        if (!File.Exists(moddedExe))
        {
            ConsoleUI.PrintWarning("  SCPSL.exe missing — cannot trigger first-run.");
            return false;
        }

        ConsoleUI.PrintInfo($"  Launching SCPSL.exe {FirstRunArg} (game exits automatically once dumped)");
        ConsoleUI.PrintInfo($"  Typically 30-60 seconds; timeout {FirstRunTimeout.TotalMinutes:0} min.");

        var psi = new ProcessStartInfo
        {
            FileName = moddedExe,
            Arguments = FirstRunArg,
            WorkingDirectory = installerDir,
            UseShellExecute = false,
        };

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"  Failed to launch SCPSL.exe: {ex.Message}");
            return false;
        }

        if (proc == null)
        {
            ConsoleUI.PrintWarning("  Could not start SCPSL.exe — assemblies will dump on the user's next launch.");
            return false;
        }

        using (proc)
        {
            DateTime startedAt = DateTime.UtcNow;
            DateTime deadline = startedAt + FirstRunTimeout;
            DateTime lastTick = startedAt;

            while (!proc.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    TryKill(proc);
                    ct.ThrowIfCancellationRequested();
                }

                if (DateTime.UtcNow > deadline)
                {
                    ConsoleUI.PrintWarning(
                        $"  First-run did not exit within {FirstRunTimeout.TotalMinutes:0} min — terminating. " +
                        "Generation may have failed; check MelonLoader/Latest.log.");
                    TryKill(proc);
                    return false;
                }

                if ((DateTime.UtcNow - lastTick).TotalSeconds >= 30)
                {
                    int elapsed = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                    ConsoleUI.PrintInfo($"  ...still running ({elapsed}s elapsed)");
                    lastTick = DateTime.UtcNow;
                }

                Thread.Sleep(500);
            }

            int totalSec = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
            if (proc.ExitCode == 0)
            {
                ConsoleUI.PrintSuccess($"First-run complete in {totalSec}s.");
                return true;
            }

            ConsoleUI.PrintWarning($"First-run exited with code {proc.ExitCode} after {totalSec}s — verify MelonLoader/Il2CppAssemblies/.");
            return false;
        }
    }

    private static void TryKill(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have already exited or be inaccessible — best-effort.
        }
    }
}
