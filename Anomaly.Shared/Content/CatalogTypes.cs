using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Content
{
    /// <summary>Which game system a catalog entry materializes into.</summary>
    public enum CatalogEntryKind : byte
    {
        Item = 0,
        Role = 1,
        Effect = 2,
    }

    /// <summary>
    /// Wire IO helpers shared by the catalog DTOs and messages. Length-prefixed UTF-8
    /// strings (ushort length) and optional 32-byte content hashes (1-byte length, 0 = absent).
    /// </summary>
    internal static class CatalogIo
    {
        public static void WriteStr(BinaryWriter w, string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        public static string ReadStr(BinaryReader r)
        {
            int len = r.ReadUInt16();
            return Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        public static void WriteHash(BinaryWriter w, byte[] h)
        {
            if (h == null || h.Length == 0)
            {
                w.Write((byte)0);
                return;
            }
            w.Write((byte)h.Length);
            w.Write(h);
        }

        public static byte[] ReadHash(BinaryReader r)
        {
            int len = r.ReadByte();
            return len == 0 ? null : r.ReadBytes(len);
        }
    }

    /// <summary>
    /// One server-defined custom content definition as it crosses the wire and is applied
    /// by a per-runtime registrar. Pure data — carries no logic-type token (server logic
    /// stays in the author's plugin and never ships to clients). Field set is a flat
    /// superset across kinds; unused fields keep their defaults per kind.
    /// </summary>
    public sealed class CatalogEntry
    {
        /// <summary>Namespaced stable key, e.g. "mymod:magic_coin". Drives deterministic ordering.</summary>
        public string Key = "";

        public CatalogEntryKind Kind;

        /// <summary>
        /// Server-assigned concrete id: an ItemType / RoleTypeId value for items/roles, or the
        /// per-controller append index for effects. Assigned by <see cref="CatalogBuilder"/>.
        /// </summary>
        public int AssignedId;

        /// <summary>Vanilla template to clone (ItemType/RoleTypeId value). -1 for effects.</summary>
        public int BaseTemplateId = -1;

        /// <summary>Display name (already resolved through the author's i18n, server locale).</summary>
        public string DisplayName = "";

        /// <summary>Optional item description; "" if unused.</summary>
        public string Description = "";

        /// <summary>32-byte SHA-256 of the AssetBundle carrying the model, or null.</summary>
        public byte[] ModelBundleHash;

        /// <summary>Asset name within the bundle; "" if no custom model.</summary>
        public string ModelAssetName = "";

        /// <summary>32-byte SHA-256 of the icon PNG, or null (items).</summary>
        public byte[] IconHash;

        // Item attributes
        public float Weight;

        // Role attributes
        public uint ColorRgba;
        public byte Team;

        // Effect attributes
        public byte EffectClassification;
        public float EffectSpeedMultiplier = 1f;

        public void WriteTo(BinaryWriter w)
        {
            w.Write((byte)Kind);
            w.Write(AssignedId);
            w.Write(BaseTemplateId);
            CatalogIo.WriteStr(w, Key);
            CatalogIo.WriteStr(w, DisplayName);
            CatalogIo.WriteStr(w, Description);
            CatalogIo.WriteHash(w, ModelBundleHash);
            CatalogIo.WriteStr(w, ModelAssetName);
            CatalogIo.WriteHash(w, IconHash);
            w.Write(Weight);
            w.Write(ColorRgba);
            w.Write(Team);
            w.Write(EffectClassification);
            w.Write(EffectSpeedMultiplier);
        }

        public static CatalogEntry ReadFrom(BinaryReader r)
        {
            return new CatalogEntry
            {
                Kind = (CatalogEntryKind)r.ReadByte(),
                AssignedId = r.ReadInt32(),
                BaseTemplateId = r.ReadInt32(),
                Key = CatalogIo.ReadStr(r),
                DisplayName = CatalogIo.ReadStr(r),
                Description = CatalogIo.ReadStr(r),
                ModelBundleHash = CatalogIo.ReadHash(r),
                ModelAssetName = CatalogIo.ReadStr(r),
                IconHash = CatalogIo.ReadHash(r),
                Weight = r.ReadSingle(),
                ColorRgba = r.ReadUInt32(),
                Team = r.ReadByte(),
                EffectClassification = r.ReadByte(),
                EffectSpeedMultiplier = r.ReadSingle(),
            };
        }
    }

    /// <summary>The frozen set of custom content for a server session, plus its integrity hash.</summary>
    public sealed class ContentCatalog
    {
        public uint CatalogHash;
        public List<CatalogEntry> Entries = new List<CatalogEntry>();
    }
}
