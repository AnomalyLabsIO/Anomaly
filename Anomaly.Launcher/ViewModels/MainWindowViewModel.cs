using System.Collections.ObjectModel;
using System.Diagnostics;
using Anomaly.InstallerCore;
using Anomaly.Launcher.Models;
using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Install;
using Anomaly.Launcher.Services.Localization;
using Anomaly.Launcher.Services.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public enum LauncherTab
{
    Installed,
    PublicRepo,
    GameVersions,
    Settings,
}

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly PathService _paths;
    private readonly UserProfileService _userProfile;
    private readonly ModManagerService _modManager;
    private readonly GameProcessService _gameProcess;
    private readonly CrashReportService _crashReport;
    private readonly VersionCheckService _versionCheck;
    private readonly SystemInfoService _systemInfo;
    private readonly SteamDownloadService _steamDownload;
    private readonly BuildCatalogService _buildCatalog;
    private readonly SynchronizationContext? _syncContext;

    private FileSystemWatcher? _acfWatcher;
    private FileSystemWatcher? _gaWatcher;
    private DateTime _lastWatcherEvent = DateTime.MinValue;

    // --- Active tab ---

    [ObservableProperty]
    private LauncherTab _activeTab = LauncherTab.Installed;

    public bool IsInstalledTab => ActiveTab == LauncherTab.Installed;
    public bool IsPublicRepoTab => ActiveTab == LauncherTab.PublicRepo;
    public bool IsGameVersionsTab => ActiveTab == LauncherTab.GameVersions;
    public bool IsSettingsTab => ActiveTab == LauncherTab.Settings;

    public bool InstalledTabActive => IsInstalledTab;
    public bool PublicRepoTabActive => IsPublicRepoTab;
    public bool GameVersionsTabActive => IsGameVersionsTab;
    public bool SettingsTabActive => IsSettingsTab;

    partial void OnActiveTabChanged(LauncherTab value)
    {
        OnPropertyChanged(nameof(IsInstalledTab));
        OnPropertyChanged(nameof(IsPublicRepoTab));
        OnPropertyChanged(nameof(IsGameVersionsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(InstalledTabActive));
        OnPropertyChanged(nameof(PublicRepoTabActive));
        OnPropertyChanged(nameof(GameVersionsTabActive));
        OnPropertyChanged(nameof(SettingsTabActive));
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab switch
        {
            "installed" => LauncherTab.Installed,
            "public" => LauncherTab.PublicRepo,
            "versions" => LauncherTab.GameVersions,
            "settings" => LauncherTab.Settings,
            _ => ActiveTab,
        };
    }

    // --- Launcher state ---

    [ObservableProperty]
    private LauncherState _currentState = LauncherState.GameNotInstalled;

    // Computed from CurrentState — fire in OnCurrentStateChanged
    public bool IsGameNotInstalled => CurrentState == LauncherState.GameNotInstalled;
    public bool IsVanillaUpdated => CurrentState == LauncherState.VanillaUpdated;
    public bool IsOnlineBuildMismatch => CurrentState == LauncherState.OnlineBuildMismatch;
    public bool IsAllGood => CurrentState == LauncherState.AllGood;
    public bool IsGameInstalled => CurrentState != LauncherState.GameNotInstalled;

    [ObservableProperty]
    private bool _isCheckingState;

    // --- Steam build info ---

    [ObservableProperty]
    private string? _branch;

    [ObservableProperty]
    private string? _localBuildId;

    [ObservableProperty]
    private string? _onlineBuildId;

    // --- System info ---

    [ObservableProperty]
    private string? _melonLoaderVersion;

    [ObservableProperty]
    private string? _vanillaGaDate;

    [ObservableProperty]
    private string? _moddedGaDate;

    // --- Username ---

    [ObservableProperty]
    private string _username = "Unknown";

    [ObservableProperty]
    private bool _isEditingUsername;

    [ObservableProperty]
    private string _editingUsernameText = string.Empty;

    // --- Mod list ---

    [ObservableProperty]
    private ObservableCollection<ModItemViewModel> _mods = new();

    [ObservableProperty]
    private ModItemViewModel? _selectedMod;

    public int EnabledModCount => Mods.Count(m => m.IsActive);

    public string ModCountHeader => $"{Mods.Count} mods · {EnabledModCount} active";

    partial void OnSelectedModChanged(ModItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedMod));
        OnPropertyChanged(nameof(NoSelectedMod));
    }

    public bool HasSelectedMod => SelectedMod != null;
    public bool NoSelectedMod => SelectedMod == null;

    // --- Public repository (stub catalogue) ---

    [ObservableProperty]
    private ObservableCollection<RepoModItemViewModel> _repoMods = new();

    [ObservableProperty]
    private ObservableCollection<RepoModItemViewModel> _filteredRepoMods = new();

    [ObservableProperty]
    private RepoModItemViewModel? _selectedRepoMod;

    [ObservableProperty]
    private string _repoSearchQuery = string.Empty;

    partial void OnRepoSearchQueryChanged(string value) => RefreshRepoFilter();

    private void RefreshRepoFilter()
    {
        var query = RepoSearchQuery?.Trim() ?? string.Empty;
        FilteredRepoMods.Clear();
        foreach (var m in RepoMods)
        {
            if (string.IsNullOrEmpty(query)
                || m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRepoMods.Add(m);
            }
        }
        if (SelectedRepoMod == null || !FilteredRepoMods.Contains(SelectedRepoMod))
            SelectedRepoMod = FilteredRepoMods.FirstOrDefault();
    }

    partial void OnSelectedRepoModChanged(RepoModItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedRepoMod));
    }

    public bool HasSelectedRepoMod => SelectedRepoMod != null;

    // --- Game versions ---

    [ObservableProperty]
    private ObservableCollection<GameVersionItemViewModel> _gameVersions = new();

    [ObservableProperty]
    private GameVersionItemViewModel? _selectedGameVersion;

    [ObservableProperty]
    private bool _isLoadingGameVersions;

    partial void OnSelectedGameVersionChanged(GameVersionItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedGameVersion));
    }

    public bool HasSelectedGameVersion => SelectedGameVersion != null;

    // --- Launch args ---

    [ObservableProperty]
    private string _launchArgs = string.Empty;

    // --- Game state ---

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private string _gameStatusText = Tr.Get(TrKeys.Status_Ready);

    // --- State 1: manual path entry ---

    [ObservableProperty]
    private string _manualVanillaPath = string.Empty;

    // --- Crash dialog ---

    [ObservableProperty]
    private bool _showCrashDialog;

    [ObservableProperty]
    private CrashReportDialogViewModel? _crashDialogViewModel;

    // --- Steam download dialog ---

    [ObservableProperty]
    private bool _showSteamDownloadDialog;

    [ObservableProperty]
    private SteamDownloadDialogViewModel? _steamDownloadDialogViewModel;

    // --- Install progress dialog ---

    [ObservableProperty]
    private bool _showInstallProgressDialog;

    [ObservableProperty]
    private InstallProgressDialogViewModel? _installProgressDialogViewModel;

    // --- Sync state (for titlebar) ---

    [ObservableProperty]
    private string _syncStatusText = "SYNCED";

    [ObservableProperty]
    private bool _isBuildNominal = true;

    // --- Computed ---

    public bool CanLaunch => !IsGameRunning && IsGameInstalled;

    public string GameVersionLabel => IsGameInstalled
        ? (GameVersionFromBuild() ?? LocalBuildId ?? "—")
        : "—";

    private string? GameVersionFromBuild()
    {
        if (LocalBuildId == null) return null;
        return GameVersions.FirstOrDefault(v => v.BuildId == LocalBuildId)?.GameVersion;
    }

    partial void OnCurrentStateChanged(LauncherState value)
    {
        OnPropertyChanged(nameof(IsGameNotInstalled));
        OnPropertyChanged(nameof(IsVanillaUpdated));
        OnPropertyChanged(nameof(IsOnlineBuildMismatch));
        OnPropertyChanged(nameof(IsAllGood));
        OnPropertyChanged(nameof(IsGameInstalled));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(GameVersionLabel));
        IsBuildNominal = value == LauncherState.AllGood;
    }

    partial void OnLocalBuildIdChanged(string? value)
    {
        OnPropertyChanged(nameof(GameVersionLabel));
        UpdateInstalledFlag();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLaunch));
    }

    // ===================== Constructor =====================

    public MainWindowViewModel(
        PathService paths,
        UserProfileService userProfile,
        ModManagerService modManager,
        GameProcessService gameProcess,
        CrashReportService crashReport,
        VersionCheckService versionCheck,
        SystemInfoService systemInfo,
        SteamDownloadService steamDownload,
        BuildCatalogService buildCatalog)
    {
        _paths = paths;
        _userProfile = userProfile;
        _modManager = modManager;
        _gameProcess = gameProcess;
        _crashReport = crashReport;
        _versionCheck = versionCheck;
        _systemInfo = systemInfo;
        _steamDownload = steamDownload;
        _buildCatalog = buildCatalog;
        _syncContext = SynchronizationContext.Current;

        Username = userProfile.GetUsername();
        LaunchArgs = LoadLaunchArgs();
        LoadMods();
        LoadStaticInfo();
        LoadGameVersions();
        LoadSampleRepoMods();

        _gameProcess.GameExited += OnGameExited;

        _ = RefreshStateAsync();
    }

    // ===================== Game Versions =====================

    public void LoadGameVersions()
    {
        IsLoadingGameVersions = true;
        try
        {
            var entries = _buildCatalog.Merge(Array.Empty<SteamBuildCatalogEntry>(), includeAll: false);
            GameVersions.Clear();
            foreach (var entry in entries)
                GameVersions.Add(new GameVersionItemViewModel(entry, isInstalled: false));
            UpdateInstalledFlag();
            if (SelectedGameVersion == null || !GameVersions.Contains(SelectedGameVersion))
                SelectedGameVersion = GameVersions.FirstOrDefault(v => v.IsInstalled) ?? GameVersions.FirstOrDefault();
        }
        finally
        {
            IsLoadingGameVersions = false;
        }
    }

    private void UpdateInstalledFlag()
    {
        foreach (var v in GameVersions)
            v.IsInstalled = !string.IsNullOrEmpty(LocalBuildId) && v.BuildId == LocalBuildId;
        OnPropertyChanged(nameof(GameVersionLabel));
    }

    [RelayCommand]
    private void InstallGameVersion(GameVersionItemViewModel? entry)
    {
        entry ??= SelectedGameVersion;
        if (entry == null) return;

        // Delegate into existing Steam download dialog flow; the dialog already handles
        // authentication + download + installer handoff.
        OpenSteamDownloadDialog();

        // Seed selection if the dialog VM is ready
        if (SteamDownloadDialogViewModel is { } dlg)
        {
            var match = dlg.AvailableBuilds.FirstOrDefault(b => b.ManifestId == entry.ManifestId);
            if (match != null)
                dlg.SelectedBuild = match;
        }
    }

    // ===================== Public Repository (stub) =====================

    private void LoadSampleRepoMods()
    {
        // Stub data matching the design mock. Will be replaced by a real
        // repo.anomaly.scp/v1 catalogue client.
        var items = new[]
        {
            new RepoModItemViewModel("gamemode.hideandseek", "Hide & Seek", "gamemode", "silverfox",
                "2.1.0", "14.2.6", 12483, true,
                "Asymmetric 1-vs-many round variant with custom win conditions.",
                "3d ago", false),
            new RepoModItemViewModel("gamemode.speedrun", "Speedrun Mode", "gamemode", "anomaly-team",
                "0.5.2", "14.2.6", 3214, true,
                "Timed escape objectives with per-route leaderboards.",
                "1w ago", false),
            new RepoModItemViewModel("cosmetic.helmets", "Helmet Skins Pack", "cosmetic", "community",
                "1.4.0", "14.2.x", 8012, false,
                "16 new helmet cosmetics for MTF and Chaos Insurgency.",
                "2d ago", false),
            new RepoModItemViewModel("utility.roundreports", "RoundReports", "utility", "TayTayModded",
                "2.3.0", "14.2.x", 932, false,
                "Per-round statistics export to JSON / CSV.",
                "5h ago", false),
            new RepoModItemViewModel("utility.spectatorfx", "SpectatorFX", "utility", "community",
                "0.4.2", "14.2.6", 4201, true,
                "Enhanced spectator camera, pings, and nameplate overlays.",
                "6h ago", false),
            new RepoModItemViewModel("map.undergroundex", "Underground Expanded", "map", "mapmakers-guild",
                "1.0.0", "14.2.6", 1890, true,
                "Expands HCZ with 4 additional rooms and new routes.",
                "2w ago", false),
        };

        RepoMods.Clear();
        foreach (var i in items)
            RepoMods.Add(i);

        // Mark installed state by id match against locally loaded mods
        foreach (var repo in RepoMods)
        {
            if (Mods.Any(m => string.Equals(m.DisplayName, repo.Name, StringComparison.OrdinalIgnoreCase)))
                repo.IsInstalled = true;
        }

        RefreshRepoFilter();
    }

    partial void OnLaunchArgsChanged(string value)
    {
        SaveLaunchArgs(value);
    }

    private string LoadLaunchArgs()
    {
        try
        {
            var file = Path.Combine(_paths.AnomalyDataDir, "launch_args.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void SaveLaunchArgs(string args)
    {
        try
        {
            var dir = _paths.AnomalyDataDir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "launch_args.txt"), args);
        }
        catch
        {
            // Ignore save failures
        }
    }

    private void LoadStaticInfo()
    {
        MelonLoaderVersion = _systemInfo.GetMelonLoaderVersion() ?? Tr.Get(TrKeys.InfoPanel_NotInstalled);
    }

    // ===================== State Machine =====================

    [RelayCommand]
    private async Task RefreshState()
    {
        LoadStaticInfo();
        await RefreshStateAsync();
        LoadMods();
    }

    private async Task RefreshStateAsync()
    {
        if (IsCheckingState) return;
        IsCheckingState = true;

        try
        {
            // 1. Check if game is installed
            var moddedExe = _paths.GetModdedExePath();
            var vanillaExe = _paths.GetVanillaExePath();

            if (!File.Exists(moddedExe) && vanillaExe == null)
            {
                CurrentState = LauncherState.GameNotInstalled;
                ClearBuildInfo();
                return;
            }

            // 2. Load GameAssembly comparison
            LoadGameAssemblyInfo();

            // 3. Check GameAssembly dates — vanilla updated?
            var comparison = _systemInfo.CompareGameAssembly();
            if (comparison is { VanillaIsNewer: true })
            {
                CurrentState = LauncherState.VanillaUpdated;
                await LoadBuildInfoAsync();
                SetupWatchers();
                return;
            }

            // 4. Check build IDs
            await LoadBuildInfoAsync();

            if (LocalBuildId != null && OnlineBuildId != null && LocalBuildId != OnlineBuildId)
            {
                CurrentState = LauncherState.OnlineBuildMismatch;
                SetupWatchers();
                return;
            }

            // 5. All good
            CurrentState = LauncherState.AllGood;
            SetupWatchers();
        }
        finally
        {
            IsCheckingState = false;
        }
    }

    private async Task LoadBuildInfoAsync()
    {
        var info = await _versionCheck.CheckBuildAsync();
        Branch = info.Branch;
        LocalBuildId = info.LocalBuildId;
        OnlineBuildId = info.OnlineBuildId;
    }

    private void LoadGameAssemblyInfo()
    {
        var comparison = _systemInfo.CompareGameAssembly();
        if (comparison != null)
        {
            VanillaGaDate = comparison.VanillaLastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ModdedGaDate = comparison.ModdedLastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            VanillaGaDate = null;
            ModdedGaDate = null;
        }
    }

    private void ClearBuildInfo()
    {
        Branch = null;
        LocalBuildId = null;
        OnlineBuildId = null;
        VanillaGaDate = null;
        ModdedGaDate = null;
    }

    // ===================== FileSystemWatchers =====================

    private void SetupWatchers()
    {
        DisposeWatchers();

        // Watch appmanifest for build ID changes
        var steamapps = _paths.SteamappsDir;
        if (steamapps != null && Directory.Exists(steamapps))
        {
            try
            {
                _acfWatcher = new FileSystemWatcher(steamapps, "appmanifest_700330.acf")
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _acfWatcher.Changed += OnWatchedFileChanged;
            }
            catch
            {
                // Watcher setup failed — rely on manual refresh
            }
        }

        // Watch vanilla GameAssembly.dll for modification
        var vanillaDir = _paths.VanillaDir;
        if (vanillaDir != null && Directory.Exists(vanillaDir))
        {
            try
            {
                _gaWatcher = new FileSystemWatcher(vanillaDir, "GameAssembly.dll")
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _gaWatcher.Changed += OnWatchedFileChanged;
            }
            catch
            {
                // Watcher setup failed — rely on manual refresh
            }
        }
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastWatcherEvent).TotalSeconds < 1)
            return;
        _lastWatcherEvent = now;

        PostToUI(() => _ = RefreshStateAsync());
    }

    private void DisposeWatchers()
    {
        if (_acfWatcher != null)
        {
            _acfWatcher.Changed -= OnWatchedFileChanged;
            _acfWatcher.Dispose();
            _acfWatcher = null;
        }

        if (_gaWatcher != null)
        {
            _gaWatcher.Changed -= OnWatchedFileChanged;
            _gaWatcher.Dispose();
            _gaWatcher = null;
        }
    }

    // ===================== Mod list =====================

    private void LoadMods()
    {
        foreach (var old in Mods)
            old.PropertyChanged -= OnModItemPropertyChanged;

        Mods.Clear();
        foreach (var mod in _modManager.GetMods())
        {
            var vm = new ModItemViewModel(mod, _modManager, null);
            vm.PropertyChanged += OnModItemPropertyChanged;
            Mods.Add(vm);
        }
        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(ModCountHeader));
    }

    private void OnModItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModItemViewModel.IsActive))
        {
            OnPropertyChanged(nameof(EnabledModCount));
            OnPropertyChanged(nameof(ModCountHeader));
        }
    }

    [RelayCommand]
    private void SelectMod(ModItemViewModel? mod)
    {
        SelectedMod = mod;
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        try
        {
            if (Directory.Exists(_paths.ModsDir))
                Process.Start(new ProcessStartInfo { FileName = _paths.ModsDir, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void OpenVanillaFolder()
    {
        try
        {
            var dir = _paths.VanillaDir;
            if (dir != null && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    // ===================== Username editing =====================

    [RelayCommand]
    private void BeginEditUsername()
    {
        EditingUsernameText = Username;
        IsEditingUsername = true;
    }

    [RelayCommand]
    private void SaveUsername()
    {
        var trimmed = EditingUsernameText.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed != Username)
        {
            if (_userProfile.SaveUsername(trimmed))
                Username = trimmed;
        }

        IsEditingUsername = false;
    }

    [RelayCommand]
    private void CancelEditUsername()
    {
        IsEditingUsername = false;
    }

    [RelayCommand]
    private void CopyUserId()
    {
        var userId = _userProfile.GetUserId();
        if (userId != null)
        {
            _copyToClipboard?.Invoke(userId);
            GameStatusText = Tr.Get(TrKeys.Status_UserIdCopied);
        }
        else
        {
            GameStatusText = Tr.Get(TrKeys.Status_NoIdentity);
        }
    }

    /// <summary>
    /// Set by the View to provide clipboard access (Avalonia clipboard requires a Window reference).
    /// </summary>
    public Action<string>? _copyToClipboard;

    // ===================== Launch / Kill =====================

    [RelayCommand]
    private void LaunchVanilla()
    {
        if (_gameProcess.LaunchVanilla())
        {
            IsGameRunning = true;
            GameStatusText = Tr.Get(TrKeys.Status_GameRunningVanilla);
        }
        else
        {
            GameStatusText = FormatLaunchFailure(TrKeys.Status_FailedLaunchVanilla);
        }
    }

    [RelayCommand]
    private void LaunchModded()
    {
        var args = string.IsNullOrWhiteSpace(LaunchArgs) ? null : LaunchArgs.Trim();
        if (_gameProcess.LaunchModded(args))
        {
            IsGameRunning = true;
            GameStatusText = Tr.Get(TrKeys.Status_GameRunningModded);
        }
        else
        {
            GameStatusText = FormatLaunchFailure(TrKeys.Status_FailedLaunchModded);
        }
    }

    private string FormatLaunchFailure(string fallbackKey) => _gameProcess.LastFailureReason switch
    {
        LaunchFailureReason.WineUnavailable => Tr.Get(TrKeys.Status_WineMissing),
        LaunchFailureReason.ExeNotFound    => Tr.Get(TrKeys.Status_ExeMissing),
        _                                   => Tr.Get(fallbackKey)
    };

    [RelayCommand]
    private void KillGame()
    {
        _gameProcess.KillGame();
    }

    // ===================== State 1: Set vanilla path =====================

    [RelayCommand]
    private async Task SetVanillaPath()
    {
        var path = ManualVanillaPath.Trim();
        if (string.IsNullOrEmpty(path))
        {
            GameStatusText = Tr.Get(TrKeys.Status_EnterPath);
            return;
        }

        // Accept either the exe or its parent directory
        if (Directory.Exists(path))
        {
            var exeInDir = Path.Combine(path, "SCPSL.exe");
            if (!File.Exists(exeInDir))
            {
                GameStatusText = Tr.Get(TrKeys.Status_ExeNotInDir);
                return;
            }
        }
        else if (File.Exists(path) && path.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Good — it's the exe directly
        }
        else
        {
            GameStatusText = Tr.Get(TrKeys.Status_InvalidPath);
            return;
        }

        _paths.SaveVanillaPath(path);
        GameStatusText = Tr.Get(TrKeys.Status_VanillaPathSet);

        // Launch installer to set up
        SyncAnomaly();

        await RefreshStateAsync();
    }

    public void HandleFileDrop(string[] files)
    {
        foreach (var file in files)
        {
            if (file.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
            {
                ManualVanillaPath = file;
                _ = SetVanillaPathCommand.ExecuteAsync(null);
                return;
            }

            // If they dropped a directory, check inside
            if (Directory.Exists(file))
            {
                var exe = Path.Combine(file, "SCPSL.exe");
                if (File.Exists(exe))
                {
                    ManualVanillaPath = file;
                    _ = SetVanillaPathCommand.ExecuteAsync(null);
                    return;
                }
            }
        }

        GameStatusText = Tr.Get(TrKeys.Status_NoExeInDrop);
    }

    // ===================== Steam URI commands =====================

    [RelayCommand]
    private void InstallGame()
    {
        OpenSteamUri("steam://install/700330");
        GameStatusText = Tr.Get(TrKeys.Status_OpeningInstall);
    }

    [RelayCommand]
    private void UpdateScpsl()
    {
        OpenSteamUri("steam://install/700330");
        GameStatusText = Tr.Get(TrKeys.Status_OpeningUpdate);
    }

    [RelayCommand]
    private void ValidateGameFiles()
    {
        OpenSteamUri("steam://validate/700330");
        GameStatusText = Tr.Get(TrKeys.Status_OpeningValidate);
    }

    private static void OpenSteamUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch
        {
            // Steam not installed or URI handler not registered
        }
    }

    // ===================== Sync Anomaly (in-process installer) =====================

    [RelayCommand]
    private void SyncAnomaly()
    {
        StartInstall(targetVersion: null, skipAnomalyContent: false);
    }

    private void StartInstall(string? targetVersion, bool skipAnomalyContent)
    {
        if (ShowInstallProgressDialog) return; // Already running

        var vanillaExe = _paths.GetVanillaExePath();

        var options = new InstallOptions
        {
            InstallerDir = _paths.LauncherDir,
            VanillaPath = vanillaExe,
            TargetVersion = targetVersion,
            NoAnomaly = skipAnomalyContent,
        };

        var coordinator = new InstallerCoordinator();
        var dialogVm = new InstallProgressDialogViewModel(coordinator);
        InstallProgressDialogViewModel = dialogVm;
        ShowInstallProgressDialog = true;

        var installTask = coordinator.RunInstallAsync(options);
        _ = dialogVm.TrackAsync(installTask).ContinueWith(_ =>
        {
            RefreshStateCommand.Execute(null);
        }, TaskScheduler.FromCurrentSynchronizationContext());

        GameStatusText = Tr.Get(TrKeys.Status_InstallerLaunched);
    }

    [RelayCommand]
    private void CloseInstallProgressDialog()
    {
        ShowInstallProgressDialog = false;
        InstallProgressDialogViewModel = null;
    }

    // ===================== Steam download dialog =====================

    [RelayCommand]
    private void OpenSteamDownloadDialog()
    {
        var vm = new SteamDownloadDialogViewModel(_steamDownload, _buildCatalog, _paths);
        vm.BuildActivated += OnSteamBuildActivated;
        SteamDownloadDialogViewModel = vm;
        ShowSteamDownloadDialog = true;
    }

    [RelayCommand]
    private void CloseSteamDownloadDialog()
    {
        if (SteamDownloadDialogViewModel != null)
            SteamDownloadDialogViewModel.BuildActivated -= OnSteamBuildActivated;
        ShowSteamDownloadDialog = false;
        SteamDownloadDialogViewModel = null;
    }

    private void OnSteamBuildActivated(string installPath, bool isPatchCompatible, string? gameVersion)
    {
        // The Steam download lands files directly in the launcher directory (same location the
        // installer's normal Steam copy stage would deposit them). Point vanilla_path at it and
        // kick off the in-process installer so it patches in place.
        _paths.SaveVanillaPath(installPath);
        StartInstall(targetVersion: gameVersion, skipAnomalyContent: !isPatchCompatible);
    }

    // ===================== Crash dialog =====================

    [RelayCommand]
    private void CloseCrashDialog()
    {
        ShowCrashDialog = false;
        CrashDialogViewModel = null;
    }

    [RelayCommand]
    private void OpenCrashReportsFolder()
    {
        try
        {
            if (Directory.Exists(_paths.CrashReportsDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _paths.CrashReportsDir,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore
        }
    }

    // ===================== Events =====================

    private void OnGameExited(object? sender, int exitCode)
    {
        PostToUI(() =>
        {
            IsGameRunning = false;

            var reports = _crashReport.GetNewReportsSince(
                _gameProcess.GameStartTime ?? DateTime.UtcNow.AddMinutes(-5));

            if (exitCode != 0 || reports.Count > 0)
            {
                GameStatusText = Tr.Get(TrKeys.Status_GameExitedAbnormally, exitCode);
                if (reports.Count > 0)
                {
                    CrashDialogViewModel = new CrashReportDialogViewModel(reports);
                    ShowCrashDialog = true;
                }
            }
            else
            {
                GameStatusText = Tr.Get(TrKeys.Status_GameExitedNormally);
            }
        });
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }

    public void Dispose()
    {
        _gameProcess.GameExited -= OnGameExited;
        DisposeWatchers();
        _crashReport.Dispose();
    }
}
