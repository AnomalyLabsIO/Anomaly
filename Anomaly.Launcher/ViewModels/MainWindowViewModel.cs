using System.Collections.ObjectModel;
using System.Diagnostics;
using Anomaly.InstallerCore;
using Anomaly.Launcher.Models;
using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Install;
using Anomaly.Launcher.Services.Localization;
using Anomaly.Launcher.Services.Platform;
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

public enum InstallSource
{
    Steam,
    Local,
    Unknown,
}

/// <summary>
/// Top-right pill state. Compares the installed manifest/gameVersion against the latest catalog
/// entry. Drives whether the pill shows NOMINAL (green), UPDATE (red, same gameVersion but newer
/// manifest), or UPGRADE (orange, newer gameVersion available).
/// </summary>
public enum CatalogStatus
{
    Unknown,
    Nominal,
    Update,
    Upgrade,
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
    private readonly PlatformApiClient _platform;
    private readonly ModInstaller _modInstaller;
    private readonly InstalledModsService _installedMods;
    private readonly SynchronizationContext? _syncContext;

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
    private string? _moddedGaDate;

    // --- Currently installed game (drives Game Versions tab "Currently Installed" panel) ---

    [ObservableProperty]
    private InstalledGameStateViewModel _installedState = new();

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

    // --- Catalog status (drives top-right NOMINAL/UPDATE/UPGRADE pill) ---

    [ObservableProperty]
    private CatalogStatus _catalogStatus = CatalogStatus.Unknown;

    public bool IsCatalogNominal => CatalogStatus == CatalogStatus.Nominal;
    public bool IsCatalogUpdate  => CatalogStatus == CatalogStatus.Update;
    public bool IsCatalogUpgrade => CatalogStatus == CatalogStatus.Upgrade;

