using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anomaly.Launcher.ViewModels;

/// <summary>
/// One mod shown on the Public Repository tab. Backed by the live Anomaly mods platform —
/// see <see cref="MainWindowViewModel.LoadOnlineRepoModsAsync"/>. Install/Uninstall commands
/// delegate to the main view-model so a single <c>ModInstaller</c> instance handles staging.
/// </summary>
public partial class RepoModItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Author { get; }
    public string Version { get; }
    public string Target { get; }              // e.g. "14.2.6, 14.3.0"
    public IReadOnlyList<string> GameVersions { get; }
    public int Downloads { get; }
    public string DownloadsText => Downloads.ToString("N0");
    public bool Verified { get; }
    public string Tagline { get; }
    public string Updated { get; }
    public string License { get; }
    public string Size { get; }
    public string Sha256Short { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _installedVersion;

    public bool HasUpdate =>
        IsInstalled && !string.IsNullOrEmpty(InstalledVersion) && InstalledVersion != Version;

    public string CategoryUpper => Category.ToUpperInvariant();

    public IRelayCommand InstallCommand { get; }
    public IRelayCommand UninstallCommand { get; }

    public RepoModItemViewModel(
        string id,
        string name,
        string category,
        string author,
        string version,
        string target,
        IReadOnlyList<string> gameVersions,
        int downloads,
        bool verified,
        string tagline,
        string updated,
        bool isInstalled,
        string? installedVersion,
        IRelayCommand installCommand,
        IRelayCommand uninstallCommand,
        string license = "—",
        string size = "—",
        string sha256Short = "—",
        string? description = null)
    {
        Id = id;
        Name = name;
        Category = category;
        Author = author;
        Version = version;
        Target = target;
        GameVersions = gameVersions;
        Downloads = downloads;
        Verified = verified;
        Tagline = tagline;
        Updated = updated;
        License = license;
        Size = size;
        Sha256Short = sha256Short;
        Description = description ?? tagline;
        _isInstalled = isInstalled;
        _installedVersion = installedVersion;
        InstallCommand = installCommand;
        UninstallCommand = uninstallCommand;
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(HasUpdate));
    partial void OnInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(HasUpdate));
}
