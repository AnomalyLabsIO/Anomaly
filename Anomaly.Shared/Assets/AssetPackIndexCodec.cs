using System;
using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Assets;

/// <summary>Bounded metadata validation shared by authoring, the server and the client.</summary>
public static partial class AssetPackIndexCodec
{
    public const int MaxIndexBytes = 256 * 1024;
    public const int MaxAssets = 4095;
    public const int MaxVisuals = 128;
    public const int MaxVisualNodes = 64;
    public const int MaxVisualDepth = 16;
    public const int MaxMeshVertices = 100000;
    public const int MaxMeshTriangles = 200000;
    public const int MaxSceneInstances = 128;
    public const int MaxSceneTriangles = 1000000;
    public const long MaxExpandedBytes = 128L * 1024 * 1024;
    public const long MaxDecodedBytes = 256L * 1024 * 1024;

    public static AssetPackIndex Parse(byte[] bytes, bool remote)
    {
        Dictionary<string, object> map = BoundedPackJson.Read(bytes);
        if (Integer(map, "SchemaVersion") != AssetPackIndex.CurrentSchemaVersion)
            throw Bad("Unsupported asset index schema; re-export this pack with the current authoring tools to include its measured decoded resource budget");
        Keys(map, "SchemaVersion", "PackId", "Namespace", "UnityVersion", "Platform", "GameProfile", "BundleFile", "BundleSha256", "BundleBytes", "DecodedBytes", "Assets", "Visuals");
        var index = new AssetPackIndex
        {
            SchemaVersion = Integer(map, "SchemaVersion"), PackId = Text(map, "PackId"), Namespace = Text(map, "Namespace"),
            UnityVersion = Text(map, "UnityVersion"), Platform = Text(map, "Platform"), GameProfile = Text(map, "GameProfile"),
            BundleFile = Text(map, "BundleFile"), BundleSha256 = Text(map, "BundleSha256"), BundleBytes = Integer(map, "BundleBytes"),
            DecodedBytes = Integer(map, "DecodedBytes"),
        };
        foreach (object value in List(map, "Assets", MaxAssets))
        {
            var asset = Object(value);
            Keys(asset, "Address", "Kind", "EntryName", "Width", "Height", "SampleFrames", "SampleRate", "Channels", "VertexCount", "TriangleCount");
            index.Assets.Add(new PackAssetEntry
            {
                Address = Text(asset, "Address"), Kind = Text(asset, "Kind"), EntryName = Text(asset, "EntryName"),
                Width = Integer(asset, "Width", true), Height = Integer(asset, "Height", true),
                SampleFrames = Integer(asset, "SampleFrames", true), SampleRate = Integer(asset, "SampleRate", true),
                Channels = Integer(asset, "Channels", true), VertexCount = Integer(asset, "VertexCount", true),
                TriangleCount = Integer(asset, "TriangleCount", true),
            });
        }
        foreach (object value in List(map, "Visuals", MaxVisuals, true))
        {
            var visualMap = Object(value);
            Keys(visualMap, "Address", "Nodes");
            var visual = new DecorativeVisualDefinition { Address = Text(visualMap, "Address") };
            foreach (object nodeValue in List(visualMap, "Nodes", MaxVisualNodes))
            {
                var node = Object(nodeValue);
                Keys(node, "Name", "Parent", "Position", "Rotation", "Scale", "MeshAddress", "TextureAddress", "MaterialPreset", "Color");
                visual.Nodes.Add(new DecorativeVisualNode
                {
                    Name = Text(node, "Name"), Parent = Integer(node, "Parent"),
                    Position = Vector(node, "Position", 3), Rotation = Vector(node, "Rotation", 4), Scale = Vector(node, "Scale", 3),
                    MeshAddress = Text(node, "MeshAddress", true), TextureAddress = Text(node, "TextureAddress", true),
                    MaterialPreset = Text(node, "MaterialPreset", true),
                    Color = node.ContainsKey("Color") ? Vector(node, "Color", 4) : new[] { 1f, 1f, 1f, 1f },
                });
            }
            index.Visuals.Add(visual);
        }
        Validate(index, remote);
        return index;
    }

