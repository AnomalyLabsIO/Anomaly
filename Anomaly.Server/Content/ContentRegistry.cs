using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Server.Assets;
using Anomaly.Shared.Content;
using InventorySystem;
using PlayerRoles;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Order-independent store of registered custom-content definitions, frozen once (before
    /// any spawn) into a deterministic <see cref="ContentCatalog"/>. Computes the custom id
    /// bases from the LIVE game enums + loader dictionaries so they auto-adapt to game updates
    /// (never hardcoded). Post-freeze it exposes id/key/logic/entry lookups for the bridge,
    /// registrar, grant helpers, and name bridge.
    /// </summary>
    internal static class ContentRegistry
    {
        // Item pickup ids cross the (widened) wire as a byte; reserve 255 for ItemType.None.
        private const int ItemIdCeiling = 254;
        // RoleTypeId crosses the wire as an sbyte.
        private const int RoleIdCeiling = 127;

        private static readonly Dictionary<string, CustomItemDefinition> _items = new Dictionary<string, CustomItemDefinition>();
        private static readonly Dictionary<string, CustomRoleDefinition> _roles = new Dictionary<string, CustomRoleDefinition>();
        private static readonly Dictionary<string, CustomEffectDefinition> _effects = new Dictionary<string, CustomEffectDefinition>();

        public static bool Frozen { get; private set; }
        public static ContentCatalog Catalog { get; private set; } = new ContentCatalog();

        // Post-freeze lookups.
        private static readonly Dictionary<string, int> _keyToId = new Dictionary<string, int>();
        private static readonly Dictionary<int, CatalogEntry> _itemById = new Dictionary<int, CatalogEntry>();
        private static readonly Dictionary<int, CatalogEntry> _roleById = new Dictionary<int, CatalogEntry>();
        private static readonly Dictionary<int, ItemLogicBase> _itemLogic = new Dictionary<int, ItemLogicBase>();
        private static readonly Dictionary<int, RoleLogicBase> _roleLogic = new Dictionary<int, RoleLogicBase>();

        internal static void Add(CustomItemDefinition def) => AddGeneric(def?.Key, def, _items, "item");
        internal static void Add(CustomRoleDefinition def) => AddGeneric(def?.Key, def, _roles, "role");
        internal static void Add(CustomEffectDefinition def) => AddGeneric(def?.Key, def, _effects, "effect");

        private static void AddGeneric<T>(string key, T def, Dictionary<string, T> store, string kind) where T : class
        {
            if (Frozen)
            {
                Logger.Warn($"Anomaly.Content: cannot register {kind} '{key}' after the catalog is frozen; ignoring.");
                return;
            }
            if (def == null)
                return;
            if (!ModNamespace.IsValidContentKey(key))
            {
                Logger.Warn($"Anomaly.Content: invalid {kind} key '{key}' (expected 'modid:name', lowercase); ignoring.");
                return;
            }
            if (_items.ContainsKey(key) || _roles.ContainsKey(key) || _effects.ContainsKey(key))
            {
                Logger.Warn($"Anomaly.Content: duplicate content key '{key}'; ignoring.");
                return;
            }
            store[key] = def;
        }

        /// <summary>
        /// Freezes the registry into the catalog. Idempotent. Builds entries, assigns ids from
        /// the dynamic bases, hashes the result, and populates lookups. Does NOT inject game
        /// templates — that's the registrar's job, driven by the bridge after freeze.
        /// </summary>
        internal static void Freeze()
        {
            if (Frozen)
                return;

            var entries = new List<CatalogEntry>();
            foreach (var d in _items.Values) entries.Add(BuildItemEntry(d));
            foreach (var d in _roles.Values) entries.Add(BuildRoleEntry(d));
            foreach (var d in _effects.Values) entries.Add(BuildEffectEntry(d));

            int itemBase = CatalogBuilder.ComputeIdBase(MaxEnumValue<ItemType>(), IntKeys(InventoryItemLoader.AvailableItems.Keys));
            int roleBase = CatalogBuilder.ComputeIdBase(MaxEnumValue<RoleTypeId>(), IntKeys(PlayerRoleLoader.AllRoles.Keys));

            try
            {
                Catalog = CatalogBuilder.Build(entries, itemBase, ItemIdCeiling, roleBase, RoleIdCeiling);
            }
            catch (Exception ex)
            {
                Logger.Error($"Anomaly.Content: failed to build catalog ({ex.Message}); custom content disabled this session.");
                Catalog = new ContentCatalog();
                Frozen = true;
                return;
            }

            foreach (var e in Catalog.Entries)
            {
                _keyToId[e.Key] = e.AssignedId;
                switch (e.Kind)
                {
                    case CatalogEntryKind.Item:
                        _itemById[e.AssignedId] = e;
                        if (_items.TryGetValue(e.Key, out var idef) && idef.Logic != null)
                            _itemLogic[e.AssignedId] = idef.Logic;
                        break;
                    case CatalogEntryKind.Role:
                        _roleById[e.AssignedId] = e;
                        if (_roles.TryGetValue(e.Key, out var rdef) && rdef.Logic != null)
                            _roleLogic[e.AssignedId] = rdef.Logic;
                        break;
                }
            }

            Frozen = true;
            Logger.Info($"Anomaly.Content: catalog frozen — {_items.Count} items, {_roles.Count} roles, {_effects.Count} effects " +
                        $"(hash=0x{Catalog.CatalogHash:X8}, itemBase={itemBase}, roleBase={roleBase}).");
        }

        // --- lookups ---

        internal static bool TryGetId(string key, out int id) => _keyToId.TryGetValue(key, out id);
        internal static bool TryGetItemEntry(int id, out CatalogEntry entry) => _itemById.TryGetValue(id, out entry);
        internal static bool TryGetRoleEntry(int id, out CatalogEntry entry) => _roleById.TryGetValue(id, out entry);
        internal static bool TryGetItemLogic(int id, out ItemLogicBase logic) => _itemLogic.TryGetValue(id, out logic);
        internal static bool TryGetRoleLogic(int id, out RoleLogicBase logic) => _roleLogic.TryGetValue(id, out logic);
        internal static bool IsCustomItemId(int id) => _itemById.ContainsKey(id);
        internal static bool IsCustomRoleId(int id) => _roleById.ContainsKey(id);

        internal static IEnumerable<CatalogEntry> ItemEntries => _itemById.Values;
        internal static IEnumerable<CatalogEntry> RoleEntries => _roleById.Values;

        // --- entry builders ---

        private static CatalogEntry BuildItemEntry(CustomItemDefinition d) => new CatalogEntry
        {
            Key = d.Key,
            Kind = CatalogEntryKind.Item,
            BaseTemplateId = (int)d.BaseTemplate,
            DisplayName = d.DisplayName ?? "",
            Description = d.Description ?? "",
            Weight = d.Weight,
            ModelBundleHash = ResolveHash(d.ModelBundlePath, d.Key),
            ModelAssetName = d.ModelAssetName ?? "",
            IconHash = ResolveHash(d.IconPath, d.Key),
        };

        private static CatalogEntry BuildRoleEntry(CustomRoleDefinition d) => new CatalogEntry
        {
            Key = d.Key,
            Kind = CatalogEntryKind.Role,
            BaseTemplateId = (int)d.BaseTemplate,
            DisplayName = d.DisplayName ?? "",
            ColorRgba = PackColor(d.Color),
            Team = (byte)d.Team,
            ModelBundleHash = ResolveHash(d.ModelBundlePath, d.Key),
            ModelAssetName = d.ModelAssetName ?? "",
        };

        private static CatalogEntry BuildEffectEntry(CustomEffectDefinition d) => new CatalogEntry
        {
            Key = d.Key,
            Kind = CatalogEntryKind.Effect,
            DisplayName = d.DisplayName ?? "",
            EffectClassification = d.Classification,
            EffectSpeedMultiplier = d.SpeedMultiplier,
        };

        private static byte[] ResolveHash(string relativePath, string key)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;
            var hash = AssetRegistry.GetHashByPath(relativePath);
            if (hash == null)
                Logger.Warn($"Anomaly.Content: asset '{relativePath}' for '{key}' not found in the asset directory; entry will fall back to no asset.");
            return hash;
        }

        private static uint PackColor(Color c)
        {
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return (uint)((r << 24) | (g << 16) | (b << 8) | a);
        }

        private static int MaxEnumValue<T>() where T : struct, Enum
        {
            int max = int.MinValue;
            foreach (var v in (T[])Enum.GetValues(typeof(T)))
            {
                int i = Convert.ToInt32(v);
                if (i > max)
                    max = i;
            }
            return max;
        }

        private static IEnumerable<int> IntKeys<T>(IEnumerable<T> keys) where T : struct, Enum =>
            keys.Select(k => Convert.ToInt32(k));
    }
}
