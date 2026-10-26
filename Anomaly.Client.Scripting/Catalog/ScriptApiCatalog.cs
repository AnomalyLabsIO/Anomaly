using System.Text;
using System.Text.Json;

namespace Anomaly.Client.Scripting.Catalog;

/// <summary>Immutable approved schema shared by descriptor policy, baseline and tooling exports.</summary>
internal sealed class ScriptApiCatalog
{
    internal ScriptApiCatalog(IEnumerable<ScriptTypeCatalog> types, IEnumerable<ScriptEventCatalogEntry>? events = null,
        bool hasMessagesGlobal = false, IEnumerable<ScriptEnumCatalog>? enums = null)
    {
        HasMessagesGlobal = hasMessagesGlobal;
        Types = Array.AsReadOnly(types.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray());
        Events = Array.AsReadOnly((events ?? Array.Empty<ScriptEventCatalogEntry>())
            .OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal).ToArray());
        Enums = Array.AsReadOnly((enums ?? Array.Empty<ScriptEnumCatalog>())
            .OrderBy(value => value.ClrType, StringComparer.Ordinal).ToArray());
        foreach (var type in Types)
        {
            ScriptNameRules.ValidateIdentifier(type.Name);
            foreach (var member in type.Members) ScriptNameRules.ValidateIdentifier(member.Name);
        }
        foreach (var entry in Events)
        {
            ScriptNameRules.ValidateIdentifier(entry.Category);
            ScriptNameRules.ValidateIdentifier(entry.Name);
        }
        var enumKeys = new Dictionary<string, ScriptEnumCatalog>(StringComparer.Ordinal);
        var enumGlobals = new HashSet<string>(StringComparer.Ordinal) { "Enums", "Events", "Messages", "Game", "Output" };
        foreach (var entry in Enums)
        {
            if (string.IsNullOrEmpty(entry.ClrType) || !enumKeys.TryAdd(entry.ClrType, entry))
                throw new InvalidOperationException("Catalog enum identities must be unique and nonempty.");
            if (entry.Alias != null)
            {
                ScriptNameRules.ValidateIdentifier(entry.Alias);
                if (enumKeys.TryGetValue(entry.Alias, out var existing) && !ReferenceEquals(existing, entry))
                    throw new InvalidOperationException("Catalog enum aliases must be unique.");
                enumKeys[entry.Alias] = entry;
            }
            if (entry.GlobalName != null)
            {
                ScriptNameRules.ValidateIdentifier(entry.GlobalName);
                if (!enumGlobals.Add(entry.GlobalName))
                    throw new InvalidOperationException("Catalog enum globals must be unique and non-reserved.");
            }
            if (entry.Members.GroupBy(value => value.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Catalog enum member names must be unique.");
        }
        if (Types.GroupBy(value => value.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            Events.GroupBy(value => value.Path, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Catalog names must be unique.");
    }

    public IReadOnlyList<ScriptTypeCatalog> Types { get; }
    public IReadOnlyList<ScriptEventCatalogEntry> Events { get; }
    public IReadOnlyList<ScriptEnumCatalog> Enums { get; }
    public bool HasEventsGlobal => Events.Count != 0;
    public bool HasMessagesGlobal { get; }
    public bool HasGameGlobal => Types.Any(type => type.ClrType == typeof(Bindings.ScriptGameContext).FullName);
    public bool HasOutputGlobal => Types.Any(type => type.ClrType == typeof(Output.ScriptOutput).FullName);

    internal ScriptApiCatalog WithEvents(IEnumerable<ScriptEventCatalogEntry> events) => new(Types, events, HasMessagesGlobal, Enums);
    internal ScriptApiCatalog WithMessages() => new(Types, Events, hasMessagesGlobal: true, enums: Enums);

    internal string DescribeSurface()
    {
        var lines = new List<string>();
        foreach (var type in Types)
        {
            lines.Add($"type {type.Name} -> {type.ClrType}");
            foreach (var member in type.Members)
            {
                if (member.Kind == ScriptMemberKind.Property)
                    lines.Add($"  property {member.Name}: {member.ReturnType} {(member.AllowWrite ? "readwrite" : "readonly")}");
                else
                    lines.Add($"  method {member.Name}({string.Join(",", member.Parameters.Select(value => value.ClrType))}): {member.ReturnType}");
            }
        }
        if (HasGameGlobal) lines.Add("global Game: GameContext");
        if (HasOutputGlobal) lines.Add("global Output: ScriptOutput");
        if (Enums.Count != 0) lines.Add("global Enums: readonly-enum-registry");
        foreach (var entry in Enums)
        {
            lines.Add($"enum {EnumPath(entry)} -> {entry.ClrType} string{(entry.IsFlags ? " flags" : "")} case-insensitive-input");
            if (entry.Alias != null && entry.Alias != entry.ClrType) lines.Add($"  alias Enums.{entry.Alias}");
            if (entry.GlobalName != null) lines.Add($"  global {entry.GlobalName}: readonly-enum-constants");
            foreach (var member in entry.Members)
                lines.Add($"  constant {LuaString(member.Name)}: {LuaString(member.Value)} = {member.NumericValue}");
        }
        if (HasEventsGlobal) lines.Add("global Events: event-registry");
        foreach (var entry in Events)
        {
            lines.Add($"event {entry.Path}({string.Join(",", entry.Parameters.Select(value => value.Name + ":" + value.ClrType))}) -> {entry.ClrDeclaringType}.{entry.ClrEventName}");
            lines.Add(entry.CoalesceLatest ? "  delivery queued-latest-per-registration" : "  delivery queued-ordered");
            lines.Add("  method Add(callback:function): System.String");
            lines.Add("  method Remove(callbackOrToken:function|string): System.Boolean");
        }
        if (HasMessagesGlobal)
        {
            lines.Add("global Messages: pack-message-registry");
            lines.Add("  method On(name:string,callback:function): System.String");
            lines.Add("  method Off(subscriptionToken:string): System.Boolean");
            lines.Add("  method Send(name:string,payload:record): boolean,string|nil");
        }
        return string.Join("\n", lines) + (lines.Count == 0 ? "" : "\n");
    }

    internal string ToJson(IReadOnlyDictionary<string, string>? documentation = null)
    {
        var globals = new List<object>();
        if (HasGameGlobal) globals.Add(new { name = "Game", kind = "GameContext" });
        if (HasOutputGlobal) globals.Add(new { name = "Output", kind = "ScriptOutput" });
        if (HasEventsGlobal) globals.Add(new { name = "Events", kind = "event-registry" });
        if (HasMessagesGlobal) globals.Add(new { name = "Messages", kind = "pack-message-registry" });
        if (Enums.Count != 0) globals.Add(new { name = "Enums", kind = "readonly-enum-registry" });
        foreach (var entry in Enums.Where(value => value.GlobalName != null))
            globals.Add(new { name = entry.GlobalName, kind = "readonly-enum-constants", clrType = entry.ClrType });
        var manifest = new
        {
            schemaVersion = 1,
            globals,
            types = Types.Select(type => new
            {
                name = type.Name, clrType = type.ClrType, documentationId = type.DocumentationId,
                documentation = TypeDocumentation(documentation, type),
                members = type.Members.Select(member => new
                {
                    kind = member.Kind.ToString(), name = member.Name, clrName = member.ClrName,
                    returnType = member.ReturnType, allowWrite = member.AllowWrite,
                    parameters = member.Parameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }),
                    documentationId = member.DocumentationId,
                    documentation = MemberDocumentation(documentation, type, member)
                })
            }),
            enums = Enums.Select(entry => new
            {
                clrType = entry.ClrType, path = EnumPath(entry), alias = entry.Alias, globalName = entry.GlobalName,
                isFlags = entry.IsFlags, representation = "string", caseInsensitiveInput = true,
                documentationId = entry.DocumentationId, documentation = Documentation(documentation, entry.DocumentationId),
                members = entry.Members.Select(member => new
                { name = member.Name, value = member.Value, numericValue = member.NumericValue })
            }),
            events = Events.Select(entry => new
            {
                category = entry.Category, name = entry.Name, path = entry.Path,
                clrDeclaringType = entry.ClrDeclaringType, clrEventName = entry.ClrEventName,
                delivery = entry.CoalesceLatest ? "queued-latest-per-registration" : "queued-ordered",
                canCancel = false,
                parameters = entry.Parameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }),
                add = new { parameters = entry.AddParameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }), returnType = entry.AddReturnType },
                remove = new { parameters = entry.RemoveParameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }), returnType = entry.RemoveReturnType },
                documentationId = entry.DocumentationId,
                documentation = EventDocumentation(documentation, entry)
            })
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    internal string ToLuaDefinitions(IReadOnlyDictionary<string, string>? documentation = null)
    {
        var output = new StringBuilder("---@meta\n-- Generated from the approved script catalog. Definitions only; no host objects are constructed.\n");
        if (Enums.Count != 0)
        {
            output.AppendLine("\n-- Host enum registries are read-only. Values are canonical names; enum inputs also accept case-insensitive names.");
            output.AppendLine("-- Flags inputs accept comma-separated declared names and the unnamed zero string '0'; other numeric inputs are rejected.");
            output.AppendLine("Enums = {}");
            foreach (var entry in Enums)
            {
                output.AppendLine();
                AppendDocumentation(output, Documentation(documentation, entry.DocumentationId));
                output.AppendLine("---@enum " + EnumDefinitionName(entry.ClrType));
                output.AppendLine(EnumPath(entry) + " = {");
                foreach (var member in entry.Members)
                    output.AppendLine("    [" + LuaString(member.Name) + "] = " + LuaString(member.Value) + ",");
                output.AppendLine("}");
                if (entry.Alias != null && entry.Alias != entry.ClrType)
                    output.AppendLine("Enums." + entry.Alias + " = " + EnumPath(entry));
                if (entry.GlobalName != null) output.AppendLine(entry.GlobalName + " = " + EnumPath(entry));
            }
        }
        foreach (var type in Types)
        {
            output.AppendLine();
            AppendDocumentation(output, TypeDocumentation(documentation, type));
            output.AppendLine("---@class " + type.Name);
            foreach (var member in type.Members.Where(value => value.Kind == ScriptMemberKind.Property))
            {
                var summary = MemberDocumentation(documentation, type, member);
                output.Append("---@field ").Append(member.Name).Append(' ').Append(LuaType(member.ReturnType));
                output.Append(member.AllowWrite ? " # Writable." : " # Read-only.");
                if (!string.IsNullOrEmpty(summary)) output.Append(' ').Append(OneLine(summary));
                output.AppendLine();
            }
            // A local placeholder describes a userdata type; it does not promise a global constructor or table.
            output.AppendLine("local _type_" + type.Name + " = {}");
            foreach (var member in type.Members.Where(value => value.Kind == ScriptMemberKind.Method))
            {
                AppendDocumentation(output, Documentation(documentation, member.DocumentationId));
                var parameterNames = ParameterNames(member.Parameters);
                foreach (var parameter in member.Parameters.Select((value, index) => (Value: value, Name: parameterNames[index])))
                    output.AppendLine("---@param " + parameter.Name + " " + LuaType(parameter.Value.ClrType));
                if (member.ReturnType != "System.Void") output.AppendLine("---@return " + LuaType(member.ReturnType));
                output.AppendLine("function _type_" + type.Name + ":" + member.Name + "(" + string.Join(", ", parameterNames) + ") end");
            }
        }
        if (HasGameGlobal) output.AppendLine("\n---@type GameContext\nGame = nil -- Host-installed read-only local queries; no constructor is exposed.");
        if (HasOutputGlobal) output.AppendLine("\n---@type ScriptOutput\nOutput = nil -- Host-owned bounded logging and plain text; automatically revoked with this session.");
        if (HasEventsGlobal)
        {
            output.AppendLine("\n-- Host-installed queued subscription registry. Frozen observations cannot cancel or modify the original operation.\nEvents = {}");
            foreach (var group in Events.GroupBy(value => value.Category))
            {
                output.AppendLine("Events." + group.Key + " = {}");
                foreach (var entry in group)
                {
                    AppendDocumentation(output, EventDocumentation(documentation, entry));
                    output.AppendLine(entry.CoalesceLatest
                        ? "--- Latest pending observation per registration; intervening observations may be coalesced."
                        : "--- Ordered queued delivery; callbacks must remain within the documented traffic limits.");
                    output.AppendLine(entry.Path + " = {}");
                    var parameterNames = ParameterNames(entry.Parameters);
                    var callback = "fun(" + string.Join(", ", entry.Parameters.Select((value, index) => parameterNames[index] + ": " + LuaType(value.ClrType))) + ")";
                    output.AppendLine("---@param callback " + callback);
                    output.AppendLine("---@return string subscriptionToken");
                    output.AppendLine("function " + entry.Path + ".Add(callback) end");
                    output.AppendLine("---@param callbackOrToken " + callback + "|string");
                    output.AppendLine("---@return boolean removed");
                    output.AppendLine("function " + entry.Path + ".Remove(callbackOrToken) end");
                }
            }
        }
        if (HasMessagesGlobal)
        {
            output.AppendLine("\n-- Installed for approved pack scripts. Message names and flat primitive record fields come from that pack index.");
            output.AppendLine("Messages = {}");
            output.AppendLine("---@param Name string # Approved server-to-client message name.");
            output.AppendLine("---@param Callback fun(Message: table<string, boolean|number|string>)");
            output.AppendLine("---@return string SubscriptionToken");
            output.AppendLine("function Messages.On(Name, Callback) end");
            output.AppendLine("---@param SubscriptionToken string");
            output.AppendLine("---@return boolean Removed");
            output.AppendLine("function Messages.Off(SubscriptionToken) end");
            output.AppendLine("---@param Name string # Approved client-to-server message name.");
            output.AppendLine("---@param Payload table<string, boolean|number|string> # Exact required fields; no nested tables or metatable.");
            output.AppendLine("---@return boolean Accepted");
            output.AppendLine("---@return string|nil ErrorCode # NotReady, UnknownMessage, WrongDirection, or InvalidPayload.");
            output.AppendLine("function Messages.Send(Name, Payload) end");
        }
        return output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private string LuaType(string clrType)
    {
        const string nullablePrefix = "System.Nullable`1[[";
        if (clrType.StartsWith(nullablePrefix, StringComparison.Ordinal))
        {
            var valueType = LuaType(clrType[nullablePrefix.Length..].Split(',')[0]);
            return valueType.EndsWith("|nil", StringComparison.Ordinal) ? valueType : valueType + "|nil";
        }
        if (clrType == "System.String") return "string|nil";
        if (clrType == "System.Boolean") return "boolean";
        if (clrType == "System.Void") return "nil";
        if (clrType is "System.Double" or "System.Single") return "number";
        if (clrType is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64") return "integer";
        if (Enums.Any(value => value.ClrType == clrType)) return "string";
        var type = Types.FirstOrDefault(value => value.ClrType == clrType);
        if (type == null) throw new InvalidOperationException("The catalog contains an unapproved value type.");
        return type.Name + "|nil";
    }

    private static string? Documentation(IReadOnlyDictionary<string, string>? documentation, string id)
        => documentation != null && documentation.TryGetValue(id, out var text) ? text : null;

    private static bool HasObservedPermission(ScriptTypeCatalog type) =>
        type.Members.Any(member => member.Name == "IsAllowed" && !member.AllowWrite);

    private static string? TypeDocumentation(IReadOnlyDictionary<string, string>? documentation, ScriptTypeCatalog type) =>
        HasObservedPermission(type)
            ? "Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation."
            : Documentation(documentation, type.DocumentationId);

    private static string? MemberDocumentation(IReadOnlyDictionary<string, string>? documentation,
        ScriptTypeCatalog type, ScriptMemberCatalog member) =>
        member.Name == "IsAllowed" && HasObservedPermission(type)
            ? "Observed permission at capture time, not a cancellation control."
            : Documentation(documentation, member.DocumentationId);

    private string EventDocumentation(IReadOnlyDictionary<string, string>? documentation, ScriptEventCatalogEntry entry)
    {
        var observesPermission = entry.Parameters.Any(parameter =>
            Types.Any(type => type.ClrType == parameter.ClrType && HasObservedPermission(type)));
        var summary = observesPermission ? null : Documentation(documentation, entry.DocumentationId);
        const string semantics = "Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.";
        return string.IsNullOrEmpty(summary) ? semantics : summary + "\n" + semantics;
    }

    private static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string EnumPath(ScriptEnumCatalog entry) => "Enums[" + LuaString(entry.ClrType) + "]";

    private static string EnumDefinitionName(string clrType)
    {
        var name = new StringBuilder("Enum_");
        foreach (var value in clrType)
        {
            if (value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') name.Append(value);
            else name.Append('_').Append(((int)value).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)).Append('_');
        }
        return name.ToString();
    }

    private static string LuaString(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var value in text)
        {
            if (value is '\\' or '"') quoted.Append('\\').Append(value);
            else if (char.IsControl(value))
                quoted.Append('\\').Append(((int)value).ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            else quoted.Append(value);
        }
        return quoted.Append('"').ToString();
    }

    private static string[] ParameterNames(IReadOnlyList<ScriptParameterCatalog> parameters)
    {
        var reserved = new HashSet<string>(parameters.Where(value => ScriptNameRules.IsValidIdentifier(value.Name))
            .Select(value => value.Name), StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        return parameters.Select((parameter, index) =>
        {
            if (ScriptNameRules.IsValidIdentifier(parameter.Name) && used.Add(parameter.Name)) return parameter.Name;
            var name = "arg" + index;
            var suffix = 0;
            while (reserved.Contains(name) || !used.Add(name)) name = "arg" + index + "_" + ++suffix;
            return name;
        }).ToArray();
    }

    private static void AppendDocumentation(StringBuilder output, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var line in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            output.AppendLine("--- " + line);
    }
}
