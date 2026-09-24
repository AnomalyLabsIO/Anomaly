using Anomaly.Client.Internal.Modding;
using Anomaly.Shared.Auth;
using MelonLoader;

namespace Anomaly.Client.Auth;

internal static class ClientModInventoryCollector
{
    internal static ClientModInventory Capture()
    {
        var core = Core.Instance ?? throw new InvalidOperationException("Anomaly is not initialized.");
        var entries = new List<ClientModEntry>
        {
            new(ClientModInventory.CoreId, core.Info?.Version)
        };
        var ids = new HashSet<string>(StringComparer.Ordinal) { ClientModInventory.CoreId };

        foreach (MelonBase melon in MelonMod.RegisteredMelons)
        {
            if (melon is not MelonMod || ReferenceEquals(melon, core) || !melon.Registered)
                continue;

            string id = string.IsNullOrWhiteSpace(melon.ID) ? melon.Info?.Name : melon.ID;
            Add(entries, ids, id, melon.Info?.Version);
        }

        foreach (ClientModEntry module in ManagedModRuntime.GetActiveModEntries())
            Add(entries, ids, module.Id, module.Version);

        var inventory = new ClientModInventory(entries);
        inventory.Encode(); // Check the complete wire size before starting the login request.
        return inventory;
    }

    private static void Add(List<ClientModEntry> entries, HashSet<string> ids, string id, string version)
    {
        var entry = new ClientModEntry(id, version);
        if (!ids.Add(entry.Id))
            throw new InvalidOperationException($"More than one loaded client mod reports the ID '{entry.Id}'.");
        entries.Add(entry);
    }
}
