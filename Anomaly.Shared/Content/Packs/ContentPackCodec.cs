using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;

namespace Anomaly.Shared.Content.Packs;

/// <summary>Bounded authoring metadata; reads only explicitly declared components, never discovers files.</summary>
public static class ContentPackCodec
{
    public const int MaxDescriptorBytes = 16 * 1024;
    public const int MaxItemsBytes = 64 * 1024;
    public const int MaxPacks = 128;
    public const int MaxScannedEntries = 4096;
    private const int MaxItems = 127;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static ContentPackDescriptor Parse(byte[] bytes, bool remote = true)
    {
        var map = Read(bytes, MaxDescriptorBytes, 3, 96, 8, 6, AssetAddress.MaxLength);
        Keys(map, "SchemaVersion", "PackId", "Version", "Scripts", "Assets", "Data");
        var descriptor = new ContentPackDescriptor
        {
            SchemaVersion = Integer(map, "SchemaVersion"), PackId = Text(map, "PackId"), Version = Text(map, "Version")
        };
        if (map.TryGetValue("Scripts", out object scriptsValue))
        {
            var scripts = Object(scriptsValue);
            Keys(scripts, "PackId", "EntryPoint", "Messages");
            descriptor.Scripts = new ContentPackScripts
            {
                PackId = Text(scripts, "PackId"), EntryPoint = Text(scripts, "EntryPoint"), Messages = Text(scripts, "Messages", optional: true)
            };
        }
        if (map.TryGetValue("Assets", out object assetsValue))
        {
            var assets = Object(assetsValue);
            Keys(assets, "PackId", "Index");
            descriptor.Assets = new ContentPackAssets { PackId = Text(assets, "PackId"), Index = Text(assets, "Index") };
        }
        if (map.TryGetValue("Data", out object dataValue))
        {
            var data = Object(dataValue);
            Keys(data, "Items");
            descriptor.Data = new ContentPackData { Items = Text(data, "Items") };
        }
        Validate(descriptor, remote);
        return descriptor;
    }

    /// <summary>
    /// Captures only declared metadata and source bytes. The caller bounds reads before allocating,
    /// protects its filesystem/resource boundary, and verifies the separate bundle at publication.
    /// </summary>
    public static CapturedContentPack Capture(ContentPackDescriptor descriptor, Func<string, int, byte[]> read, bool remote = true)
    {
        Validate(descriptor, remote);
        if (read == null) throw new ArgumentNullException(nameof(read));
        var result = new CapturedContentPack { Descriptor = descriptor };
        string owner = NamespaceOf(descriptor.PackId);
        if (descriptor.Assets != null)
        {
            result.AssetIndexBytes = ReadComponent(read, descriptor.Assets.Index, AssetPackIndexCodec.MaxIndexBytes);
            result.AssetIndex = AssetPackIndexCodec.Parse(result.AssetIndexBytes, remote);
            if (result.AssetIndex.PackId != descriptor.Assets.PackId || result.AssetIndex.Namespace != owner)
                throw Bad("Asset index identity does not match its declared component");
            int slash = descriptor.Assets.Index.LastIndexOf('/');
            result.BundlePath = (slash < 0 ? string.Empty : descriptor.Assets.Index.Substring(0, slash + 1)) + result.AssetIndex.BundleFile;
            if (!IsRelativePath(result.BundlePath)) throw Bad("Invalid bundle path");
        }
        if (descriptor.Scripts != null)
        {
            var script = descriptor.Scripts;
            var index = new ScriptPackIndex { PackId = script.PackId };
            // Validate schemas before reading source; global script-count limits also
            // bound schemas to 8 packs * 32 schemas without an extra descriptor parser.
            if (script.Messages != null)
                index.Messages = ScriptPackIndexCodec.ParseMessageSchemas(ReadComponent(read, script.Messages, ScriptPackLimits.MaxIndexBytes));
            result.SourceBytes = ReadComponent(read, script.EntryPoint, ScriptPackLimits.MaxScriptBytesPerFile);
            result.SourceText = ScriptSourceValidator.Validate(result.SourceBytes);
            index.Sources.Add(new ScriptPackSource
            {
                Address = SourceAddress(descriptor), SourceBytes = result.SourceBytes.Length,
                SourceSha256 = ScriptSourceValidator.HashHex(result.SourceBytes)
            });
            result.ScriptIndexBytes = ScriptPackIndexCodec.Encode(index);
            result.ScriptIndex = index;
        }
        if (descriptor.Data != null)
            result.Items = ReadItems(ReadComponent(read, descriptor.Data.Items, MaxItemsBytes), owner, result.AssetIndex);
        return result;
    }

