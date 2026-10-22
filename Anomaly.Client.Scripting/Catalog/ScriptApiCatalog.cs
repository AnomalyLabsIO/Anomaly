using System.Text;
using System.Text.Json;

namespace Anomaly.Client.Scripting.Catalog;

/// <summary>Immutable approved schema shared by descriptor policy, baseline and tooling exports.</summary>
internal sealed class ScriptApiCatalog
{
    internal ScriptApiCatalog(IEnumerable<ScriptTypeCatalog> types, IEnumerable<ScriptEventCatalogEntry>? events = null)
    {
        Types = Array.AsReadOnly(types.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray());
        Events = Array.AsReadOnly((events ?? Array.Empty<ScriptEventCatalogEntry>())
            .OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal).ToArray());
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
        if (Types.GroupBy(value => value.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            Events.GroupBy(value => value.Path, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Catalog names must be unique.");
    }

    public IReadOnlyList<ScriptTypeCatalog> Types { get; }
    public IReadOnlyList<ScriptEventCatalogEntry> Events { get; }
    public bool HasEventsGlobal => Events.Count != 0;

    internal ScriptApiCatalog WithEvents(IEnumerable<ScriptEventCatalogEntry> events) => new(Types, events);

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
        if (HasEventsGlobal) lines.Add("global Events: event-registry");
        foreach (var entry in Events)
        {
            lines.Add($"event {entry.Path}({string.Join(",", entry.Parameters.Select(value => value.Name + ":" + value.ClrType))}) -> {entry.ClrDeclaringType}.{entry.ClrEventName}");
            lines.Add("  method Add(callback:function): System.String");
            lines.Add("  method Remove(callbackOrToken:function|string): System.Boolean");
        }
        return string.Join("\n", lines) + (lines.Count == 0 ? "" : "\n");
    }

    internal string ToJson(IReadOnlyDictionary<string, string>? documentation = null)
    {
        var manifest = new
        {
            schemaVersion = 1,
            globals = HasEventsGlobal ? new[] { new { name = "Events", kind = "event-registry" } } : Array.Empty<object>(),
            types = Types.Select(type => new
            {
                name = type.Name, clrType = type.ClrType, documentationId = type.DocumentationId,
                documentation = Documentation(documentation, type.DocumentationId),
                members = type.Members.Select(member => new
                {
                    kind = member.Kind.ToString(), name = member.Name, clrName = member.ClrName,
                    returnType = member.ReturnType, allowWrite = member.AllowWrite,
                    parameters = member.Parameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }),
                    documentationId = member.DocumentationId,
                    documentation = Documentation(documentation, member.DocumentationId)
                })
            }),
            events = Events.Select(entry => new
            {
                category = entry.Category, name = entry.Name, path = entry.Path,
                clrDeclaringType = entry.ClrDeclaringType, clrEventName = entry.ClrEventName,
                parameters = entry.Parameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }),
                add = new { parameters = entry.AddParameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }), returnType = entry.AddReturnType },
                remove = new { parameters = entry.RemoveParameters.Select(parameter => new { name = parameter.Name, clrType = parameter.ClrType }), returnType = entry.RemoveReturnType },
                documentationId = entry.DocumentationId,
                documentation = Documentation(documentation, entry.DocumentationId)
            })
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    internal string ToLuaDefinitions(IReadOnlyDictionary<string, string>? documentation = null)
    {
        var output = new StringBuilder("---@meta\n-- Generated from the approved script catalog. Definitions only; no host objects are constructed.\n");
        foreach (var type in Types)
        {
            output.AppendLine();
            AppendDocumentation(output, Documentation(documentation, type.DocumentationId));
            output.AppendLine("---@class " + type.Name);
            foreach (var member in type.Members.Where(value => value.Kind == ScriptMemberKind.Property))
            {
                var summary = Documentation(documentation, member.DocumentationId);
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
        if (HasEventsGlobal)
        {
            output.AppendLine("\n-- Host-installed subscription registry; callbacks receive frozen event data.\nEvents = {}");
            foreach (var group in Events.GroupBy(value => value.Category))
            {
                output.AppendLine("Events." + group.Key + " = {}");
                foreach (var entry in group)
                {
                    AppendDocumentation(output, Documentation(documentation, entry.DocumentationId));
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
        return output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private string LuaType(string clrType)
    {
        if (clrType == "System.String") return "string|nil";
        if (clrType == "System.Boolean") return "boolean";
        if (clrType == "System.Void") return "nil";
        if (clrType is "System.Double" or "System.Single") return "number";
        if (clrType is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64") return "integer";
        var type = Types.FirstOrDefault(value => value.ClrType == clrType);
        if (type == null) throw new InvalidOperationException("The catalog contains an unapproved value type.");
        return type.Name + "|nil";
    }

    private static string? Documentation(IReadOnlyDictionary<string, string>? documentation, string id)
        => documentation != null && documentation.TryGetValue(id, out var text) ? text : null;

    private static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
