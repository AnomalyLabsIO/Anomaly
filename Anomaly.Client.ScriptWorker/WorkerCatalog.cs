using Anomaly.Client.Scripting.Remote.Protocol;

namespace Anomaly.Client.ScriptWorker;

internal sealed class WorkerCatalog
{
    internal Dictionary<string, Dictionary<string, Member>> Types { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, IReadOnlyDictionary<string, WireValue>> Enums { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> EnumGlobals { get; } = new(StringComparer.Ordinal);
    internal EnumPathNode<IReadOnlyDictionary<string, WireValue>> EnumPaths { get; } = new();
    internal EnumPathNode<IReadOnlyDictionary<string, WireValue>> EnumGlobalPaths { get; } = new();
    internal Dictionary<string, Dictionary<string, Event>> Events { get; } = new(StringComparer.Ordinal);

    internal WorkerCatalog(WireValue data)
    {
        Wire.Shape(data, "types", "enums", "enumAliases", "enumGlobals", "events");
        foreach (var type in data.Required("types").RequireArray(512))
        {
            Wire.Shape(type, "name", "members");
            var name = type.Required("name").RequireString(128);
            var members = new Dictionary<string, Member>(StringComparer.Ordinal);
            foreach (var member in type.Required("members").RequireArray(1024))
            {
                Wire.Shape(member, "name", "method", "argc");
                var memberName = member.Required("name").RequireString(128);
                var method = member.Required("method").RequireBoolean();
                var argc = (int)member.Required("argc").RequireInteger(0, 16);
                if (!method && argc != 0 || !members.TryAdd(memberName, new Member(memberName, method, argc)))
                    throw new InvalidDataException("Invalid member metadata.");
            }
            if (!Types.TryAdd(name, members)) throw new InvalidDataException("Duplicate type alias.");
        }
        foreach (var entry in data.Required("enums").RequireObject(2048))
        {
            if (entry.Key.Length is 0 or > 4096 || EnumNameRules.Clean(entry.Key) != entry.Key)
                throw new InvalidDataException("Invalid enum identity.");
            var members = entry.Value.RequireObject(2048);
            foreach (var member in members)
            {
                if (member.Key.Length > 128) throw new InvalidDataException("Invalid enum member.");
                member.Value.RequireString(4096);
            }
            Enums.Add(entry.Key, members);
            if (EnumNameRules.IsPath(entry.Key)) AddEnumPath(EnumPaths, entry.Key, members);
        }
        var canonicalEnums = new HashSet<string>(Enums.Keys, StringComparer.Ordinal);
        foreach (var entry in data.Required("enumAliases").RequireObject(1024))
        {
            var canonical = entry.Value.RequireString(4096);
            if (entry.Key.Length > 4096 || !EnumNameRules.IsPath(entry.Key) || !canonicalEnums.Contains(canonical) ||
                !Enums.TryGetValue(canonical, out var constants))
                throw new InvalidDataException("Invalid enum alias.");
            if (Enums.TryGetValue(entry.Key, out var existing))
            {
                if (!ReferenceEquals(existing, constants)) throw new InvalidDataException("Conflicting enum alias.");
            }
            else Enums.Add(entry.Key, constants);
            AddEnumPath(EnumPaths, entry.Key, constants);
        }
        foreach (var entry in data.Required("enumGlobals").RequireObject(1024))
        {
            var canonical = entry.Value.RequireString(4096);
            if (entry.Key.Length > 4096 || !EnumNameRules.IsPath(entry.Key) ||
                EnumNameRules.ReservedRoots.Contains(EnumNameRules.Root(entry.Key)) || !canonicalEnums.Contains(canonical) ||
                !Enums.TryGetValue(canonical, out var constants))
                throw new InvalidDataException("Invalid enum global.");
            EnumGlobals.Add(entry.Key, canonical);
            AddEnumPath(EnumGlobalPaths, entry.Key, constants);
        }
        foreach (var item in data.Required("events").RequireArray(512))
        {
            Wire.Shape(item, "category", "name", "coalesce", "arity");
            var category = item.Required("category").RequireString(128);
            var name = item.Required("name").RequireString(128);
            var coalesce = item.Required("coalesce").RequireBoolean();
            var arity = (int)item.Required("arity").RequireInteger(0, 1);
            if (!Events.TryGetValue(category, out var members)) Events.Add(category, members = new(StringComparer.Ordinal));
            if (!members.TryAdd(name, new Event(category + "." + name, arity, coalesce)))
                throw new InvalidDataException("Duplicate event metadata.");
        }
    }

    private static void AddEnumPath(EnumPathNode<IReadOnlyDictionary<string, WireValue>> root,
        string path, IReadOnlyDictionary<string, WireValue> constants)
    {
        try { root.Add(path, constants); }
        catch (InvalidOperationException exception)
        { throw new InvalidDataException("Conflicting enum path metadata.", exception); }
    }

    internal sealed record Member(string Name, bool Method, int ArgumentCount);
    internal sealed record Event(string Name, int Arity, bool Coalesce);
}

internal sealed record MessageSchema(string Name, int Direction, IReadOnlyList<MessageField> Fields)
{
    internal static Dictionary<string, MessageSchema> Read(WireValue data)
    {
        var result = new Dictionary<string, MessageSchema>(StringComparer.Ordinal);
        foreach (var schema in data.RequireArray(32))
        {
            Wire.Shape(schema, "name", "direction", "fields");
            var name = schema.Required("name").RequireString(64);
            if (!IsName(name)) throw new InvalidDataException("Invalid message schema name.");
            var direction = (int)schema.Required("direction").RequireInteger(1, 2);
            var fields = new List<MessageField>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in schema.Required("fields").RequireArray(32))
            {
                Wire.Shape(field, "name", "type", "maxBytes");
                var fieldName = field.Required("name").RequireString(64);
                var type = field.Required("type").RequireString(16);
                var maximum = (int)field.Required("maxBytes").RequireInteger(0, 1024);
                if (type is not ("Boolean" or "Int32" or "UInt32" or "Number" or "String") || !IsName(fieldName) ||
                    !names.Add(fieldName) || (type == "String" ? maximum < 1 : maximum != 0))
                    throw new InvalidDataException("Invalid message schema field.");
                fields.Add(new MessageField(fieldName, type, maximum));
            }
            if (!result.TryAdd(name, new MessageSchema(name, direction, fields)))
                throw new InvalidDataException("Duplicate message schema.");
        }
        return result;
    }

    private static bool IsName(string name) => name.Length is > 0 and <= 64 && name[0] is >= 'A' and <= 'Z' &&
        name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
}
internal sealed record MessageField(string Name, string Type, int MaximumBytes);
