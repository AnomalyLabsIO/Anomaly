using System.Collections.ObjectModel;
using System.IO;
using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Localization;
using Anomaly.Launcher.Services.Steam;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

public enum SteamDownloadStep
{
    SignIn,
    PickVersion,
    Downloading,
    Done,
    Error
}

public partial class SteamDownloadDialogViewModel : ObservableObject
{
    private readonly SteamDownloadService _steam;
    private readonly BuildCatalogService _catalog;
    private readonly PathService _paths;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private SteamDownloadStep _currentStep = SteamDownloadStep.SignIn;

    // --- Page 1: version picker ---

    public ObservableCollection<SteamBuildCatalogEntry> AvailableBuilds { get; } = new();

    [ObservableProperty]
    private SteamBuildCatalogEntry? _selectedBuild;

    [ObservableProperty]
    private bool _useManifestPaste;

    [ObservableProperty]
    private string _pastedManifestId = string.Empty;

    [ObservableProperty]
    private bool _showAllBuilds;

    // --- Page 2: QR sign-in ---

    [ObservableProperty]
    private Bitmap? _qrImage;

    [ObservableProperty]
    private string _signInStatus = string.Empty;

    [ObservableProperty]
    private bool _rememberMe = true;

    // --- Page 3: download progress ---

    [ObservableProperty]
    private double _downloadProgressPercent;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _downloadSpeedText = string.Empty;

    // --- Page 4/5: done / error ---

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _completedPath = string.Empty;

    // Signals to the host (MainWindow) that the dialog should close and the caller should
    // invoke the installer handoff. Null if the user cancelled.
    public string? ResultInstallPath { get; private set; }

    // True when the version the user selected is known-compatible with Anomaly's patch set.
    // False for pasted manifests (unknown) or catalog entries flagged incompatible.
    // Used by the host to decide whether to pass --no-anomaly to the installer.
    public bool ResultIsPatchCompatible { get; private set; }

    // The catalog's authoritative gameVersion for the selected build (from the CSV/PICS enrichment
    // pipeline). Null for pasted manifests or catalog entries without a resolved version. Passed
    // to the installer as --target-version so it doesn't have to read globalgamemanagers — the
    // bundle-version scan is unreliable for some older Unity builds.
    public string? ResultGameVersion { get; private set; }

    // Computed view flags
    public bool IsPickVersion => CurrentStep == SteamDownloadStep.PickVersion;
    public bool IsSignIn => CurrentStep == SteamDownloadStep.SignIn;
    public bool IsDownloading => CurrentStep == SteamDownloadStep.Downloading;
    public bool IsDone => CurrentStep == SteamDownloadStep.Done;
    public bool IsError => CurrentStep == SteamDownloadStep.Error;

    partial void OnCurrentStepChanged(SteamDownloadStep value)
    {
        OnPropertyChanged(nameof(IsPickVersion));
        OnPropertyChanged(nameof(IsSignIn));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
    }

    // Event raised when the user approves an installed build — the host wires this to the
    // installer handoff. Carries install path, compatibility flag, the catalog's resolved
    // game version (null if unknown), manifest ID, and the logged-in Steam ID.
    public event Action<string, bool, string?, ulong, ulong>? BuildActivated;

    // Event raised to close the dialog window.
    public event Action? CloseRequested;

    [ObservableProperty]
    private bool _isCatalogLoading;

    // When set by the caller, the dialog auto-downloads this manifest as soon as sign-in
    // succeeds, skipping the version picker. Used by the Reinstall flow to re-pin the
    // currently-installed manifest.
    private readonly ulong? _preSelectedManifestId;
    private ulong _steamId;

    public SteamDownloadDialogViewModel(
        SteamDownloadService steam,
        BuildCatalogService catalog,
        PathService paths,
        ulong? preSelectedManifestId = null)
    {
        _steam = steam;
        _catalog = catalog;
        _paths = paths;
        _preSelectedManifestId = preSelectedManifestId;

        // Kick the sign-in flow as soon as the dialog opens — silent-refresh if we have a
        // cached token, otherwise show the QR code. Version picking only happens once we're
        // authenticated, because the live manifest list comes from an authenticated PICS call.
        _ = StartSignInAsync();
    }

