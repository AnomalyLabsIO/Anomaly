namespace Anomaly.Client.Scripting.Catalog;

internal static class ScriptNameRules
{
    private static readonly HashSet<string> LuaKeywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
    };

    internal static bool IsValidIdentifier(string? name) => !string.IsNullOrEmpty(name) && name.Length <= 64 &&
        !LuaKeywords.Contains(name) && !name.StartsWith("__", StringComparison.Ordinal) &&
        (IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.Skip(1).All(value => IsAsciiLetter(value) || value is >= '0' and <= '9' || value == '_');

    internal static void ValidateIdentifier(string name)
    {
        if (!IsValidIdentifier(name)) throw new ArgumentException("Script names must be bounded, non-reserved ASCII identifiers.", nameof(name));
    }

    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
