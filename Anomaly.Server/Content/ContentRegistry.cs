using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Server.Api.Assets;
using Anomaly.Server.Api.Content;
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
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        private static readonly Dictionary<string, CustomItemDefinition> Items =
            new Dictionary<string, CustomItemDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> KeyToId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<int, CatalogEntry> ItemById =
            new Dictionary<int, CatalogEntry>();

        public static bool Frozen { get; private set; }
        public static bool Materialized { get; private set; }
        public static ContentCatalog Catalog { get; private set; } = EmptyCatalog();

        internal static void Add(CustomItemDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (Frozen)
                throw new InvalidOperationException("The content catalog is frozen. Restart the server to change registrations.");
            if (!ModNamespace.IsValidContentKey(definition.Key))
                throw new ArgumentException("Item keys must use the lowercase 'modid:name' form.", nameof(definition));
            if (Items.ContainsKey(definition.Key))
                throw new InvalidOperationException($"Custom item '{definition.Key}' is already registered.");
            if (!Enum.IsDefined(typeof(ItemType), definition.BaseTemplate) ||
                (int)definition.BaseTemplate < 0 || (int)definition.BaseTemplate > ItemIdCeiling)
                throw new ArgumentException("BaseTemplate must name a vanilla item in the supported item ID range.", nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.DisplayName))
                throw new ArgumentException("DisplayName must be non-empty.", nameof(definition));
            try
            {
                if (Utf8.GetByteCount(definition.Key) > ContentCatalogMessage.MaxKeyBytes ||
                    Utf8.GetByteCount(definition.DisplayName) > ContentCatalogMessage.MaxNameBytes ||
                    Utf8.GetByteCount(definition.Description ?? string.Empty) > ContentCatalogMessage.MaxDescriptionBytes)
                    throw new ArgumentException("The item key, display name or description exceeds the catalog's UTF-8 byte limit.", nameof(definition));
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("Item text must contain valid Unicode.", nameof(definition), exception);
            }
            Items.Add(definition.Key, new CustomItemDefinition
            {
                Key = definition.Key,
                BaseTemplate = definition.BaseTemplate,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                IconAddress = definition.IconAddress,
            });
        }

        internal static bool Freeze(AssetPackSnapshot snapshot = null)
        {
            if (Frozen)
                return Materialized;

            AssetPackSnapshot ownedSnapshot = snapshot == null ? ServerAssetManager.AcquireSnapshot() : null;
            snapshot ??= ownedSnapshot;
            Frozen = true;
            try
            {
                var entries = Items.Values.Select(item => BuildItemEntry(item, snapshot)).ToList();
                var availableItems = InventoryItemLoader.AvailableItems;
                if (availableItems == null)
                    throw new InvalidOperationException("InventoryItemLoader.AvailableItems is unavailable.");
                int itemBase = CatalogBuilder.ComputeIdBase(MaxEnumValue<ItemType>());
                Catalog = CatalogBuilder.Build(
                    entries,
                    itemBase,
                    ItemIdCeiling,
                    snapshot.BuildManifest(1).ManifestSha256,
                    snapshot.CopyPacks());
                ContentCatalogMessage.ValidateCatalog(Catalog, validateDeclaredHash: true);

                if (!ServerContentRegistrar.RegisterItems(Catalog.Entries))
                    throw new InvalidOperationException("One or more server item templates could not be registered.");

                foreach (CatalogEntry entry in Catalog.Entries)
                {
                    KeyToId.Add(entry.Key, entry.AssignedId);
                    ItemById.Add(entry.AssignedId, entry);
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
                Catalog = EmptyCatalog();
                Materialized = false;
                SafeLogError(
                    $"Anomaly.Content: transactional catalog freeze failed; custom content is disabled " +
                    $"(registrarClean={registrarClean}): {exception}");
                return false;
            }
            finally { ownedSnapshot?.Dispose(); }
        }

        internal static bool TryGetId(string key, out int id)
        {
            id = 0;
            return key != null && KeyToId.TryGetValue(key, out id);
        }

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
        internal static bool IsCustomItemId(int id) => ItemById.ContainsKey(id);
        internal static IEnumerable<CatalogEntry> ItemEntries => ItemById.Values;

        /// <summary>
        /// Rebinds immutable item definitions and assigned ids to the exact file
        /// snapshot offered to a future connection. Active connection catalogs
        /// are never mutated by file-watcher changes.
        /// </summary>
        internal static ContentCatalog CreateOfferCatalog(byte[] manifestSha256, AssetPackSnapshot snapshot)
        {
            if (!Freeze(snapshot))
                throw new InvalidOperationException("The server item catalog is not materialized.");

            var entries = new List<CatalogEntry>();
            foreach (CustomItemDefinition definition in Items.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                CatalogEntry current = BuildItemEntry(definition, snapshot);
                if (!KeyToId.TryGetValue(current.Key, out current.AssignedId))
                    throw new InvalidOperationException($"Item '{current.Key}' has no frozen assigned id.");
                entries.Add(current);
            }

            var offer = new ContentCatalog
            {
                ManifestSha256 = (byte[])manifestSha256.Clone(),
                Entries = entries,
                Packs = snapshot.CopyPacks(),
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
                Catalog = EmptyCatalog();
                Frozen = false;
                Materialized = false;
            }

            if (!registrarClean)
                throw new InvalidOperationException(
                    "One or more server item templates remain pending cleanup; a clean reload is blocked.");
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

        private static CatalogEntry BuildItemEntry(CustomItemDefinition definition, AssetPackSnapshot snapshot = null)
        {
            var entry = new CatalogEntry
            {
                Key = definition.Key,
                BaseTemplateId = (int)definition.BaseTemplate,
                DisplayName = definition.DisplayName ?? string.Empty,
                Description = definition.Description ?? string.Empty,
            };

            if (definition.IconAddress.HasValue)
            {
                AssetAddress address = definition.IconAddress.Value;
                AssetPackSnapshot.AssetRecord asset;
                bool found = snapshot == null ? ServerAssetManager.TryGetAsset(address, out asset) : snapshot.TryGetAsset(address, out asset);
                if (!found || (asset.Kind != AssetKind.Texture2D && asset.Kind != AssetKind.Sprite))
                    throw new InvalidDataException("Item icon is missing or is not a Texture2D/Sprite: " + address);
                entry.IconAddress = address.Value;
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