    private void EnsureValidSelection()
    {
        if (SelectedBuild == null || !AvailableBuilds.Contains(SelectedBuild))
            SelectedBuild = AvailableBuilds.FirstOrDefault();
    }

    // ===================== Sign-in =====================

    private async Task StartSignInAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Silent re-auth path
        if (_steam.HasStoredToken())
        {
            SignInStatus = Tr.Get(TrKeys.SteamDl_SignIn_RefreshingToken);
            var silent = await _steam.LoginWithStoredTokenAsync(ct).ConfigureAwait(true);
            if (silent is SteamLoginResult.Success silentSuccess)
            {
                _steamId = silentSuccess.SteamId;
                await OnSignInSucceededAsync(ct).ConfigureAwait(true);
                return;
            }
            // Token dead — fall through to QR
        }

        SignInStatus = Tr.Get(TrKeys.SteamDl_SignIn_WaitingForChallenge);

        var qrProgress = new Progress<QrChallenge>(challenge =>
        {
            try
            {
                using var ms = new MemoryStream(challenge.QrPngBytes);
                QrImage = new Bitmap(ms);
                SignInStatus = Tr.Get(TrKeys.SteamDl_SignIn_WaitingForScan);
            }
            catch
            {
                // Bitmap decode failed — leave previous image if any
            }
        });

