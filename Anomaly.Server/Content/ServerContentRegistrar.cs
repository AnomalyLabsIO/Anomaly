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
    /// DontDestroyOnLoad, inactive) compile against the publicized assembly but still require a
    /// real client/server run. Public v1 deliberately inherits the stock template's held model,
    /// pickup model, and weight; custom models, custom weight, AssetBundles, and generic spawning
    /// are outside the contract. The client changes only the item text and optional PNG icon.
    /// </remarks>
    internal static class ServerContentRegistrar
    {
        private sealed class Registration
        {
            public GameObject CloneObject;
            public ItemBase Template;
            public ItemBase PreviousTemplate;
            public bool HadPreviousTemplate;
        }

        private sealed class StagedRegistration
        {
            public CatalogEntry Entry;
            public ItemType ItemType;
            public GameObject CloneObject;
            public ItemBase Template;
            public ItemBase PreviousTemplate;
            public bool HadPreviousTemplate;
            public bool AvailablePublished;
            public bool RegistrationPublished;
        }

        private static readonly Dictionary<ItemType, Registration> Registrations =
            new Dictionary<ItemType, Registration>();
        private static readonly Dictionary<ItemType, Registration> PendingCleanup =
            new Dictionary<ItemType, Registration>();

        internal static bool IsRegistered(int itemId) =>
            Registrations.ContainsKey((ItemType)itemId);

        internal static bool RegisterItems(IEnumerable<CatalogEntry> entries)
        {
            if (entries == null)
                return false;

            var definitions = new List<CatalogEntry>(entries);
            var staged = new List<StagedRegistration>();
            try
            {
                if (Registrations.Count != 0 || PendingCleanup.Count != 0)
                    throw new InvalidOperationException(
                        "A content catalog is already active or a previous catalog still owns pending cleanup.");

                ValidateAll(definitions);
                StageAll(definitions, staged);
                PublishAll(staged);
                return true;
            }
            catch (Exception ex)
            {
                RollBack(staged);
                try { Logger.Error($"Anomaly.Content: transactional server item registration failed: {ex}"); }
                catch { }
                return false;
            }
        }

        internal static bool Reset()
        {
            var cleanup = new Dictionary<ItemType, Registration>(PendingCleanup);
            foreach (var registration in Registrations)
                cleanup[registration.Key] = registration.Value;

            // Logical registration is revoked first. PendingCleanup is private
            // ownership bookkeeping only and never makes IsRegistered true.
            Registrations.Clear();
            PendingCleanup.Clear();

            foreach (var registration in cleanup)
            {
                bool loaderReleased = false;
                try
                {
                    var available = InventoryItemLoader.AvailableItems;
                    if (available == null)
                        throw new InvalidOperationException("InventoryItemLoader.AvailableItems is unavailable.");

                    if (available.TryGetValue(registration.Key, out var current) &&
                        ReferenceEquals(current, registration.Value.Template))
                    {
                        if (registration.Value.HadPreviousTemplate)
                            available[registration.Key] = registration.Value.PreviousTemplate;
                        else
                            available.Remove(registration.Key);
                    }

                    loaderReleased = !available.TryGetValue(registration.Key, out current) ||
                        !ReferenceEquals(current, registration.Value.Template);
                }
                catch (Exception exception)
                {
                    SafeLogError(
                        $"Anomaly.Content: failed to restore item template {registration.Key}: {exception}");
                }

                bool objectReleased = registration.Value.CloneObject == null &&
                    registration.Value.Template == null;
                if (loaderReleased && !objectReleased)
                {
                    try
                    {
                        GameObject cloneObject = registration.Value.CloneObject;
                        if (cloneObject == null && registration.Value.Template != null)
                            cloneObject = registration.Value.Template.gameObject;
                        if (cloneObject == null)
                            objectReleased = true;
                        else
                        {
                            UnityEngine.Object.Destroy(cloneObject);
                            objectReleased = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        SafeLogError(
                            $"Anomaly.Content: failed to destroy item template {registration.Key}: {exception}");
                    }
                }

                if (!loaderReleased || !objectReleased)
                    PendingCleanup[registration.Key] = registration.Value;
            }

            if (PendingCleanup.Count != 0)
            {
                SafeLogError(
                    $"Anomaly.Content: {PendingCleanup.Count} item template(s) remain pending cleanup; " +
                    "new catalog registration is blocked.");
                return false;
            }

            return true;
        }

        private static void SafeLogError(string message)
        {
            try { Logger.Error(message); }
            catch { }
        }

        private static void StageAll(
            IEnumerable<CatalogEntry> entries,
            ICollection<StagedRegistration> staged)
        {
            var available = InventoryItemLoader.AvailableItems;
            foreach (CatalogEntry entry in entries)
            {
                var itemType = (ItemType)entry.AssignedId;
                ItemBase baseTemplate = available[(ItemType)entry.BaseTemplateId];
                bool hadPrevious = available.TryGetValue(itemType, out var previousTemplate);

                var cloneGo = UnityEngine.Object.Instantiate(baseTemplate.gameObject);
                if (cloneGo == null)
                    throw new InvalidOperationException($"Stock template clone failed for '{entry.Key}'.");

                var stagedItem = new StagedRegistration
                {
                    Entry = entry,
                    ItemType = itemType,
                    CloneObject = cloneGo,
                    PreviousTemplate = previousTemplate,
                    HadPreviousTemplate = hadPrevious,
                };
                staged.Add(stagedItem);

                UnityEngine.Object.DontDestroyOnLoad(cloneGo);
                cloneGo.SetActive(false); // a template, not a live item
                cloneGo.name = $"AnomalyItem_{entry.AssignedId}_{entry.Key}";

                var cloneItem = cloneGo.GetComponent<ItemBase>();
                if (cloneItem == null)
                {
                    throw new InvalidOperationException(
                        $"Cloned stock template for '{entry.Key}' did not contain ItemBase.");
                }

                cloneItem.ItemTypeId = itemType;
                stagedItem.Template = cloneItem;
            }
        }

        private static void PublishAll(IEnumerable<StagedRegistration> staged)
        {
            var available = InventoryItemLoader.AvailableItems;
            foreach (StagedRegistration item in staged)
            {
                // Recheck immediately before publication in case another plugin changed the
                // global template table between validation and commit.
                if (available.ContainsKey(item.ItemType))
                    throw new InvalidOperationException(
                        $"Item '{item.Entry.Key}' collided with id {item.Entry.AssignedId} during commit.");

                available.Add(item.ItemType, item.Template);
                item.AvailablePublished = true;
                Registrations.Add(item.ItemType, new Registration
                {
                    CloneObject = item.CloneObject,
                    Template = item.Template,
                    PreviousTemplate = item.PreviousTemplate,
                    HadPreviousTemplate = item.HadPreviousTemplate,
                });
                item.RegistrationPublished = true;
            }

            foreach (StagedRegistration item in staged)
            {
                try
                {
                    Logger.Info(
                        $"Anomaly.Content: server registered item '{item.Entry.Key}' as id " +
                        $"{item.Entry.AssignedId} (cloned from {item.Entry.BaseTemplateId}).");
                }
                catch { }
            }
        }

        private static void RollBack(IEnumerable<StagedRegistration> staged)
        {
            var registrations = new List<StagedRegistration>(staged);
            for (int index = registrations.Count - 1; index >= 0; index--)
            {
                StagedRegistration item = registrations[index];
                var cleanupOwnership = new Registration
                {
                    CloneObject = item.CloneObject,
                    Template = item.Template,
                    PreviousTemplate = item.PreviousTemplate,
                    HadPreviousTemplate = item.HadPreviousTemplate,
                };
                if (item.RegistrationPublished)
                {
                    bool registrationRemoved = false;
                    try
                    {
                        if (Registrations.TryGetValue(item.ItemType, out Registration currentRegistration) &&
                            ReferenceEquals(currentRegistration.Template, item.Template))
                            registrationRemoved = Registrations.Remove(item.ItemType);
                        else
                            registrationRemoved = true;
                    }
                    catch { }
                    item.RegistrationPublished = !registrationRemoved;
                }

                bool loaderStillOwnsClone = item.AvailablePublished;
                if (item.AvailablePublished)
                {
                    try
                    {
                        var available = InventoryItemLoader.AvailableItems;
                        if (available == null)
                            throw new InvalidOperationException(
                                "InventoryItemLoader.AvailableItems is unavailable during rollback.");

                        if (available.TryGetValue(item.ItemType, out var current) &&
                            ReferenceEquals(current, item.Template))
                        {
                            if (item.HadPreviousTemplate)
                                available[item.ItemType] = item.PreviousTemplate;
                            else
                                available.Remove(item.ItemType);
                        }
                        loaderStillOwnsClone = available.TryGetValue(item.ItemType, out current) &&
                            ReferenceEquals(current, item.Template);
                    }
                    catch { loaderStillOwnsClone = true; }
                    item.AvailablePublished = loaderStillOwnsClone;
                }

                bool objectReleased = item.CloneObject == null;
                if (!loaderStillOwnsClone && !objectReleased)
                {
                    try
                    {
                        UnityEngine.Object.Destroy(item.CloneObject);
                        objectReleased = true;
                    }
                    catch { }
                }

                if (item.RegistrationPublished || loaderStillOwnsClone || !objectReleased)
                    PendingCleanup[item.ItemType] = cleanupOwnership;
            }
        }

        private static void ValidateAll(IEnumerable<CatalogEntry> entries)
        {
            var available = InventoryItemLoader.AvailableItems;
            if (available == null)
                throw new InvalidOperationException("InventoryItemLoader.AvailableItems is unavailable.");

            var assigned = new HashSet<ItemType>();
            int maximumDefinedId = int.MinValue;
            foreach (ItemType definedType in (ItemType[])Enum.GetValues(typeof(ItemType)))
                maximumDefinedId = Math.Max(maximumDefinedId, (int)definedType);
            int expectedAssignedId = CatalogBuilder.ComputeIdBase(maximumDefinedId);
            foreach (CatalogEntry entry in entries)
            {
                if (entry == null)
                    throw new InvalidOperationException("Catalog contained a null item.");
                var itemType = (ItemType)entry.AssignedId;
                if (entry.AssignedId < 0 || entry.AssignedId > 127 || !assigned.Add(itemType))
                    throw new InvalidOperationException($"Item '{entry.Key}' has an invalid or duplicate assigned id.");
                if (entry.AssignedId != expectedAssignedId)
                    throw new InvalidOperationException(
                        $"Item '{entry.Key}' id {entry.AssignedId} was not the next canonical id {expectedAssignedId}.");
                expectedAssignedId++;
                if (available.ContainsKey(itemType))
                    throw new InvalidOperationException($"Item '{entry.Key}' collides with an existing item id {entry.AssignedId}.");
                if (!available.TryGetValue((ItemType)entry.BaseTemplateId, out var baseTemplate) || baseTemplate == null)
                    throw new InvalidOperationException(
                        $"Base item template {entry.BaseTemplateId} for '{entry.Key}' was not found.");
            }
        }
    }
}
