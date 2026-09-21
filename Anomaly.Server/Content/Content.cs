using System;
using InventorySystem;
using InventorySystem.Items;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Author-facing entry point for the Anomaly custom-content framework. A LabAPI server
    /// plugin references <c>Anomaly.Server.dll</c> + <c>Anomaly.Shared.dll</c> and calls
    /// <see cref="Register(CustomItemDefinition)"/> from its <c>Enable()</c> to declare
    /// custom content, then hands items out with <see cref="GiveItem"/>.
    /// Registration is collected eagerly and frozen once before the first spawn, so it does
    /// not depend on LabAPI plugin load order.
    /// </summary>
    public static class Content
    {
        public static event Action<Player> PlayerReady;

        public static void Register(CustomItemDefinition definition) => ContentRegistry.Add(definition);

        public static bool TryGetItemId(string key, out ItemType itemType)
        {
            if (ContentRegistry.TryGetId(key, out int id) && ContentRegistry.IsCustomItemId(id))
            {
                itemType = (ItemType)id;
                return true;
            }
            itemType = ItemType.None;
            return false;
        }

        public static bool TryGetItemKey(ItemType itemType, out string key) =>
            ContentRegistry.TryGetKey((int)itemType, out key);

        public static bool IsCustomItem(ItemType itemType) =>
            ContentRegistry.IsCustomItemId((int)itemType);

        public static ContentReadiness GetPlayerReadiness(Player player)
        {
            var hub = player?.ReferenceHub;
            return hub == null ? ContentReadiness.Unknown : ContentGrantGate.GetReadiness(hub.netId);
        }

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
            if (!ContentRegistry.TryGetId(key, out int id) ||
                !ContentRegistry.IsCustomItemId(id) ||
                !ServerContentRegistrar.IsRegistered(id))
            {
                Logger.Warn($"Anomaly.Content: GiveItem unknown key '{key}'.");
                return false;
            }
            if (!ContentGrantGate.CanGrant(hub.netId, key))
            {
                Logger.Warn($"Anomaly.Content: '{player.Nickname}' has not acknowledged '{key}'; GiveItem skipped.");
                return false;
            }
            return hub.inventory.ServerAddItem((ItemType)id, ItemAddReason.AdminCommand) != null;
        }

        /// <summary>
        /// Raises the once-per-catalog activation event after the grant gate commits.
        /// </summary>
        internal static bool RaisePlayerReady(ReferenceHub hub)
        {
            Player player;
            try { player = Player.Get(hub); }
            catch { return false; }
            if (player == null)
                return false;
            Delegate[] subscribers = PlayerReady?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (Delegate subscriber in subscribers)
            {
                try { ((Action<Player>)subscriber)(player); }
                catch (Exception exception)
                {
                    try { Logger.Error($"Anomaly.Content.PlayerReady subscriber failed: {exception}"); }
                    catch { /* logging must not suppress later subscribers */ }
                }
            }
            return true;
        }
    }
}
