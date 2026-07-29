using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Anomaly.Shared.Content
{
    /// <summary>
    /// Deterministic catalog assembly + integrity hashing, shared by client and server.
    /// The server <see cref="Build"/>s the catalog (assigning ids from dynamic bases); the
    /// client recomputes <see cref="ComputeCatalogHash"/> over what it received to confirm
    /// both sides derived identical content.
    /// </summary>
    public static class CatalogBuilder
    {
        /// <summary>
        /// Dynamic custom-id base = 1 + the highest id the running game knows about, so it
        /// auto-adapts when the game adds new vanilla items/roles. Mirrors the game's own
        /// max-id computation (<c>Mathf.Max(num,(int)key)</c> over AvailableItems in
        /// ItemTranslationReader). <paramref name="maxDefinedEnumValue"/> comes from
        /// <c>Enum.GetValues</c> (covers unloaded holiday entries); <paramref name="registryKeys"/>
        /// from the live loader dictionary (guards interop staleness). Never fills gaps below max.
        /// </summary>
        public static int ComputeIdBase(int maxDefinedEnumValue, IEnumerable<int> registryKeys)
        {
            int max = maxDefinedEnumValue;
            if (registryKeys != null)
            {
                foreach (int k in registryKeys)
                    if (k > max)
                        max = k;
            }
            return max + 1;
        }

        /// <summary>
        /// Stable-sorts entries by (Kind, Key), assigns per-kind ids from the supplied bases
        /// (effects get a 0-based append index), validates ceilings, and computes the integrity
        /// hash over the assigned, ordered entries. Throws if a kind's assignment exceeds its
        /// ceiling so the server fails loudly at freeze rather than silently dropping content.
        /// </summary>
        public static ContentCatalog Build(IEnumerable<CatalogEntry> entries,
            int itemIdBase, int itemIdCeiling, int roleIdBase, int roleIdCeiling)
        {
            var sorted = Sort(entries);

            int nextItem = itemIdBase, nextRole = roleIdBase, nextEffect = 0;
            foreach (var e in sorted)
            {
                switch (e.Kind)
                {
                    case CatalogEntryKind.Item:
                        if (nextItem > itemIdCeiling)
                            throw new InvalidOperationException(
                                $"Custom item id space exhausted (base {itemIdBase}, ceiling {itemIdCeiling}). Reduce custom items or raise the ceiling.");
                        e.AssignedId = nextItem++;
                        break;
                    case CatalogEntryKind.Role:
                        if (nextRole > roleIdCeiling)
                            throw new InvalidOperationException(
                                $"Custom role id space exhausted (base {roleIdBase}, ceiling {roleIdCeiling}).");
                        e.AssignedId = nextRole++;
                        break;
                    case CatalogEntryKind.Effect:
                        e.AssignedId = nextEffect++;
                        break;
                }
            }

            return new ContentCatalog
            {
                Entries = sorted,
                CatalogHash = HashSorted(sorted),
            };
        }

        /// <summary>
        /// Recomputes the catalog hash over the entries (re-sorted canonically). The client
        /// calls this on the received catalog and compares to <see cref="ContentCatalog.CatalogHash"/>.
        /// </summary>
        public static uint ComputeCatalogHash(IEnumerable<CatalogEntry> entries) => HashSorted(Sort(entries));

        private static List<CatalogEntry> Sort(IEnumerable<CatalogEntry> entries) =>
            entries.OrderBy(e => (byte)e.Kind).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();

        private static uint HashSorted(List<CatalogEntry> sorted)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                foreach (var e in sorted)
                    e.WriteTo(w);
                w.Flush();
                return Fnv1a(ms.ToArray());
            }
        }

        // FNV-1a 32-bit over raw bytes — same constants as AnomalyMessageRegistry.ComputeHash,
        // applied to the serialized entries so the hash covers exactly the wire content.
        private static uint Fnv1a(byte[] data)
        {
            uint hash = 2166136261u;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
