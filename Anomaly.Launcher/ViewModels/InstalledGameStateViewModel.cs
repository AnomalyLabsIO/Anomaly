using Anomaly.InstallerCore;
using Anomaly.Launcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anomaly.Launcher.ViewModels;

/// <summary>
/// Surfaces the launcher's current install-state sidecar to the Game Versions tab so the
/// "Currently Installed" panel can render manifest/build/version metadata and gate the
/// Reinstall/Uninstall buttons.
/// </summary>
public partial class InstalledGameStateViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string? _manifestId;

    [ObservableProperty]
    private string? _buildId;

    [ObservableProperty]
    private string? _gameVersion;

    [ObservableProperty]
    private string? _source;

    [ObservableProperty]
    private string? _installedAtDisplay;

    public bool HasManifestId => !string.IsNullOrEmpty(ManifestId);
    public bool IsSteamSource => string.Equals(Source, "steam", StringComparison.OrdinalIgnoreCase);
    public bool IsLocalSource => string.Equals(Source, "local", StringComparison.OrdinalIgnoreCase);
    public bool CanReinstall => IsInstalled && (IsSteamSource ? HasManifestId : true);

    partial void OnSourceChanged(string? value)
    {
        OnPropertyChanged(nameof(IsSteamSource));
        OnPropertyChanged(nameof(IsLocalSource));
        OnPropertyChanged(nameof(CanReinstall));
    }

    partial void OnManifestIdChanged(string? value)
    {
        OnPropertyChanged(nameof(HasManifestId));
        OnPropertyChanged(nameof(CanReinstall));
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReinstall));
    }

    public void Reload(PathService paths)
    {
        var state = InstallStateSidecar.TryRead(paths.LauncherDir);
        if (state is null)
        {
            IsInstalled = false;
            ManifestId = null;
            BuildId = null;
            GameVersion = null;
            Source = null;
            InstalledAtDisplay = null;
            return;
        }

        ManifestId = state.ManifestId;
        BuildId = state.BuildId;
        GameVersion = state.GameVersion;
        Source = state.Source;
        InstalledAtDisplay = DateTime.TryParse(state.InstalledAt, out var dt)
            ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : state.InstalledAt;
        IsInstalled = true;
    }
}
