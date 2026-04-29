using Anomaly.Launcher.Models;
using Newtonsoft.Json;

namespace Anomaly.Launcher.Services.Install;

/// <summary>
/// Reads and writes <see cref="InstalledMods"/> on disk. Keep IO synchronous — the file is
/// kilobytes at most and we never write from a hot path.
/// </summary>
public sealed class InstalledModsService
{
    private readonly PathService _paths;
    private readonly object _gate = new();

    public InstalledModsService(PathService paths)
    {
        _paths = paths;
    }

    public InstalledMods Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_paths.InstalledModsFile)) return new InstalledMods();
                var json = File.ReadAllText(_paths.InstalledModsFile);
                return JsonConvert.DeserializeObject<InstalledMods>(json) ?? new InstalledMods();
            }
            catch
            {
                return new InstalledMods();
            }
        }
    }

    public void Save(InstalledMods state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.AnomalyDataDir);
            var tmp = _paths.InstalledModsFile + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state, Formatting.Indented));
            if (File.Exists(_paths.InstalledModsFile))
                File.Replace(tmp, _paths.InstalledModsFile, null);
            else
                File.Move(tmp, _paths.InstalledModsFile);
        }
    }

    public InstalledMod? Find(string id)
    {
        return Load().Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Upsert(InstalledMod entry)
    {
        var state = Load();
        state.Mods.RemoveAll(m => string.Equals(m.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        state.Mods.Add(entry);
        Save(state);
    }

    public bool Remove(string id)
    {
        var state = Load();
        var removed = state.Mods.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save(state);
        return removed > 0;
    }
}
