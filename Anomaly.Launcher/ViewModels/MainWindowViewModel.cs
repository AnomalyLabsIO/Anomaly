using System.Collections.ObjectModel;
using System.Diagnostics;
using Anomaly.Launcher.Models;
using Anomaly.Launcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly PathService _paths;
    private readonly UserProfileService _userProfile;
    private readonly ModManagerService _modManager;
    private readonly GameProcessService _gameProcess;
    private readonly CrashReportService _crashReport;
    private readonly VersionCheckService _versionCheck;
    private readonly SystemInfoService _systemInfo;
    private readonly SynchronizationContext? _syncContext;

    private FileSystemWatcher? _acfWatcher;
    private FileSystemWatcher? _gaWatcher;
    private DateTime _lastWatcherEvent = DateTime.MinValue;

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

    [ObservableProperty]
    private bool _hasInstallerAvailable;

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

    // --- Game state ---

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private string _gameStatusText = "Ready";

    // --- State 1: manual path entry ---

    [ObservableProperty]
    private string _manualVanillaPath = string.Empty;

    // --- Crash dialog ---

    [ObservableProperty]
    private bool _showCrashDialog;

    [ObservableProperty]
    private CrashReportDialogViewModel? _crashDialogViewModel;

    // --- Computed ---

    public bool CanLaunch => !IsGameRunning && IsGameInstalled;

    partial void OnCurrentStateChanged(LauncherState value)
    {
        OnPropertyChanged(nameof(IsGameNotInstalled));
        OnPropertyChanged(nameof(IsVanillaUpdated));
        OnPropertyChanged(nameof(IsOnlineBuildMismatch));
        OnPropertyChanged(nameof(IsAllGood));
        OnPropertyChanged(nameof(IsGameInstalled));
        OnPropertyChanged(nameof(CanLaunch));
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
        SystemInfoService systemInfo)
    {
        _paths = paths;
        _userProfile = userProfile;
        _modManager = modManager;
        _gameProcess = gameProcess;
        _crashReport = crashReport;
        _versionCheck = versionCheck;
        _systemInfo = systemInfo;
        _syncContext = SynchronizationContext.Current;

        Username = userProfile.GetUsername();
        LoadMods();
        LoadStaticInfo();

        _gameProcess.GameExited += OnGameExited;

        _ = RefreshStateAsync();
    }

    private void LoadStaticInfo()
    {
        MelonLoaderVersion = _systemInfo.GetMelonLoaderVersion() ?? "Not installed";
        HasInstallerAvailable = _systemInfo.GetInstallerPath() != null;
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
        Mods.Clear();
        foreach (var mod in _modManager.GetMods())
        {
            Mods.Add(new ModItemViewModel(mod, _modManager, null));
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

    // ===================== Launch / Kill =====================

    [RelayCommand]
    private void LaunchVanilla()
    {
        if (_gameProcess.LaunchVanilla())
        {
            IsGameRunning = true;
            GameStatusText = "Game running (vanilla)";
        }
        else
        {
            GameStatusText = "Failed to launch vanilla game";
        }
    }

    [RelayCommand]
    private void LaunchModded()
    {
        if (_gameProcess.LaunchModded())
        {
            IsGameRunning = true;
            GameStatusText = "Game running (modded)";
        }
        else
        {
            GameStatusText = "Failed to launch modded game";
        }
    }

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
            GameStatusText = "Please enter a path";
            return;
        }

        // Accept either the exe or its parent directory
        if (Directory.Exists(path))
        {
            var exeInDir = Path.Combine(path, "SCPSL.exe");
            if (!File.Exists(exeInDir))
            {
                GameStatusText = "SCPSL.exe not found in that directory";
                return;
            }
        }
        else if (File.Exists(path) && path.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Good — it's the exe directly
        }
        else
        {
            GameStatusText = "Invalid path — provide the SCPSL.exe or its directory";
            return;
        }

        _paths.SaveVanillaPath(path);
        GameStatusText = "Vanilla path set";

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

        GameStatusText = "No SCPSL.exe found in dropped files";
    }

    // ===================== Steam URI commands =====================

    [RelayCommand]
    private void InstallGame()
    {
        OpenSteamUri("steam://install/700330");
        GameStatusText = "Opening Steam to install SCP:SL...";
    }

    [RelayCommand]
    private void UpdateScpsl()
    {
        OpenSteamUri("steam://install/700330");
        GameStatusText = "Opening Steam to update SCP:SL...";
    }

    [RelayCommand]
    private void ValidateGameFiles()
    {
        OpenSteamUri("steam://validate/700330");
        GameStatusText = "Opening Steam to validate game files...";
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

    // ===================== Sync Anomaly (Installer) =====================

    [RelayCommand]
    private void SyncAnomaly()
    {
        var installerPath = _systemInfo.GetInstallerPath();
        if (installerPath == null)
        {
            GameStatusText = "Anomaly.Installer not found next to launcher";
            return;
        }

        var vanillaExe = _paths.GetVanillaExePath();
        if (_gameProcess.LaunchInstaller(installerPath, vanillaExe))
            GameStatusText = "Installer launched";
        else
            GameStatusText = "Failed to launch installer";
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
                GameStatusText = $"Game exited abnormally (code {exitCode})";
                if (reports.Count > 0)
                {
                    CrashDialogViewModel = new CrashReportDialogViewModel(reports);
                    ShowCrashDialog = true;
                }
            }
            else
            {
                GameStatusText = "Game exited normally";
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
