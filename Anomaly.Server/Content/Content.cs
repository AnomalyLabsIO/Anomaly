using InventorySystem;
using InventorySystem.Items;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Author-facing entry point for the Anomaly custom-content framework. A LabAPI server
    /// plugin references <c>Anomaly.Server.dll</c> + <c>Anomaly.Shared.dll</c> and calls
    /// <see cref="Register(CustomItemDefinition)"/> et al. from its <c>Enable()</c> to declare
    /// custom content, then hands items out with <see cref="GiveItem"/>. Role and effect
    /// activation are intentionally disabled in v1.
    /// Registration is collected eagerly and frozen once before the first spawn, so it does
    /// not depend on LabAPI plugin load order.
    /// </summary>
    public static class Content
    {
        public static void Register(CustomItemDefinition definition) => ContentRegistry.Add(definition);
        public static void Register(CustomRoleDefinition definition) => ContentRegistry.Add(definition);
        public static void Register(CustomEffectDefinition definition) => ContentRegistry.Add(definition);

        /// <summary>
        /// Grants the custom item with the given key to a player. Gated: succeeds only once the
        /// player has acknowledged the catalog (so the client can actually render it). Returns
        /// false and logs otherwise.
        /// </summary>
        public static bool GiveItem(Player player, string key)
        {
            var hub = player?.ReferenceHub;
            if (hub == null)
                return false;
            if (!ContentRegistry.TryGetId(key, out int id) || !ContentRegistry.IsCustomItemId(id))
            {
                Logger.Warn($"Anomaly.Content: GiveItem unknown key '{key}'.");
                return false;
            }
            if (!ContentGrantGate.CanGrant(hub.netId, key))
            {
                Logger.Warn($"Anomaly.Content: '{player.Nickname}' has not acknowledged '{key}'; GiveItem skipped.");
                return false;
            }
            hub.inventory.ServerAddItem((ItemType)id, ItemAddReason.AdminCommand);
            return true;
        }

        /// <summary>
        /// Custom roles are not a safe v1 activation path and are rejected fail-closed.
        /// </summary>
        public static bool SetRole(Player player, string key)
        {
            Logger.Warn(
                $"Anomaly.Content: SetRole('{key}') rejected; custom roles are disabled in the v1 items-only profile.");
            return false;
        }
    }
}
