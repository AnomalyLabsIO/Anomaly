using System;
using Anomaly.Server.Content;
using Anomaly.Server.Api.Events.Handlers;
using InventorySystem;
using ItemAddReason = InventorySystem.Items.ItemAddReason;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Content
{
    /// <summary>
    /// Author-facing entry point for the Anomaly custom-content framework. A LabAPI server
    /// plugin references <c>Anomaly.Server.dll</c> + <c>Anomaly.Shared.dll</c> and calls
    /// <see cref="Register(CustomItemDefinition)"/> from its <c>Enable()</c> to declare
    /// custom content, then hands items out with <see cref="TryGiveItem"/>.
    /// Register during your LabAPI plugin's Enable. Definitions are copied and the catalog
    /// freezes when first offered to a client; restart the server to change that catalog.
    /// </summary>
    public static class CustomItems
    {
        /// <summary>Copies a definition into the pending catalog without retaining author-owned state.</summary>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        /// <exception cref="ArgumentException">The key, vanilla base item or bounded Unicode text is invalid.</exception>
        /// <exception cref="InvalidOperationException">The key is registered or the catalog is frozen.</exception>
        public static void Register(CustomItemDefinition definition) => ContentRegistry.Add(definition);

        /// <summary>Resolves a stable content key after the catalog has materialized.</summary>
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

        /// <summary>Identifies custom items received in ordinary LabAPI inventory events.</summary>
        public static bool TryGetItemKey(ItemType itemType, out string key) =>
            ContentRegistry.TryGetKey((int)itemType, out key);

        /// <summary>Whether the frozen catalog owns the given item ID.</summary>
        public static bool IsCustomItem(ItemType itemType) =>
            ContentRegistry.IsCustomItemId((int)itemType);

        /// <summary>Returns the client's exact-catalog approval and activation state.</summary>
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
        public static bool TryGiveItem(Player player, string key, out Item item)
        {
            item = null;
            var hub = player?.ReferenceHub;
            if (hub == null || string.IsNullOrEmpty(key))
                return false;
            if (!ContentRegistry.TryGetId(key, out int id) ||
                !ContentRegistry.IsCustomItemId(id) ||
                !ServerContentRegistrar.IsRegistered(id))
            {
                try { Logger.Warn($"Anomaly.Content: TryGiveItem unknown key '{key}'."); }
                catch { }
                return false;
            }
            if (!ContentGrantGate.CanGrant(hub.netId, key))
            {
                try { Logger.Warn($"Anomaly.Content: '{player.Nickname}' has not acknowledged '{key}'; TryGiveItem skipped."); }
                catch { }
                return false;
            }
            var created = hub.inventory.ServerAddItem((ItemType)id, ItemAddReason.AdminCommand);
            item = created == null ? null : Item.Get(created);
            return item != null;
        }

        /// <summary>
        /// Raises the once-per-catalog activation event after the grant gate commits.
        /// </summary>
        internal static bool RaisePlayerReady(ReferenceHub hub)
        {
            return ClientEvents.OnContentReady(hub);
        }
    }
}
