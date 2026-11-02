namespace Anomaly.Client.Scripting.Remote.Protocol;

/// <summary>Shared host/worker rules for script-visible enum paths and namespace-only nodes.</summary>
internal static class EnumNameRules
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
    };

    internal static IReadOnlySet<string> ReservedRoots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "_G", "_VERSION", "assert", "error", "select", "type", "tonumber", "tostring", "ipairs", "pairs", "next",
        "pcall", "xpcall", "math", "table", "string", "Enums", "Events", "Messages", "Game", "Output", "io", "os", "package",
        "debug", "require", "load", "loadfile", "dofile", "coroutine", "collectgarbage", "rawget", "rawset",
        "getmetatable", "setmetatable", "print"
    };

    internal static string Clean(string clrName)
    {
        var name = clrName;
        while (name.StartsWith("Il2Cpp", StringComparison.Ordinal)) name = name[6..].TrimStart('.');
        return name.Replace('+', '.');
    }

    internal static bool IsIdentifier(string name) => name.Length is > 0 and <= 128 && !Keywords.Contains(name) &&
        !name.StartsWith("__", StringComparison.Ordinal) &&
        (IsLetter(name[0]) || name[0] == '_') &&
        name.Skip(1).All(value => IsLetter(value) || value is >= '0' and <= '9' || value == '_');

    internal static bool IsPath(string path) => path.Length is > 0 and <= 4096 && path.Split('.').All(IsIdentifier);
    internal static string Root(string path) => path.Split('.')[0];
    internal static bool Conflicts(string first, string second) => first == second ||
        first.StartsWith(second + ".", StringComparison.Ordinal) || second.StartsWith(first + ".", StringComparison.Ordinal);
    private static bool IsLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

/// <summary>A preflighted enum path cannot be both a constant registry and a namespace.</summary>
internal sealed class EnumPathNode<T> where T : class
{
    internal EnumPathNode(T? value = null) { Value = value; }
    internal T? Value { get; private set; }
    internal Dictionary<string, EnumPathNode<T>> Children { get; } = new(StringComparer.Ordinal);

    internal EnumPathNode<T> Add(string path, T value)
    {
        if (!EnumNameRules.IsPath(path)) throw new InvalidOperationException("Invalid Lua enum path: " + path);
        var node = this;
        foreach (var part in path.Split('.'))
        {
            if (node.Value != null) throw new InvalidOperationException("An enum path conflicts with a namespace: " + path);
            if (!node.Children.TryGetValue(part, out var child)) node.Children.Add(part, child = new());
            node = child;
        }
        if (node.Children.Count != 0 || node.Value != null && !ReferenceEquals(node.Value, value))
            throw new InvalidOperationException("Conflicting Lua enum path: " + path);
        node.Value = value;
        return node;
    }
}
