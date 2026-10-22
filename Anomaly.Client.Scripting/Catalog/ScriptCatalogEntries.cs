namespace Anomaly.Client.Scripting.Catalog;

internal enum ScriptMemberKind { Property, Method }

internal sealed class ScriptParameterCatalog
{
    internal ScriptParameterCatalog(string name, string clrType)
    {
        Name = name;
        ClrType = clrType;
    }

    public string Name { get; }
    public string ClrType { get; }
}

internal sealed class ScriptMemberCatalog
{
    internal ScriptMemberCatalog(ScriptMemberKind kind, string name, string clrName, string returnType,
        bool allowWrite, IEnumerable<ScriptParameterCatalog> parameters, string documentationId)
    {
        Kind = kind;
        Name = name;
        ClrName = clrName;
        ReturnType = returnType;
        AllowWrite = allowWrite;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        DocumentationId = documentationId;
    }

    public ScriptMemberKind Kind { get; }
    public string Name { get; }
    public string ClrName { get; }
    public string ReturnType { get; }
    public bool AllowWrite { get; }
    public IReadOnlyList<ScriptParameterCatalog> Parameters { get; }
    public string DocumentationId { get; }
}

internal sealed class ScriptTypeCatalog
{
    internal ScriptTypeCatalog(string name, string clrType, IEnumerable<ScriptMemberCatalog> members)
    {
        Name = name;
        ClrType = clrType;
        Members = Array.AsReadOnly(members.OrderBy(member => member.Name, StringComparer.Ordinal).ToArray());
    }

    public string Name { get; }
    public string ClrType { get; }
    public IReadOnlyList<ScriptMemberCatalog> Members { get; }
    public string DocumentationId => "T:" + ClrType.Replace('+', '.');
}

internal sealed class ScriptEventCatalogEntry
{
    internal ScriptEventCatalogEntry(string category, string name, string clrDeclaringType, string clrEventName,
        IEnumerable<ScriptParameterCatalog> parameters)
    {
        Category = category;
        Name = name;
        ClrDeclaringType = clrDeclaringType;
        ClrEventName = clrEventName;
        Parameters = Array.AsReadOnly(parameters.ToArray());
    }

    public string Category { get; }
    public string Name { get; }
    public string ClrDeclaringType { get; }
    public string ClrEventName { get; }
    public IReadOnlyList<ScriptParameterCatalog> Parameters { get; }
    public string Path => "Events." + Category + "." + Name;
    public string DocumentationId => "E:" + ClrDeclaringType.Replace('+', '.') + "." + ClrEventName;
    public IReadOnlyList<ScriptParameterCatalog> AddParameters { get; } =
        Array.AsReadOnly(new[] { new ScriptParameterCatalog("callback", "function") });
    public string AddReturnType => "System.String";
    public IReadOnlyList<ScriptParameterCatalog> RemoveParameters { get; } =
        Array.AsReadOnly(new[] { new ScriptParameterCatalog("callbackOrToken", "function|string") });
    public string RemoveReturnType => "System.Boolean";
}
