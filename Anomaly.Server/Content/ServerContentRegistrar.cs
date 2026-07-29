using System;
using System.Collections.Generic;
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
        private sealed class Registration
        {
            public ItemBase Template;
            public ItemBase PreviousTemplate;
            public bool HadPreviousTemplate;
        }

        private static readonly Dictionary<ItemType, Registration> Registrations =
            new Dictionary<ItemType, Registration>();

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

        internal static void Reset()
        {
            var available = InventoryItemLoader.AvailableItems;
            foreach (var registration in Registrations)
            {
                if (available.TryGetValue(registration.Key, out var current) &&
                    ReferenceEquals(current, registration.Value.Template))
                {
                    if (registration.Value.HadPreviousTemplate)
                        available[registration.Key] = registration.Value.PreviousTemplate;
                    else
                        available.Remove(registration.Key);
                }

                if (registration.Value.Template != null)
                    UnityEngine.Object.Destroy(registration.Value.Template.gameObject);
            }

            Registrations.Clear();
        }

        private static void RegisterItem(CatalogEntry entry)
        {
            var available = InventoryItemLoader.AvailableItems;
            var itemType = (ItemType)entry.AssignedId;
            if (Registrations.TryGetValue(itemType, out var existing))
            {
                available[itemType] = existing.Template;
                return;
            }

            if (!available.TryGetValue((ItemType)entry.BaseTemplateId, out var baseTemplate) || baseTemplate == null)
            {
                Logger.Warn($"Anomaly.Content: base item template {entry.BaseTemplateId} for '{entry.Key}' not found; skipping.");
                return;
            }

            bool hadPrevious = available.TryGetValue(itemType, out var previousTemplate);
            var cloneGo = UnityEngine.Object.Instantiate(baseTemplate.gameObject);
            UnityEngine.Object.DontDestroyOnLoad(cloneGo);
            cloneGo.SetActive(false); // a template, not a live item
            cloneGo.name = $"AnomalyItem_{entry.AssignedId}_{entry.Key}";

            var cloneItem = cloneGo.GetComponent<ItemBase>();
            cloneItem.ItemTypeId = itemType;

            available[itemType] = cloneItem;
            Registrations[itemType] = new Registration
            {
                Template = cloneItem,
                PreviousTemplate = previousTemplate,
                HadPreviousTemplate = hadPrevious,
            };
            Logger.Info($"Anomaly.Content: server registered item '{entry.Key}' as id {entry.AssignedId} (cloned from {entry.BaseTemplateId}).");
        }
    }
}
