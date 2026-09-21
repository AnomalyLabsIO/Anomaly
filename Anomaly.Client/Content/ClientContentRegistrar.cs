using Anomaly.Client.Api.Assets;
using Anomaly.Shared.Content;
using Il2Cpp;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Content;

/// <summary>Stages stock-template clones and publishes the complete catalog atomically.</summary>
internal static class ClientContentRegistrar
{
    private sealed class StagedItem
    {
        internal CatalogEntry Entry;
        internal ItemBase Clone;
        internal bool AvailablePublished;
        internal bool ActiveItemPublished;
        internal bool TranslationPublished;
    }

    private static readonly Dictionary<int, ItemBase> ActiveItems = new();
    private static readonly Dictionary<int, ItemTranslationReader.NameDescriptionPair> ActiveTranslations = new();
    private static readonly List<StagedItem> ActiveRegistrations = new();
    private static readonly List<StagedItem> PendingCleanup = new();

    internal static bool TryActivate(ContentCatalog catalog, out List<string> failedKeys, out string error)
    {
        failedKeys = new List<string>();
        error = string.Empty;
        var staged = new List<StagedItem>();

        try
        {
            if (ActiveItems.Count != 0 || ActiveRegistrations.Count != 0 || PendingCleanup.Count != 0)
                throw new InvalidOperationException(
                    "A content catalog is already active or a previous catalog still owns pending cleanup.");

            var available = InventoryItemLoader.AvailableItems;
            if (available == null)
                throw new InvalidOperationException("InventoryItemLoader is not ready.");

            int maximumDefinedId = int.MinValue;
            foreach (ItemType definedType in Enum.GetValues<ItemType>())
                maximumDefinedId = Math.Max(maximumDefinedId, (int)definedType);
            int expectedAssignedId = CatalogBuilder.ComputeIdBase(maximumDefinedId);

            foreach (CatalogEntry entry in catalog.Entries)
            {
                if (entry.AssignedId != expectedAssignedId || expectedAssignedId > 127)
                    throw new InvalidDataException(
                        $"Assigned item id {entry.AssignedId} was not the next canonical id {expectedAssignedId}.");
                expectedAssignedId++;
                var assignedType = (ItemType)entry.AssignedId;
                var baseType = (ItemType)entry.BaseTemplateId;
                if (available.ContainsKey(assignedType))
                    throw new InvalidDataException($"Assigned item id {entry.AssignedId} collides with an existing template.");
                if (!available.TryGetValue(baseType, out ItemBase template) || template == null)
                    throw new InvalidDataException($"Base item id {entry.BaseTemplateId} is unavailable for '{entry.Key}'.");

                Texture2D icon = null;
                if (entry.IconHash != null &&
                    !AnomalyResources.TryGetTexture(entry.IconHash, out icon))
                    throw new InvalidDataException($"Approved PNG icon could not be decoded for '{entry.Key}'.");

                ItemBase clone = UnityEngine.Object.Instantiate(template);
                if (clone == null)
                    throw new InvalidOperationException($"Stock template clone failed for '{entry.Key}'.");
                var stagedItem = new StagedItem { Entry = entry, Clone = clone };
                staged.Add(stagedItem);
                clone.ItemTypeId = assignedType;
                if (icon != null)
                    clone.Icon = icon;
                clone.name = $"Anomaly Item [{entry.Key}]";
            }

            // Nothing enters the global loader or translation path until every clone,
            // icon, text entry, and collision check above has succeeded.
            foreach (StagedItem item in staged)
            {
                var assignedType = (ItemType)item.Entry.AssignedId;
                available.Add(assignedType, item.Clone);
                item.AvailablePublished = true;
                ActiveItems.Add(item.Entry.AssignedId, item.Clone);
                item.ActiveItemPublished = true;
                ActiveTranslations.Add(item.Entry.AssignedId, new ItemTranslationReader.NameDescriptionPair
                {
                    Name = item.Entry.DisplayName,
                    Nickname = item.Entry.DisplayName,
                    Description = item.Entry.Description ?? string.Empty,
                });
                item.TranslationPublished = true;
            }

            ActiveRegistrations.AddRange(staged);
            try { MelonLogger.Msg($"Activated {ActiveItems.Count} custom item templates transactionally."); }
            catch { }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            foreach (CatalogEntry entry in catalog.Entries)
                failedKeys.Add(entry.Key);

            RollBack(staged);
            return false;
        }
    }

    internal static bool TryGetTranslation(
        ItemType itemType,
        out ItemTranslationReader.NameDescriptionPair translation) =>
        ActiveTranslations.TryGetValue((int)itemType, out translation);

    internal static void Reset()
    {
        RollBack(ActiveRegistrations);
        if (PendingCleanup.Count != 0)
            throw new InvalidOperationException(
                "One or more client item templates remain pending cleanup; a clean reconnect is blocked.");
    }

    private static void RollBack(IList<StagedItem> registrations)
    {
        var cleanup = new List<StagedItem>(PendingCleanup);
        PendingCleanup.Clear();
        foreach (StagedItem registration in registrations)
        {
            if (!cleanup.Contains(registration))
                cleanup.Add(registration);
        }
        registrations.Clear();

        // Revoke public/logical lookup state before touching Unity objects.
        ActiveItems.Clear();
        ActiveTranslations.Clear();

        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            StagedItem item = cleanup[index];
            int assignedId = item.Entry.AssignedId;
            var itemType = (ItemType)assignedId;
            if (item.TranslationPublished)
            {
                try { ActiveTranslations.Remove(assignedId); }
                catch { }
                item.TranslationPublished = false;
            }
            if (item.ActiveItemPublished)
            {
                try
                {
                    if (ActiveItems.TryGetValue(assignedId, out ItemBase active) &&
                        ReferenceEquals(active, item.Clone))
                        ActiveItems.Remove(assignedId);
                }
                catch { }
                item.ActiveItemPublished = false;
            }

            bool loaderStillOwnsClone = false;
            if (item.AvailablePublished)
            {
                try
                {
                    var available = InventoryItemLoader.AvailableItems;
                    if (available != null &&
                        available.TryGetValue(itemType, out ItemBase current) &&
                        ReferenceEquals(current, item.Clone))
                        available.Remove(itemType);
                    loaderStillOwnsClone = available != null &&
                        available.TryGetValue(itemType, out current) &&
                        ReferenceEquals(current, item.Clone);
                }
                catch { loaderStillOwnsClone = true; }
                item.AvailablePublished = loaderStillOwnsClone;
            }

            bool objectReleased = item.Clone == null;
            if (!loaderStillOwnsClone && !objectReleased)
            {
                try
                {
                    UnityEngine.Object.Destroy(item.Clone.gameObject);
                    objectReleased = true;
                }
                catch { }
            }

            if (loaderStillOwnsClone || !objectReleased)
                PendingCleanup.Add(item);
        }

        ActiveRegistrations.Clear();
    }
}