    public static void Validate(AssetPackIndex index, bool remote)
    {
        if (index == null || index.SchemaVersion != AssetPackIndex.CurrentSchemaVersion ||
            index.UnityVersion != AssetPackIndex.SupportedUnityVersion || index.Platform != AssetPackIndex.SupportedPlatform ||
            index.GameProfile != AssetPackIndex.SupportedGameProfile) throw Bad("Unsupported pack compatibility profile");
        if (string.IsNullOrEmpty(index.Namespace) || index.Namespace.Length > 64 || !Content.ModNamespace.IsValid(index.Namespace) ||
            (remote && Content.ModNamespace.IsReserved(index.Namespace))) throw Bad("Invalid pack namespace");
        if (!IsPackId(index.PackId, remote) || !index.PackId.StartsWith(index.Namespace + ":", StringComparison.Ordinal)) throw Bad("Invalid pack id");
        if (string.IsNullOrEmpty(index.BundleFile) || index.BundleFile.Length > 128 ||
            !index.BundleFile.EndsWith(".bundle", StringComparison.Ordinal) || !ValidSegment(index.BundleFile)) throw Bad("BundleFile must be one lowercase .bundle filename");
        if (!IsHash(index.BundleSha256) || index.BundleBytes <= 0 || index.BundleBytes > 64L * 1024 * 1024)
            throw Bad("Invalid bundle hash or encoded size");
        if (index.DecodedBytes <= 0 || index.DecodedBytes > MaxDecodedBytes)
            throw Bad("Invalid measured decoded bundle size");
        if (index.Assets == null || index.Assets.Count == 0 || index.Assets.Count > MaxAssets ||
            index.Visuals == null || index.Visuals.Count > MaxVisuals) throw Bad("Invalid asset or visual count");
        var roots = new Dictionary<string, PackAssetEntry>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackAssetEntry entry in index.Assets)
        {
            if (entry == null) throw Bad("Null asset entry");
            AssetAddress address = ValidateAddress(entry.Address, index.Namespace, remote);
            if (!addresses.Add(entry.Address) || entry.EntryName != entry.Address) throw Bad("Duplicate address or noncanonical bundle entry name");
            AssetKind kind = entry.AssetKind;
            if (kind == AssetKind.Unknown) throw Bad("Unsupported asset type");
            if (address.IsSkin && kind != AssetKind.Texture2D) throw Bad("Skin targets require Texture2D");
            bool cassie = entry.Address.StartsWith("game/cassie/", StringComparison.Ordinal);
            if (cassie && kind != AssetKind.AudioClip) throw Bad("CASSIE targets require AudioClip");
            switch (kind)
            {
                case AssetKind.Texture2D:
                case AssetKind.Sprite:
                    if (entry.Width <= 0 || entry.Height <= 0 || entry.Width > 4096 || entry.Height > 4096 ||
                        (long)entry.Width * entry.Height > 16L * 1024 * 1024 ||
                        entry.SampleFrames != 0 || entry.SampleRate != 0 || entry.Channels != 0 || entry.VertexCount != 0 || entry.TriangleCount != 0)
                        throw Bad("Invalid texture/sprite dimensions or unrelated metadata");
                    break;
                case AssetKind.AudioClip:
                    if (entry.SampleFrames <= 0 || entry.SampleRate < 8000 || entry.SampleRate > 96000 ||
                        entry.Channels < 1 || entry.Channels > 2 || entry.SampleFrames / (double)entry.SampleRate > 30 ||
                        entry.Width != 0 || entry.Height != 0 || entry.VertexCount != 0 || entry.TriangleCount != 0)
                        throw Bad("Invalid audio metadata");
                    if (cassie && (!StockAssetProfile.TryGetCassie(address, out CassieAssetTiming timing) ||
                        !timing.MatchesClip(entry.SampleFrames, entry.SampleRate))) throw Bad("CASSIE replacement changes the stock clip timing contract");
                    break;
                case AssetKind.Mesh:
                    if (entry.VertexCount <= 0 || entry.VertexCount > MaxMeshVertices || entry.TriangleCount <= 0 || entry.TriangleCount > MaxMeshTriangles ||
                        entry.Width != 0 || entry.Height != 0 || entry.SampleFrames != 0 || entry.SampleRate != 0 || entry.Channels != 0)
                        throw Bad("Invalid static mesh metadata");
                    break;
            }
            roots.Add(entry.Address, entry);
        }
        int totalNodes = 0;
        foreach (DecorativeVisualDefinition visual in index.Visuals)
        {
            if (visual == null || !ValidateAddress(visual.Address, index.Namespace, remote).Value.StartsWith("mods/", StringComparison.Ordinal) ||
                !addresses.Add(visual.Address) || visual.Nodes == null || visual.Nodes.Count == 0 || visual.Nodes.Count > MaxVisualNodes)
                throw Bad("Invalid or duplicate decorative visual");
            totalNodes = checked(totalNodes + visual.Nodes.Count);
            if (totalNodes > 1024) throw Bad("Pack visual descriptions exceed their total node budget");
            var depths = new int[visual.Nodes.Count];
            var scaleBounds = new double[visual.Nodes.Count];
            var positionBounds = new double[visual.Nodes.Count];
            long triangles = 0;
            for (int i = 0; i < visual.Nodes.Count; i++)
            {
                DecorativeVisualNode node = visual.Nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 64 ||
                    node.Parent < -1 || node.Parent >= i || (i == 0 ? node.Parent != -1 : node.Parent == -1)) throw Bad("Invalid visual hierarchy");
                foreach (char c in node.Name) if (char.IsControl(c)) throw Bad("Invalid node name");
                depths[i] = node.Parent < 0 ? 1 : depths[node.Parent] + 1;
                if (depths[i] > MaxVisualDepth) throw Bad("Visual hierarchy exceeded its depth limit");
                ValidateTransform(node.Position, node.Rotation, node.Scale);
                // Bound the composed hierarchy too: individually finite transforms can
                // otherwise produce enormous matrices after repeated parent scaling.
                double parentScale = node.Parent < 0 ? 1 : scaleBounds[node.Parent];
                double parentPosition = node.Parent < 0 ? 0 : positionBounds[node.Parent];
                double localScale = Math.Max(node.Scale[0], Math.Max(node.Scale[1], node.Scale[2]));
                double localPosition = Math.Sqrt((double)node.Position[0] * node.Position[0] +
                    (double)node.Position[1] * node.Position[1] + (double)node.Position[2] * node.Position[2]);
                scaleBounds[i] = parentScale * localScale;
                positionBounds[i] = parentPosition + parentScale * localPosition;
                if (scaleBounds[i] > 100 || positionBounds[i] > 100000)
                    throw Bad("Composed visual transform exceeds its spatial budget");
                ValidateVector(node.Color, 4, 0, 1);
                if (node.Color[3] != 1f) throw Bad("Decorative materials must be opaque");
                if (string.IsNullOrEmpty(node.MeshAddress))
                {
                    if (!string.IsNullOrEmpty(node.TextureAddress) || !string.IsNullOrEmpty(node.MaterialPreset)) throw Bad("A transform-only node cannot define rendering properties");
                    continue;
                }
                if (!roots.TryGetValue(node.MeshAddress, out PackAssetEntry mesh) || mesh.AssetKind != AssetKind.Mesh)
                    throw Bad("Visual meshes must reference a Mesh in the same pack");
                if (!string.IsNullOrEmpty(node.TextureAddress) && (!roots.TryGetValue(node.TextureAddress, out PackAssetEntry texture) || texture.AssetKind != AssetKind.Texture2D))
                    throw Bad("Visual textures must reference a Texture2D in the same pack");
                if (node.MaterialPreset != "opaque-unlit" && node.MaterialPreset != "opaque-lit") throw Bad("Unknown trusted material preset");
                triangles += mesh.TriangleCount;
                if (triangles > MaxSceneTriangles) throw Bad("A visual alone exceeds the scene triangle budget");
            }
        }
    }

    public static long GetTriangleCount(AssetPackIndex index, DecorativeVisualDefinition visual)
    {
        long total = 0;
        foreach (DecorativeVisualNode node in visual.Nodes)
            if (!string.IsNullOrEmpty(node.MeshAddress))
            {
                PackAssetEntry mesh = index.Assets.Find(asset => asset.Address == node.MeshAddress);
                if (mesh == null || mesh.AssetKind != AssetKind.Mesh) throw Bad("Missing visual mesh");
                total = checked(total + mesh.TriangleCount);
            }
        return total;
    }

    public static void ValidateTransform(float[] position, float[] rotation, float[] scale)
    {
        ValidateVector(position, 3, -100000, 100000);
        ValidateVector(rotation, 4, -1, 1);
        ValidateVector(scale, 3, 0.001f, 100);
        double squared = 0;
        foreach (float component in rotation) squared += (double)component * component;
        if (Math.Abs(squared - 1) > 0.001) throw Bad("Visual rotation must be a unit quaternion");
    }

    private static AssetAddress ValidateAddress(string value, string packNamespace, bool remote)
    {
        if (!AssetAddress.TryParse(value, out AssetAddress address)) throw Bad("Unknown or noncanonical asset address");
        if (address.IsSkin && remote) throw Bad("Skin replacements are local-only");
        if (!address.IsGameReplacement && !value.StartsWith("mods/" + packNamespace + "/", StringComparison.Ordinal))
            throw Bad("An added asset must belong to its pack namespace");
        return address;
    }
    public static bool IsHash(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        return true;
    }
    public static bool IsPackId(string value, bool remote = true)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        int colon = value.IndexOf(':');
        if (colon <= 0 || colon > 64 || colon == value.Length - 1) return false;
        string owner = value.Substring(0, colon);
        return Content.ModNamespace.IsValid(owner) && (!remote || !Content.ModNamespace.IsReserved(owner)) && ValidSegment(value.Substring(colon + 1));
    }
    private static bool ValidSegment(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "." || value == "..") return false;
        foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
        return true;
    }
    private static void ValidateVector(float[] value, int count, float minimum, float maximum)
    {
        if (value == null || value.Length != count) throw Bad("Invalid vector length");
        foreach (float component in value)
            if (float.IsNaN(component) || float.IsInfinity(component) || component < minimum || component > maximum) throw Bad("Nonfinite or out-of-range vector component");
    }
    private static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw Bad("Expected JSON object");
    private static void Keys(Dictionary<string, object> map, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (string key in map.Keys) if (!allowed.Contains(key)) throw Bad("Unknown index property: " + key);
    }
    private static string Text(Dictionary<string, object> map, string key, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value) || value == null)
            return optional ? null : throw Bad("Missing text property: " + key);
        return value as string ?? throw Bad("Expected text property: " + key);
    }
    private static int Integer(Dictionary<string, object> map, string key, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value)) return optional ? 0 : throw Bad("Missing numeric property: " + key);
        if (!(value is double number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) throw Bad("Invalid integer: " + key);
        return (int)number;
    }
    private static List<object> List(Dictionary<string, object> map, string key, int maximum, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value)) return optional ? new List<object>() : throw Bad("Missing array: " + key);
        if (!(value is List<object> list) || list.Count > maximum) throw Bad("Invalid array: " + key);
        return list;
    }
    private static float[] Vector(Dictionary<string, object> map, string key, int count)
    {
        List<object> values = List(map, key, count);
        if (values.Count != count) throw Bad("Invalid vector: " + key);
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (!(values[i] is double number) || number < -float.MaxValue || number > float.MaxValue) throw Bad("Invalid vector number: " + key);
            result[i] = (float)number;
        }
        return result;
    }
    private static InvalidDataException Bad(string message) => new InvalidDataException(message + ".");
}
