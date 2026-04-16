using Anomaly.Launcher.Models;
using Anomaly.Launcher.Services;
using Anomaly.Launcher.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anomaly.Launcher.ViewModels;

public partial class ModItemViewModel : ObservableObject
{
    private readonly ModManagerService _modManager;
    private readonly ModInfo _mod;

    [ObservableProperty]
    private bool _isActive;

    public string DisplayName => _mod.AssemblyName ?? Path.GetFileNameWithoutExtension(
        _mod.FileName.Replace(".DISABLED", "", StringComparison.OrdinalIgnoreCase));

    public string VersionText => _mod.Version ?? Tr.Get(TrKeys.ModItem_UnknownVersion);

    public string? TargetGameVersion => _mod.TargetGameVersion;

    public bool IsVersionMismatch { get; }

    public string? VersionWarningText { get; }

    /// <summary>
    /// True for core mods (e.g., Anomaly.Client) whose checkbox should be disabled when active.
    /// </summary>
    public bool IsCoreMod { get; }

    public bool CanToggle => !IsCoreMod || !IsActive;

    public ModItemViewModel(ModInfo mod, ModManagerService modManager, string? currentGameVersion)
    {
        _mod = mod;
        _modManager = modManager;
        _isActive = mod.IsActive;

        IsCoreMod = string.Equals(mod.AssemblyName, "Anomaly.Client", StringComparison.OrdinalIgnoreCase);

        if (mod.TargetGameVersion != null && currentGameVersion != null
            && mod.TargetGameVersion != currentGameVersion)
        {
            IsVersionMismatch = true;
            VersionWarningText = Tr.Get(TrKeys.ModItem_VersionWarning, mod.TargetGameVersion, currentGameVersion);
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        try
        {
            _modManager.ToggleMod(_mod);
        }
        catch
        {
            // Revert on failure
            SetProperty(ref _isActive, !value, nameof(IsActive));
        }
    }
}