        var loginResult = await _steam.LoginWithQrAsync(qrProgress, RememberMe, ct).ConfigureAwait(true);
        switch (loginResult)
        {
            case SteamLoginResult.Success success:
                _steamId = success.SteamId;
                SignInStatus = Tr.Get(TrKeys.SteamDl_SignIn_Success);
                await OnSignInSucceededAsync(ct).ConfigureAwait(true);
                break;
            case SteamLoginResult.Cancelled:
                RequestClose();
                break;
            case SteamLoginResult.QrExpired:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_QrExpired);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamLoginResult.UserDeclined:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_UserDeclined);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamLoginResult.NetworkFailure nf:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_Network, nf.Message);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamLoginResult.TokenExpired:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_TokenExpired);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamLoginResult.UnexpectedError ue:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_Unexpected, ue.Message);
                CurrentStep = SteamDownloadStep.Error;
                break;
        }
    }

    private IReadOnlyList<SteamBuildCatalogEntry> _lastLive = Array.Empty<SteamBuildCatalogEntry>();

    private async Task OnSignInSucceededAsync(CancellationToken ct)
    {
        CurrentStep = SteamDownloadStep.PickVersion;

        // Seed synchronously from embedded catalog so the list isn't empty while we fetch.
        ApplyCatalogView(Array.Empty<SteamBuildCatalogEntry>());

        await RefreshCatalogAsync(ct).ConfigureAwait(true);

        // Reinstall flow: caller asked us to re-pin a known manifest. Skip the picker and
        // jump straight into the download — pre-select the catalog row so the post-download
        // metadata (gameVersion, compatibility) flows through the same path the picker uses.
        if (_preSelectedManifestId is ulong manifestId)
        {
            var match = AvailableBuilds.FirstOrDefault(b => b.ManifestId == manifestId);
            if (match != null)
                SelectedBuild = match;
            else
                UseManifestPaste = true;
            PastedManifestId = manifestId.ToString();

            await RunDownloadAsync(manifestId, ct).ConfigureAwait(true);
        }
    }

    // ===================== Catalog =====================

    partial void OnShowAllBuildsChanged(bool value) => ApplyCatalogView(_lastLive);

    [RelayCommand]
    private async Task RefreshCatalog()
    {
        await RefreshCatalogAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task RefreshCatalogAsync(CancellationToken ct)
    {
        if (IsCatalogLoading) return;
        IsCatalogLoading = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            IReadOnlyList<SteamBuildCatalogEntry> live = Array.Empty<SteamBuildCatalogEntry>();
            if (_steam.HasStoredToken() || CurrentStep == SteamDownloadStep.PickVersion)
            {
                try
                {
                    live = await _steam.FetchManifestListAsync(timeout.Token).ConfigureAwait(true);
                }
                catch
                {
                    // Fall back to seeds-only when PICS fetch fails (offline, session dropped, etc.)
                }
            }

            _lastLive = live;
            ApplyCatalogView(live);
        }
        finally
        {
            IsCatalogLoading = false;
        }
    }

    private void ApplyCatalogView(IReadOnlyList<SteamBuildCatalogEntry> live)
    {
        var merged = _catalog.Merge(live, ShowAllBuilds);
        AvailableBuilds.Clear();
        foreach (var entry in merged)
            AvailableBuilds.Add(entry);
        EnsureValidSelection();
    }

    // ===================== Download =====================

    [RelayCommand]
    private async Task StartDownload()
    {
        var manifestId = ResolveManifestId();
        if (manifestId == 0)
        {
            ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_NoVersionSelected);
            CurrentStep = SteamDownloadStep.Error;
            return;
        }

        _cts = new CancellationTokenSource();
        await RunDownloadAsync(manifestId, _cts.Token).ConfigureAwait(true);
    }

    private ulong _lastDownloadedManifestId;

    private async Task RunDownloadAsync(ulong manifestId, CancellationToken ct)
    {
        CurrentStep = SteamDownloadStep.Downloading;
        DownloadStatusText = Tr.Get(TrKeys.SteamDl_Download_Starting);
        _lastDownloadedManifestId = manifestId;

        var progress = new Progress<SteamDownloadProgress>(p =>
        {
            DownloadProgressPercent = p.BytesTotal > 0 ? (double)p.BytesDownloaded / p.BytesTotal * 100.0 : 0;
            CurrentFileName = p.CurrentFile ?? string.Empty;
            var mb = p.BytesDownloaded / 1024.0 / 1024.0;
            var totalMb = p.BytesTotal / 1024.0 / 1024.0;
            var speedMb = p.BytesPerSecond / 1024.0 / 1024.0;
            DownloadStatusText = Tr.Get(TrKeys.SteamDl_Download_Progress, mb.ToString("F1"), totalMb.ToString("F1"));
            DownloadSpeedText = Tr.Get(TrKeys.SteamDl_Download_Speed, speedMb.ToString("F1"));
        });

        var destDir = _paths.LauncherDir;
        var result = await _steam.DownloadBuildAsync(manifestId, destDir, progress, ct);

        switch (result)
        {
            case SteamDownloadResult.Success s:
                CompletedPath = s.DestinationPath;
                ResultInstallPath = s.DestinationPath;
                ResultIsPatchCompatible = !UseManifestPaste && (SelectedBuild?.IsPatchCompatible ?? false);
                ResultGameVersion = UseManifestPaste ? null : SelectedBuild?.GameVersion;
                CurrentStep = SteamDownloadStep.Done;

                // Reinstall flow auto-confirms — the user already committed to the manifest
                // up front, so don't make them click "Use this build" again.
                if (_preSelectedManifestId.HasValue)
                    UseBuildCommand.Execute(null);
                break;
            case SteamDownloadResult.Cancelled:
                RequestClose();
                break;
            case SteamDownloadResult.NotOwned:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_NotOwned);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamDownloadResult.InsufficientDiskSpace d:
                var reqMb = d.RequiredBytes / 1024.0 / 1024.0;
                var availMb = d.AvailableBytes / 1024.0 / 1024.0;
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_DiskSpace, reqMb.ToString("F0"), availMb.ToString("F0"));
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamDownloadResult.NetworkFailure nf:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_Network, nf.Message);
                CurrentStep = SteamDownloadStep.Error;
                break;
            case SteamDownloadResult.UnexpectedError ue:
                ErrorMessage = Tr.Get(TrKeys.SteamDl_Error_Unexpected, ue.Message);
                CurrentStep = SteamDownloadStep.Error;
                break;
        }
    }

    private ulong ResolveManifestId()
    {
        if (UseManifestPaste)
        {
            if (ulong.TryParse(PastedManifestId.Trim(), out var id))
                return id;
            return 0;
        }

        return SelectedBuild?.ManifestId ?? 0;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        RequestClose();
    }

    [RelayCommand]
    private void UseBuild()
    {
        if (!string.IsNullOrEmpty(CompletedPath))
            BuildActivated?.Invoke(CompletedPath, ResultIsPatchCompatible, ResultGameVersion, _lastDownloadedManifestId, _steamId);
        RequestClose();
    }

    [RelayCommand]
    private void DismissError()
    {
        CurrentStep = SteamDownloadStep.PickVersion;
        ErrorMessage = string.Empty;
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke();
    }
}
