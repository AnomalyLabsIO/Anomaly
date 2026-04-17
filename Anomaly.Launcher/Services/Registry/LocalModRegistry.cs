namespace Anomaly.Launcher.Services.Registry;

/// <summary>
/// Wraps <see cref="ModManagerService"/>'s filesystem scan as an <see cref="IModRegistry"/>.
/// Default registry for v1.0 — everything the user sees is whatever's in their Mods/ folder.
/// </summary>
public sealed class LocalModRegistry : IModRegistry
{
    private readonly ModManagerService _mods;

    public LocalModRegistry(ModManagerService mods)
    {
        _mods = mods;
    }

    public string Name => "Local";
    public bool IsOnline => false;

    public Task<IReadOnlyList<ModRegistryEntry>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ModRegistryEntry> entries = _mods.GetMods()
            .Select(m => new ModRegistryEntry(
                Id: m.AssemblyName ?? m.FileName,
                DisplayName: m.AssemblyName ?? m.FileName,
                Version: m.Version,
                TargetGameVersion: m.TargetGameVersion,
                DownloadUrl: null,
                IsInstalled: true))
            .ToList();

        return Task.FromResult(entries);
    }
}
