using System;
using Anomaly.Shared.Content;
using InventorySystem;
using InventorySystem.Items;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Server-side materializer: clones a vanilla item template into
    /// <see cref="InventoryItemLoader.AvailableItems"/> under the custom id so the server's
    /// inventory/pickup machinery (ServerAddItem / ServerCreatePickup) resolves the custom item.
    /// </summary>
    /// <remarks>
    /// RUNTIME-VERIFY: the Unity template/clone semantics (instantiate-as-template,
    /// DontDestroyOnLoad, inactive) compile against the publicized assembly but must be validated
    /// in a real client↔server run.
    /// <para>
    /// PILOT SCOPE: the custom item shares its base template's pickup prefab, so a dropped custom
    /// item renders as the base item in the world. Custom held/third-person visuals + icon + name
    /// are applied client-side (no networked prefab). A distinct WORLD-pickup model needs a cloned
    /// pickup prefab with a runtime-assigned Mirror assetId — and <see cref="NetworkIdentity.assetId"/>
    /// is read-only, so that requires reflection on the backing field (<c>m_AssetId</c>) plus the
    /// "assetId symmetry" spike. Deferred past the pilot; <see cref="ContentAssetId"/> is in place for it.
    /// Weight is inherited from the base template (a cloned concrete ItemBase can't override the
    /// abstract Weight getter); custom weight needs the Strategy-B subclass path.
    /// </para>
    /// </remarks>
    internal static class ServerContentRegistrar
    {
        internal static void RegisterItems()
        {
            foreach (var entry in ContentRegistry.ItemEntries)
            {
                try
                {
                    RegisterItem(entry);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Anomaly.Content: server item register '{entry.Key}' failed: {ex}");
                }
            }
        }

        private static void RegisterItem(CatalogEntry entry)
        {
            var available = InventoryItemLoader.AvailableItems;
            if (!available.TryGetValue((ItemType)entry.BaseTemplateId, out var baseTemplate) || baseTemplate == null)
            {
                Logger.Warn($"Anomaly.Content: base item template {entry.BaseTemplateId} for '{entry.Key}' not found; skipping.");
                return;
            }

            var cloneGo = UnityEngine.Object.Instantiate(baseTemplate.gameObject);
            UnityEngine.Object.DontDestroyOnLoad(cloneGo);
            cloneGo.SetActive(false); // a template, not a live item
            cloneGo.name = $"AnomalyItem_{entry.AssignedId}_{entry.Key}";

            var cloneItem = cloneGo.GetComponent<ItemBase>();
            cloneItem.ItemTypeId = (ItemType)entry.AssignedId;

            available[(ItemType)entry.AssignedId] = cloneItem;
            Logger.Info($"Anomaly.Content: server registered item '{entry.Key}' as id {entry.AssignedId} (cloned from {entry.BaseTemplateId}).");
        }
    }
}
