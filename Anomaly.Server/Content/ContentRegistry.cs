using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Server.Assets;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using InventorySystem;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>Order-independent registration and frozen items-only session catalog.</summary>
    internal static class ContentRegistry
    {
        private const int ItemIdCeiling = 127;

        private static readonly Dictionary<string, CustomItemDefinition> Items =
            new Dictionary<string, CustomItemDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> KeyToId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<int, CatalogEntry> ItemById =
            new Dictionary<int, CatalogEntry>();
        private static readonly Dictionary<int, ItemLogicBase> ItemLogic =
            new Dictionary<int, ItemLogicBase>();
        private static string _registrationError;

        public static bool Frozen { get; private set; }
        public static bool Materialized { get; private set; }
        public static ContentCatalog Catalog { get; private set; } = EmptyCatalog();

        internal static void Add(CustomItemDefinition definition)
        {
            if (definition == null)
            {
                RejectRegistration("a null custom item definition was registered");
                return;
            }
            if (Frozen)
            {
                Logger.Warn($"Anomaly.Content: cannot register item '{definition.Key}' after the catalog is frozen.");
                return;
            }
            if (!ModNamespace.IsValidContentKey(definition.Key))
            {
                RejectRegistration($"invalid item key '{definition.Key}' (expected 'modid:name', lowercase)");
                return;
            }
            if (Items.ContainsKey(definition.Key))
            {
                RejectRegistration($"duplicate item key '{definition.Key}'");
                return;
            }
            Items.Add(definition.Key, definition);
        }

        internal static bool Freeze()
        {
            if (Frozen)
                return Materialized;

            Frozen = true;
            try
            {
                if (!string.IsNullOrEmpty(_registrationError))
                    throw new InvalidOperationException(_registrationError);
                var entries = Items.Values.Select(BuildItemEntry).ToList();
                var availableItems = InventoryItemLoader.AvailableItems;
                if (availableItems == null)
                    throw new InvalidOperationException("InventoryItemLoader.AvailableItems is unavailable.");
                int itemBase = CatalogBuilder.ComputeIdBase(MaxEnumValue<ItemType>());
                Catalog = CatalogBuilder.Build(
                    entries,
                    itemBase,
                    ItemIdCeiling,
                    ServerAssetManager.ComputeCurrentManifestSha256());
                ContentCatalogMessage.ValidateCatalog(Catalog, validateDeclaredHash: true);

                if (!ServerContentRegistrar.RegisterItems(Catalog.Entries))
                    throw new InvalidOperationException("One or more server item templates could not be registered.");

                foreach (CatalogEntry entry in Catalog.Entries)
                {
                    KeyToId.Add(entry.Key, entry.AssignedId);
                    ItemById.Add(entry.AssignedId, entry);
                    if (Items.TryGetValue(entry.Key, out CustomItemDefinition definition) && definition.Logic != null)
                        ItemLogic.Add(entry.AssignedId, definition.Logic);
                }

                Materialized = true;
                SafeLogInfo(
                    $"Anomaly.Content: items-only catalog frozen with {Catalog.Entries.Count} item(s), " +
                    $"catalog={ToHex(Catalog.CatalogSha256)}, manifest={ToHex(Catalog.ManifestSha256)}.");
                return true;
            }
            catch (Exception exception)
            {
                // Revoke published state before diagnostics. Logger failures
                // must never strand a partially active catalog.
                bool registrarClean = ServerContentRegistrar.Reset();
                KeyToId.Clear();
                ItemById.Clear();
                ItemLogic.Clear();
                Catalog = EmptyCatalog();
                Materialized = false;
                SafeLogError(
                    $"Anomaly.Content: transactional catalog freeze failed; custom content is disabled " +
                    $"(registrarClean={registrarClean}): {exception}");
                return false;
            }
        }

        internal static bool TryGetId(string key, out int id) => KeyToId.TryGetValue(key, out id);

        internal static bool TryGetKey(int id, out string key)
        {
            if (ItemById.TryGetValue(id, out CatalogEntry entry))
            {
                key = entry.Key;
                return true;
            }
            key = null;
            return false;
        }

        internal static bool TryGetItemEntry(int id, out CatalogEntry entry) => ItemById.TryGetValue(id, out entry);
        internal static bool TryGetItemLogic(int id, out ItemLogicBase logic) => ItemLogic.TryGetValue(id, out logic);
        internal static bool IsCustomItemId(int id) => ItemById.ContainsKey(id);
        internal static IEnumerable<CatalogEntry> ItemEntries => ItemById.Values;

        /// <summary>
        /// Rebinds immutable item definitions and assigned ids to the exact file
        /// snapshot offered to a future connection. Active connection catalogs
        /// are never mutated by file-watcher changes.
        /// </summary>
        internal static ContentCatalog CreateOfferCatalog(byte[] manifestSha256)
        {
            if (!Freeze())
                throw new InvalidOperationException("The server item catalog is not materialized.");

            var entries = new List<CatalogEntry>();
            foreach (CustomItemDefinition definition in Items.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                CatalogEntry current = BuildItemEntry(definition);
                if (!KeyToId.TryGetValue(current.Key, out current.AssignedId))
                    throw new InvalidOperationException($"Item '{current.Key}' has no frozen assigned id.");
                entries.Add(current);
            }

            var offer = new ContentCatalog
            {
                ManifestSha256 = (byte[])manifestSha256.Clone(),
                Entries = entries,
            };
            offer.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(offer);
            ContentCatalogMessage.ValidateCatalog(offer, validateDeclaredHash: true);
            return offer;
        }

        internal static void Reset()
        {
            bool registrarClean = false;
            try
            {
                registrarClean = ServerContentRegistrar.Reset();
            }
            finally
            {
                // Logical state is revoked even when a Unity object remains
                // pending best-effort cleanup in the registrar.
                Items.Clear();
                KeyToId.Clear();
                ItemById.Clear();
                ItemLogic.Clear();
                Catalog = EmptyCatalog();
                Frozen = false;
                Materialized = false;
                _registrationError = null;
            }

            if (!registrarClean)
                throw new InvalidOperationException(
                    "One or more server item templates remain pending cleanup; a clean reload is blocked.");
        }

        private static void RejectRegistration(string reason)
        {
            _registrationError ??= reason;
            SafeLogError($"Anomaly.Content: catalog registration rejected: {reason}. The complete catalog will fail closed.");
        }

        private static void SafeLogInfo(string message)
        {
            try { Logger.Info(message); }
            catch { }
        }

        private static void SafeLogError(string message)
        {
            try { Logger.Error(message); }
            catch { }
        }

        private static CatalogEntry BuildItemEntry(CustomItemDefinition definition)
        {
            var entry = new CatalogEntry
            {
                Key = definition.Key,
                BaseTemplateId = (int)definition.BaseTemplate,
                DisplayName = definition.DisplayName ?? string.Empty,
                Description = definition.Description ?? string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(definition.IconPath))
            {
                byte[] hash = AssetRegistry.GetHashByPath(definition.IconPath);
                if (hash == null || !ServerAssetManager.TryGetAsset(hash, out ServerAssetManager.AssetInfo asset) ||
                    asset.Kind != AssetKind.Texture2D ||
                    !string.Equals(Path.GetExtension(asset.RelativePath), ".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Item '{definition.Key}' icon '{definition.IconPath}' is missing or is not a PNG.");
                entry.IconHash = (byte[])hash.Clone();
                entry.IconSize = asset.Size;
            }

            return entry;
        }

        private static ContentCatalog EmptyCatalog()
        {
            byte[] manifest = FileManifestMessage.ComputeManifestSha256(
                Array.Empty<FileManifestMessage.FileEntry>());
            return CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 0, ItemIdCeiling, manifest);
        }

        private static int MaxEnumValue<T>() where T : struct, Enum
        {
            int max = int.MinValue;
            foreach (T value in (T[])Enum.GetValues(typeof(T)))
                max = Math.Max(max, Convert.ToInt32(value));
            return max;
        }

        private static string ToHex(byte[] hash) =>
            BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