    partial void OnCatalogStatusChanged(CatalogStatus value)
    {
        OnPropertyChanged(nameof(IsCatalogNominal));
        OnPropertyChanged(nameof(IsCatalogUpdate));
        OnPropertyChanged(nameof(IsCatalogUpgrade));
    }

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
        BuildCatalogService buildCatalog,
        PlatformApiClient platform,
        ModInstaller modInstaller,
        InstalledModsService installedMods)
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
        _platform = platform;
        _modInstaller = modInstaller;
        _installedMods = installedMods;
        _syncContext = SynchronizationContext.Current;

        // Read the install-state sidecar synchronously so the initial tab + state are stable
        // before any async work runs. RefreshStateAsync() will reconcile to OnlineBuildMismatch
        // or VanillaUpdated later if the Steam buildId or vanilla files have drifted.
        InstalledState.Reload(_paths);
        if (!InstalledState.IsInstalled)
        {
            CurrentState = LauncherState.GameNotInstalled;
            _activeTab = LauncherTab.GameVersions;
        }
        else
        {
            CurrentState = LauncherState.AllGood;
        }

        Username = userProfile.GetUsername();
        LaunchArgs = LoadLaunchArgs();
        LoadMods();
        LoadStaticInfo();
        LoadGameVersions();
        _ = LoadOnlineRepoModsAsync();

        _gameProcess.GameExited += OnGameExited;

        _ = RefreshStateAsync();
    }

    // ===================== Game Versions =====================

    public void LoadGameVersions() => _ = LoadGameVersionsAsync();

    public async Task LoadGameVersionsAsync(CancellationToken ct = default)
    {
        IsLoadingGameVersions = true;
        try
        {
            var live = await _buildCatalog.FetchLiveAsync(_platform, _paths.GameBuildsCacheFile, ct);
            var entries = _buildCatalog.Merge(live, includeAll: false);
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
        var installedManifest = InstalledState.ManifestId;
        var installedBuild = InstalledState.BuildId;
        foreach (var v in GameVersions)
        {
            v.IsInstalled =
                (!string.IsNullOrEmpty(installedManifest) && v.ManifestId.ToString() == installedManifest)
                || (!string.IsNullOrEmpty(installedBuild) && v.BuildId == installedBuild);
        }
        OnPropertyChanged(nameof(GameVersionLabel));
        UpdateCatalogStatus();
    }

    /// <summary>
    /// Compares the installed sidecar against the latest catalog entry. Catalog is sorted
    /// newest-first by BuildCatalogService.Merge, so GameVersions[0] is the latest known build.
    /// </summary>
    private void UpdateCatalogStatus()
    {
        if (!InstalledState.IsInstalled || GameVersions.Count == 0
            || string.IsNullOrEmpty(InstalledState.ManifestId)
            || string.IsNullOrEmpty(InstalledState.GameVersion))
        {
            CatalogStatus = CatalogStatus.Unknown;
            return;
        }

        var latest = GameVersions[0];
        if (!string.IsNullOrEmpty(latest.GameVersion)
            && !string.Equals(latest.GameVersion, InstalledState.GameVersion, StringComparison.Ordinal))
        {
            CatalogStatus = CatalogStatus.Upgrade;
        }
        else if (latest.ManifestId.ToString() != InstalledState.ManifestId)
        {
            CatalogStatus = CatalogStatus.Update;
        }
        else
        {
            CatalogStatus = CatalogStatus.Nominal;
        }
    }

    [RelayCommand]
    private void TriggerCatalogUpdate()
    {
        if (CatalogStatus != CatalogStatus.Update || GameVersions.Count == 0) return;
        OpenSteamDownloadDialogInternal(preSelectedManifestId: GameVersions[0].ManifestId);
    }

    [RelayCommand]
    private void TriggerCatalogUpgrade()
    {
        if (CatalogStatus != CatalogStatus.Upgrade || GameVersions.Count == 0) return;
        ActiveTab = LauncherTab.GameVersions;
        SelectedGameVersion = GameVersions[0];
    }

    [RelayCommand]
    private void InstallGameVersion(GameVersionItemViewModel? entry)
    {
        entry ??= SelectedGameVersion;
        if (entry == null) return;

        // Use the preselected-manifest flow so the dialog signs in, then jumps straight
        // to the download — the previous code opened the generic picker and tried to set
        // SelectedBuild before the catalog finished loading, which always lost the race.
        OpenSteamDownloadDialogInternal(preSelectedManifestId: entry.ManifestId);
    }

    // ===================== Public Repository =====================

    [ObservableProperty]
    private bool _isLoadingRepo;

    [ObservableProperty]
    private string? _repoErrorMessage;

    [RelayCommand]
    private Task RefreshRepoMods() => LoadOnlineRepoModsAsync();

    public async Task LoadOnlineRepoModsAsync(CancellationToken ct = default)
    {
        IsLoadingRepo = true;
        RepoErrorMessage = null;
        try
        {
            var gameVersion = GameVersionFromBuild();
            var response = await _platform.ListModsAsync(gameVersion: gameVersion, ct: ct);
            if (response == null)
            {
                RepoErrorMessage = "Couldn't reach the mods catalog. Showing nothing for now.";
                return;
            }

            var installed = _installedMods.Load();
            var byInstalledId = installed.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

            RepoMods.Clear();
            foreach (var dto in response.Items)
            {
                var latest = dto.LatestVersion;
                if (latest == null) continue;
                var installedEntry = byInstalledId.TryGetValue(dto.Id, out var inst) ? inst : null;

                var item = new RepoModItemViewModel(
                    id: dto.Id,
                    name: dto.Name,
                    category: dto.Tags.FirstOrDefault() ?? "general",
                    author: dto.Author?.Username ?? "anonymous",
                    version: latest.Semver,
                    target: string.Join(", ", latest.GameVersions),
                    gameVersions: latest.GameVersions,
                    downloads: dto.Downloads,
                    verified: false,
                    tagline: dto.Description ?? "",
                    updated: FormatRelative(dto.UpdatedAt),
                    isInstalled: installedEntry != null,
                    installedVersion: installedEntry?.Version,
                    installCommand: new AsyncRelayCommand<RepoModItemViewModel>(InstallRepoModAsync),
                    uninstallCommand: new AsyncRelayCommand<RepoModItemViewModel>(UninstallRepoModAsync));
                RepoMods.Add(item);
            }

            RefreshRepoFilter();
        }
        catch (Exception ex)
        {
            RepoErrorMessage = $"Catalog fetch failed: {ex.Message}";
        }
        finally
        {
            IsLoadingRepo = false;
        }
    }

    private async Task InstallRepoModAsync(RepoModItemViewModel? item)
    {
        if (item == null || item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            var result = await _modInstaller.InstallAsync(item.Id, GameVersionFromBuild());
            var match = result.Installed.FirstOrDefault(m => string.Equals(m.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            item.IsInstalled = true;
            item.InstalledVersion = match?.Version ?? item.Version;
            // The local Mods/ folder now has new DLLs — re-scan so the Installed tab reflects them.
            LoadMods();
        }
        catch (Exception ex)
        {
            RepoErrorMessage = $"Install of {item.Id} failed: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task UninstallRepoModAsync(RepoModItemViewModel? item)
    {
        if (item == null || item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            await _modInstaller.UninstallAsync(item.Id);
            item.IsInstalled = false;
            item.InstalledVersion = null;
            LoadMods();
        }
        catch (Exception ex)
        {
            RepoErrorMessage = $"Uninstall of {item.Id} failed: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public void HandleDeepLinkInstall(UrlProtocolService.InstallRequest request)
    {
        ActiveTab = LauncherTab.PublicRepo;
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadOnlineRepoModsAsync();
                await _modInstaller.InstallAsync(request.ModId, GameVersionFromBuild());
                PostToUI(() =>
                {
                    LoadMods();
                    var item = RepoMods.FirstOrDefault(m => string.Equals(m.Id, request.ModId, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        item.IsInstalled = true;
                        item.InstalledVersion = item.Version;
                    }
                });
            }
            catch (Exception ex)
            {
                PostToUI(() => RepoErrorMessage = $"Deep-link install of {request.ModId} failed: {ex.Message}");
            }
        });
    }

    private static string FormatRelative(long unixSeconds)
    {
        if (unixSeconds <= 0) return "—";
        var when = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
        return when.ToString("yyyy-MM-dd");
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
            InstalledState.Reload(_paths);
            UpdateInstalledFlag();

            var moddedExe = _paths.GetModdedExePath();
            if (!InstalledState.IsInstalled || !File.Exists(moddedExe))
            {
                CurrentState = LauncherState.GameNotInstalled;
                ClearBuildInfo();
                return;
            }

            LoadGameAssemblyInfo();
            await LoadBuildInfoAsync();

            if (!string.IsNullOrEmpty(LocalBuildId)
                && !string.IsNullOrEmpty(OnlineBuildId)
                && LocalBuildId != OnlineBuildId)
            {
                CurrentState = LauncherState.OnlineBuildMismatch;
                return;
            }

            CurrentState = LauncherState.AllGood;
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
        var ts = _systemInfo.GetModdedGameAssemblyTimestamp();
        ModdedGaDate = ts?.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private void ClearBuildInfo()
    {
        Branch = null;
        LocalBuildId = null;
        OnlineBuildId = null;
        ModdedGaDate = null;
    }

    // ===================== Mod list =====================

    private void LoadMods()
    {
        foreach (var old in Mods)
            old.PropertyChanged -= OnModItemPropertyChanged;

        var currentVersion = InstalledState.GameVersion;

        Mods.Clear();
        foreach (var mod in _modManager.GetMods())
        {
            var vm = new ModItemViewModel(mod, _modManager, currentVersion);
            vm.PropertyChanged += OnModItemPropertyChanged;
            Mods.Add(vm);
        }
        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(ModCountHeader));
    }

    [RelayCommand]
    private void RefreshMods() => LoadMods();

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

    // ===================== Install orchestration =====================

    /// <summary>
    /// Set by MainWindow.axaml.cs so the launcher can show an Avalonia folder picker for the
    /// "Use local directory…" flow. Returns the selected directory path or null if cancelled.
    /// </summary>
    public Func<Task<string?>>? _pickLocalGameDirectory;

    [RelayCommand]
    private void SyncAnomaly()
    {
        // Re-run the patch pipeline against the existing files in LauncherDir. Used when the
        // game files are already in place but need (re)patching.
        _ = StartInstallAsync(InstallSource.Unknown, sourceManifestId: null,
            sourceSteamId: null,
            vanillaPath: _paths.LauncherDir,
            targetVersion: InstalledState.GameVersion,
            skipAnomalyContent: false);
    }

    private async Task StartInstallAsync(
        InstallSource source,
        string? sourceManifestId,
        ulong? sourceSteamId,
        string vanillaPath,
        string? targetVersion,
        bool skipAnomalyContent)
    {
        if (ShowInstallProgressDialog) return; // Already running

        var options = new InstallOptions
        {
            InstallerDir = _paths.LauncherDir,
            VanillaPath = vanillaPath,
            TargetVersion = targetVersion,
            NoAnomaly = skipAnomalyContent,
            SourceKind = source.ToString().ToLowerInvariant(),
            SourceManifestId = sourceManifestId,
            SourceSteamId = sourceSteamId,
        };

        var coordinator = new InstallerCoordinator();
        var dialogVm = new InstallProgressDialogViewModel(coordinator);
        InstallProgressDialogViewModel = dialogVm;
        ShowInstallProgressDialog = true;
        GameStatusText = Tr.Get(TrKeys.Status_InstallerLaunched);

        var installTask = coordinator.RunInstallAsync(options);
        await dialogVm.TrackAsync(installTask);

        await RefreshStateAsync();
        LoadMods();
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
        OpenSteamDownloadDialogInternal(preSelectedManifestId: null);
    }

    private void OpenSteamDownloadDialogInternal(ulong? preSelectedManifestId)
    {
        var vm = new SteamDownloadDialogViewModel(_steamDownload, _buildCatalog, _paths, preSelectedManifestId);
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

    private void OnSteamBuildActivated(string installPath, bool isPatchCompatible, string? gameVersion, ulong manifestId, ulong steamId)
    {
        // Steam download lands files directly in LauncherDir (same place InstallerCore would
        // copy them to). Run the patch in-place — installerDir == vanillaDir is fine because
        // SteamDownloadService wrote the .anomaly_source.json sidecar that gates that mode.
        _ = StartInstallAsync(
            source: InstallSource.Steam,
            sourceManifestId: manifestId.ToString(),
            sourceSteamId: steamId == 0 ? null : steamId,
            vanillaPath: installPath,
            targetVersion: gameVersion,
            skipAnomalyContent: !isPatchCompatible);
    }

    // ===================== Local-directory install / Reinstall / Uninstall =====================

    [RelayCommand]
    private async Task OpenLocalDirectoryInstall()
    {
        if (_pickLocalGameDirectory == null)
        {
            GameStatusText = "Folder picker is not available in this build.";
            return;
        }

        var picked = await _pickLocalGameDirectory();
        if (string.IsNullOrEmpty(picked))
            return;

        var exe = Path.Combine(picked, "SCPSL.exe");
        if (!File.Exists(exe))
        {
            GameStatusText = Tr.Get(TrKeys.Status_ExeNotInDir);
            return;
        }

        await StartInstallAsync(
            source: InstallSource.Local,
            sourceManifestId: null,
            sourceSteamId: null,
            vanillaPath: picked,
            targetVersion: null,
            skipAnomalyContent: false);
    }

    [RelayCommand]
    private async Task Reinstall()
    {
        if (!InstalledState.IsInstalled) return;

        if (InstalledState.IsSteamSource && !string.IsNullOrEmpty(InstalledState.ManifestId)
            && ulong.TryParse(InstalledState.ManifestId, out var manifestId))
        {
            OpenSteamDownloadDialogInternal(preSelectedManifestId: manifestId);
            return;
        }

        // Local source (or unknown): fall back to the directory picker.
        await OpenLocalDirectoryInstall();
    }

    [RelayCommand]
    private async Task Uninstall()
    {
        if (!InstalledState.IsInstalled) return;
        if (ShowInstallProgressDialog) return;

        var coordinator = new InstallerCoordinator();
        var dialogVm = new InstallProgressDialogViewModel(coordinator);
        InstallProgressDialogViewModel = dialogVm;
        ShowInstallProgressDialog = true;

        var task = coordinator.RunRemoveGameFilesAsync(_paths.LauncherDir);
        await dialogVm.TrackAsync(task);

        await RefreshStateAsync();
        LoadMods();
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
        _crashReport.Dispose();
    }
}
