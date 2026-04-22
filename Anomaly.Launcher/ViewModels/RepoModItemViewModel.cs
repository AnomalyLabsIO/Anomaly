using CommunityToolkit.Mvvm.ComponentModel;

namespace Anomaly.Launcher.ViewModels;

/// <summary>
/// One mod shown in the Public Repository tab. Populated from stub data today; a real
/// <c>repo.anomaly.scp</c> catalogue service will replace the source later.
/// </summary>
public partial class RepoModItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Author { get; }
    public string Version { get; }
    public string Target { get; }
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

    public string CategoryUpper => Category.ToUpperInvariant();

    public RepoModItemViewModel(
        string id,
        string name,
        string category,
        string author,
        string version,
        string target,
        int downloads,
        bool verified,
        string tagline,
        string updated,
        bool isInstalled,
        string license = "MIT",
        string size = "1.8 MB",
        string sha256Short = "—",
        string? description = null)
    {
        Id = id;
        Name = name;
        Category = category;
        Author = author;
        Version = version;
        Target = target;
        Downloads = downloads;
        Verified = verified;
        Tagline = tagline;
        Updated = updated;
        License = license;
        Size = size;
        Sha256Short = sha256Short;
        Description = description ?? tagline;
        _isInstalled = isInstalled;
    }
}