    /// <summary>Canonical pack-relative path, independent of the host operating system.</summary>
    public static bool IsRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > AssetAddress.MaxLength) return false;
        int start = 0, segments = 1;
        for (int index = 0; index <= path.Length; index++)
        {
            if (index == path.Length || path[index] == '/')
            {
                int length = index - start;
                if (length == 0 || (length == 1 && path[start] == '.') ||
                    (length == 2 && path[start] == '.' && path[start + 1] == '.')) return false;
                if (index != path.Length && ++segments > 8) return false;
                start = index + 1;
            }
            else
            {
                char value = path[index];
                if (!(value >= 'a' && value <= 'z') && !(value >= '0' && value <= '9') && value != '.' && value != '_' && value != '-') return false;
            }
        }
        return true;
    }

    private static void Validate(ContentPackDescriptor descriptor, bool remote)
    {
        if (descriptor == null || descriptor.SchemaVersion != ContentPackDescriptor.CurrentSchemaVersion ||
            !AssetPackIndexCodec.IsPackId(descriptor.PackId, remote) || !IsVersion(descriptor.Version) ||
            (descriptor.Scripts == null && descriptor.Assets == null && descriptor.Data == null))
            throw Bad("Invalid content-pack profile, identity, version or empty component set");
        string owner = NamespaceOf(descriptor.PackId);
        var paths = new HashSet<string>(StringComparer.Ordinal) { "pack.json" };
        if (descriptor.Scripts != null)
        {
            var script = descriptor.Scripts;
            // Lua identities retain their existing non-reserved host profile even for local packs.
            if (!OwnedId(script.PackId, owner, true) || !ScriptPackIndexCodec.IsSourceAddress(SourceAddress(descriptor), script.PackId))
                throw Bad("Invalid script component identity or address");
            Path(script.EntryPoint, ".lua", paths);
            if (script.Messages != null) Path(script.Messages, ".json", paths);
        }
        if (descriptor.Assets != null)
        {
            if (!OwnedId(descriptor.Assets.PackId, owner, remote) || descriptor.Assets.PackId == descriptor.Scripts?.PackId)
                throw Bad("Invalid or duplicate asset component identity");
            Path(descriptor.Assets.Index, ".index.json", paths);
        }
        if (descriptor.Data != null) Path(descriptor.Data.Items, ".json", paths);
    }

    private static PackItemDefinition[] ReadItems(byte[] bytes, string owner, AssetPackIndex assets)
    {
        var map = Read(bytes, MaxItemsBytes, 3, 1024, MaxItems, 5, 4096);
        Keys(map, "SchemaVersion", "Items");
        if (Integer(map, "SchemaVersion") != 1 || !map.TryGetValue("Items", out object value) || !(value is List<object> items))
            throw Bad("Invalid item definition profile");
        var result = new List<PackItemDefinition>(items.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (object itemValue in items)
        {
            var item = Object(itemValue);
            Keys(item, "Key", "BaseTemplate", "DisplayName", "Description", "IconAddress");
            var definition = new PackItemDefinition
            {
                Key = Text(item, "Key"), BaseTemplate = Text(item, "BaseTemplate"), DisplayName = Text(item, "DisplayName"),
                Description = Text(item, "Description", optional: true) ?? string.Empty,
                IconAddress = Text(item, "IconAddress", optional: true)
            };
            if (!ModNamespace.IsValidContentKey(definition.Key) || !definition.Key.StartsWith(owner + ":", StringComparison.Ordinal) ||
                !keys.Add(definition.Key) || !EnumName(definition.BaseTemplate) || string.IsNullOrWhiteSpace(definition.DisplayName) ||
                ByteCount(definition.Key) > 128 || ByteCount(definition.DisplayName) > 512 || ByteCount(definition.Description) > 4096)
                throw Bad("Invalid, duplicate or out-of-namespace item definition");
            if (definition.IconAddress != null)
            {
                if (!AssetAddress.TryParse(definition.IconAddress, out _) || !definition.IconAddress.StartsWith("mods/" + owner + "/", StringComparison.Ordinal) ||
                    assets == null || !assets.Assets.Any(asset => asset.Address == definition.IconAddress &&
                        (asset.AssetKind == AssetKind.Texture2D || asset.AssetKind == AssetKind.Sprite)))
                    throw Bad("Item icon must be a texture or sprite in this pack's declared assets");
            }
            result.Add(definition);
        }
        return result.ToArray();
    }

    private static bool EnumName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] < 'A' || value[0] > 'Z') return false;
        foreach (char character in value)
            if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                !(character >= '0' && character <= '9') && character != '_') return false;
        return true;
    }

    private static bool IsVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
        int parts = 0, start = 0;
        for (int index = 0; index <= value.Length; index++)
        {
            if (index == value.Length || value[index] == '.')
            {
                int length = index - start;
                if (length == 0 || length > 10 || (length > 1 && value[start] == '0') || ++parts > 3) return false;
                start = index + 1;
            }
            else if (value[index] < '0' || value[index] > '9') return false;
        }
        return parts == 3;
    }

    private static byte[] ReadComponent(Func<string, int, byte[]> read, string path, int maximum)
    {
        byte[] bytes = read(path, maximum);
        if (bytes == null || bytes.Length > maximum) throw Bad("Declared component exceeded its byte limit: " + path);
        return bytes;
    }
    private static Dictionary<string, object> Read(byte[] bytes, int maximum, int depth, int tokens, int arrayItems, int properties, int textLength)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > maximum) throw Bad("Metadata exceeded its byte limit");
        try { return BoundedPackJson.Read(bytes, depth, tokens, arrayItems, properties, textLength); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Content-pack metadata must use strict UTF-8.", exception); }
    }
    private static int ByteCount(string value)
    {
        try { return Utf8.GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Item text must contain valid Unicode.", exception); }
    }
    private static bool OwnedId(string id, string owner, bool remote) => AssetPackIndexCodec.IsPackId(id, remote) && id.StartsWith(owner + ":", StringComparison.Ordinal);
    private static string NamespaceOf(string packId) => packId.Substring(0, packId.IndexOf(':'));
    private static string SourceAddress(ContentPackDescriptor descriptor) => "mods/" + descriptor.PackId.Replace(':', '/') + "/" + descriptor.Scripts.EntryPoint;
    private static void Path(string value, string extension, HashSet<string> paths)
    {
        if (!IsRelativePath(value) || !value.EndsWith(extension, StringComparison.Ordinal) || !paths.Add(value))
            throw Bad("Invalid, duplicate or noncanonical component path");
    }
    private static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw Bad("Expected JSON object");
    private static string Text(Dictionary<string, object> map, string key, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value)) return optional ? null : throw Bad("Missing text property: " + key);
        return value as string ?? throw Bad("Invalid text property: " + key);
    }
    private static int Integer(Dictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out object value) || !(value is double number) ||
            number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number))
            throw Bad("Invalid integer: " + key);
        return (int)number;
    }
    private static void Keys(Dictionary<string, object> map, params string[] allowed)
    {
        foreach (string key in map.Keys)
            if (Array.IndexOf(allowed, key) < 0) throw Bad("Unknown content-pack property: " + key);
    }
    private static InvalidDataException Bad(string message) => new InvalidDataException(message + ".");
}
