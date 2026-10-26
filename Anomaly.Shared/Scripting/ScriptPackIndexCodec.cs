using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Scripting;

/// <summary>Strict bounded JSON and deterministic source-pack index encoding.</summary>
public static class ScriptPackIndexCodec
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static ScriptPackIndex Parse(byte[] bytes)
    {
        var map = Read(bytes);
        Keys(map, "SchemaVersion", "PackId", "ApiVersion", "Sources", "Messages");
        var index = new ScriptPackIndex
        {
            SchemaVersion = Integer(map, "SchemaVersion"), PackId = Text(map, "PackId"), ApiVersion = Integer(map, "ApiVersion"),
        };
        foreach (object value in List(map, "Sources", ScriptPackLimits.MaxScripts))
        {
            var source = Object(value);
            Keys(source, "Address", "SourceSha256", "SourceBytes");
            index.Sources.Add(new ScriptPackSource { Address = Text(source, "Address"), SourceSha256 = Text(source, "SourceSha256"), SourceBytes = Integer(source, "SourceBytes") });
        }
        index.Messages = ReadMessages(map);
        Validate(index);
        return index;
    }

    /// <summary>Reads an optional author-supplied descriptor without accepting serializer extensions.</summary>
    public static List<ScriptMessageSchema> ParseMessageSchemas(byte[] bytes)
    {
        var map = Read(bytes);
        Keys(map, "Messages");
        if (!map.ContainsKey("Messages")) throw Bad("Missing Messages array");
        return ReadMessages(map);
    }

    public static void Validate(ScriptPackIndex index)
    {
        if (index == null || index.SchemaVersion != ScriptPackIndex.CurrentSchemaVersion || index.ApiVersion != ScriptPackLimits.ApiVersion ||
            !AssetPackIndexCodec.IsPackId(index.PackId) || index.Sources == null || index.Sources.Count < 1 || index.Sources.Count > ScriptPackLimits.MaxScripts)
            throw Bad("Invalid script pack profile, identity or source count");
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var hashes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ScriptPackSource source in index.Sources)
        {
            if (source == null || !IsSourceAddress(source.Address, index.PackId) || !addresses.Add(source.Address) ||
                !AssetPackIndexCodec.IsHash(source.SourceSha256) || source.SourceBytes < 0 || source.SourceBytes > ScriptPackLimits.MaxScriptBytesPerFile ||
                ((source.SourceBytes == 0) != (source.SourceSha256 == ScriptSourceValidator.EmptySha256Hex)))
                throw Bad("Invalid or duplicate script source entry");
            if (hashes.TryGetValue(source.SourceSha256, out int previousSize) && previousSize != source.SourceBytes)
                throw Bad("A source hash cannot declare conflicting byte counts");
            hashes[source.SourceSha256] = source.SourceBytes;
            bytes += source.SourceBytes;
            if (bytes > ScriptPackLimits.MaxScriptBytes) throw Bad("Script source inventory exceeded its byte limit");
        }
        ValidateMessages(index.Messages);
    }

    public static bool IsSourceAddress(string address, string packId)
    {
        if (!AssetPackIndexCodec.IsPackId(packId) || !AssetAddress.TryParse(address, out _) || !address.EndsWith(".lua", StringComparison.Ordinal)) return false;
        string owner = packId.Substring(0, packId.IndexOf(':'));
        return address.StartsWith("mods/" + owner + "/", StringComparison.Ordinal);
    }

    public static byte[] Encode(ScriptPackIndex index)
    {
        Validate(index);
        var text = new StringBuilder();
        text.Append("{\"SchemaVersion\":1,\"PackId\":"); Quote(text, index.PackId);
        text.Append(",\"ApiVersion\":1,\"Sources\":[");
        bool first = true;
        foreach (ScriptPackSource source in index.Sources.OrderBy(value => value.Address, StringComparer.Ordinal))
        {
            Separator(text, ref first);
            text.Append("{\"Address\":"); Quote(text, source.Address);
            text.Append(",\"SourceSha256\":"); Quote(text, source.SourceSha256);
            text.Append(",\"SourceBytes\":").Append(source.SourceBytes.ToString(CultureInfo.InvariantCulture)).Append('}');
        }
        text.Append("],\"Messages\":["); first = true;
        foreach (ScriptMessageSchema schema in index.Messages.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            Separator(text, ref first);
            text.Append("{\"Name\":"); Quote(text, schema.Name);
            text.Append(",\"Direction\":"); Quote(text, schema.Direction.ToString());
            text.Append(",\"Version\":1,\"Fields\":[");
            bool firstField = true;
            foreach (ScriptMessageField field in schema.Fields.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                Separator(text, ref firstField);
                text.Append("{\"Name\":"); Quote(text, field.Name);
                text.Append(",\"Type\":"); Quote(text, field.Type);
                text.Append(",\"MaxBytes\":").Append(field.MaxBytes.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            text.Append("]}");
        }
        text.Append("]}");
        byte[] result = Utf8.GetBytes(text.ToString());
        if (result.Length > ScriptPackLimits.MaxIndexBytes) throw Bad("Script index exceeded its byte limit");
        return result;
    }

    private static List<ScriptMessageSchema> ReadMessages(Dictionary<string, object> map)
    {
        var schemas = new List<ScriptMessageSchema>();
        foreach (object value in List(map, "Messages", ScriptMessageLimits.MaxSchemasPerPack, optional: true))
        {
            var schemaMap = Object(value);
            Keys(schemaMap, "Name", "Direction", "Version", "Fields");
            string direction = Text(schemaMap, "Direction");
            int version = schemaMap.ContainsKey("Version") ? Integer(schemaMap, "Version") : 1;
            if (version != 1 || (direction != "ClientToServer" && direction != "ServerToClient")) throw Bad("Unsupported message version or direction");
            var schema = new ScriptMessageSchema { Name = Text(schemaMap, "Name"), Version = (ushort)version,
                Direction = direction == "ClientToServer" ? AnomalyMessageDirection.ClientToServer : AnomalyMessageDirection.ServerToClient };
            foreach (object fieldValue in List(schemaMap, "Fields", ScriptMessageLimits.MaxFields))
            {
                var field = Object(fieldValue);
                Keys(field, "Name", "Type", "MaxBytes");
                schema.Fields.Add(new ScriptMessageField { Name = Text(field, "Name"), Type = Text(field, "Type"), MaxBytes = Integer(field, "MaxBytes", optional: true) });
            }
            schemas.Add(schema);
        }
        ValidateMessages(schemas);
        return schemas;
    }

    private static void ValidateMessages(List<ScriptMessageSchema> schemas)
    {
        if (schemas == null || schemas.Count > ScriptMessageLimits.MaxSchemasPerPack) throw Bad("Script message schema limit exceeded");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScriptMessageSchema schema in schemas)
        {
            if (schema == null || !names.Add(schema.Name)) throw Bad("Invalid or duplicate message schema");
            schema.Validate();
        }
    }

    private static Dictionary<string, object> Read(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > ScriptPackLimits.MaxIndexBytes) throw Bad("Script index exceeded its byte limit");
        // The largest supported tree has 32 schemas with 32 fields each. Apply
        // that shape's allocation limits during parsing, including unknown keys,
        // rather than first allocating the much larger generic asset tree.
        try { return BoundedPackJson.Read(bytes, maxDepth: 5, maxTokens: 4608,
            maxArrayItems: ScriptMessageLimits.MaxSchemasPerPack, maxObjectProperties: 5,
            maxStringLength: AssetAddress.MaxLength); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Script index must use strict UTF-8.", exception); }
    }

    // Validated identities and schema names contain no JSON escape characters.
    private static void Quote(StringBuilder text, string value) => text.Append('"').Append(value).Append('"');
    private static void Separator(StringBuilder text, ref bool first) { if (!first) text.Append(','); first = false; }
    private static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw Bad("Expected JSON object");
    private static void Keys(Dictionary<string, object> map, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (string key in map.Keys) if (!allowed.Contains(key)) throw Bad("Unknown script index property: " + key);
    }
    private static string Text(Dictionary<string, object> map, string key) => map.TryGetValue(key, out object value) && value is string text ? text : throw Bad("Missing text property: " + key);
    private static int Integer(Dictionary<string, object> map, string key, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value)) return optional ? 0 : throw Bad("Missing integer: " + key);
        if (!(value is double number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) throw Bad("Invalid integer: " + key);
        return (int)number;
    }
    private static List<object> List(Dictionary<string, object> map, string key, int maximum, bool optional = false)
    {
        if (!map.TryGetValue(key, out object value)) return optional ? new List<object>() : throw Bad("Missing array: " + key);
        return value is List<object> list && list.Count <= maximum ? list : throw Bad("Invalid array: " + key);
    }
    private static InvalidDataException Bad(string message) => new InvalidDataException(message + ".");
}
