using Anomaly.Launcher.Models.Steam;
using Anomaly.Launcher.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anomaly.Launcher.ViewModels;

/// <summary>
/// One row in the Game Versions tab. Wraps a <see cref="SteamBuildCatalogEntry"/> plus the
/// "currently installed" flag derived from whatever vanilla build the launcher is pointing at.
/// </summary>
public partial class GameVersionItemViewModel : ObservableObject
{
    public SteamBuildCatalogEntry Entry { get; }

    [ObservableProperty]
    private bool _isInstalled;

    public ulong ManifestId => Entry.ManifestId;
    public string ManifestIdText => Entry.ManifestId.ToString();
    public string? BuildId => Entry.BuildId;
    public string? GameVersion => Entry.GameVersion;
    public string VersionLabel => Entry.GameVersion
                                  ?? Entry.VersionLabel
                                  ?? Tr.Get(TrKeys.InfoPanel_Unknown);
    public string? Notes => Entry.Notes;

    public string ReleasedAtText => Entry.ReleasedAt.HasValue
        ? Entry.ReleasedAt.Value.LocalDateTime.ToString("yyyy-MM-dd")
        : "—";

    public SupportStatus Status => Entry.Status;

    public string StatusLabel => Status switch
    {
        SupportStatus.FullySupported => Tr.Get(TrKeys.SteamDl_Status_Supported),
        SupportStatus.Patchable => Tr.Get(TrKeys.SteamDl_Status_Patchable),
        SupportStatus.Unsupported => Tr.Get(TrKeys.SteamDl_Status_Unsupported),
        _ => Tr.Get(TrKeys.SteamDl_Status_Unknown),
    };

    public string StatusChipClass => Status switch
    {
        SupportStatus.FullySupported => "ok",
        SupportStatus.Patchable => "warn",
        SupportStatus.Unsupported => "err",
        _ => "dim",
    };

    public GameVersionItemViewModel(SteamBuildCatalogEntry entry, bool isInstalled)
    {
        Entry = entry;
        _isInstalled = isInstalled;
    }
}
