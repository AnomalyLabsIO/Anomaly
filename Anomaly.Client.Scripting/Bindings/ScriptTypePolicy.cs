using System.Collections.ObjectModel;
using System.Reflection;
using Anomaly.Client.Scripting.Catalog;

namespace Anomaly.Client.Scripting.Bindings;

internal sealed class ScriptTypePolicy
{
    internal ScriptTypePolicy(Type type, string name, IReadOnlyDictionary<string, ScriptMemberPolicy> members)
    {
        Type = type;
        Members = new ReadOnlyDictionary<string, ScriptMemberPolicy>(new Dictionary<string, ScriptMemberPolicy>(members, StringComparer.Ordinal));
        Catalog = new ScriptTypeCatalog(name, type.FullName ?? type.Name, Members.Values.Select(member => member.Catalog));
    }

    internal Type Type { get; }
    internal string Name => Catalog.Name;
    internal ScriptTypeCatalog Catalog { get; }
    internal IReadOnlyDictionary<string, ScriptMemberPolicy> Members { get; }
}

internal sealed class ScriptMemberPolicy
{
    internal ScriptMemberPolicy(string name, PropertyInfo property, bool allowWrite)
    {
        Property = property;
        ReturnType = property.PropertyType;
        Parameters = Array.Empty<ParameterInfo>();
        Catalog = new ScriptMemberCatalog(ScriptMemberKind.Property, name, property.Name,
            ReturnType.FullName ?? ReturnType.Name, allowWrite, Array.Empty<ScriptParameterCatalog>(),
            "P:" + DocumentationType(property.DeclaringType!) + "." + property.Name);
    }

    internal ScriptMemberPolicy(string name, MethodInfo method)
    {
        Method = method;
        ReturnType = method.ReturnType;
        Parameters = method.GetParameters();
        var parameterTypes = Parameters.Select(parameter => DocumentationType(parameter.ParameterType)).ToArray();
        Catalog = new ScriptMemberCatalog(ScriptMemberKind.Method, name, method.Name,
            ReturnType.FullName ?? ReturnType.Name, false,
            Parameters.Select((parameter, index) => new ScriptParameterCatalog(parameter.Name ?? "arg" + index,
                parameter.ParameterType.FullName ?? parameter.ParameterType.Name)),
            "M:" + DocumentationType(method.DeclaringType!) + "." + method.Name +
            (parameterTypes.Length == 0 ? "" : "(" + string.Join(",", parameterTypes) + ")"));
    }

    internal string Name => Catalog.Name;
    internal ScriptMemberCatalog Catalog { get; }
    internal PropertyInfo? Property { get; }
    internal MethodInfo? Method { get; }
    internal bool AllowWrite => Catalog.AllowWrite;
    internal Type ReturnType { get; }
    internal ParameterInfo[] Parameters { get; }

    private static string DocumentationType(Type type) => (type.FullName ?? type.Name).Replace('+', '.');
}
