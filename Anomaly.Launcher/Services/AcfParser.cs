namespace Anomaly.Launcher.Services;

/// <summary>
/// Parses Valve KeyValue (ACF/VDF) text format into a flat dictionary
/// with dot-separated keys (e.g., "AppState.buildid", "AppState.UserConfig.betakey").
/// </summary>
public static class AcfParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sectionStack = new Stack<string>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line == "{")
                continue;

            if (line == "}")
            {
                if (sectionStack.Count > 0)
                    sectionStack.Pop();
                continue;
            }

            var tokens = ExtractQuotedStrings(line);

            if (tokens.Count == 1)
            {
                // Section header — push onto stack
                sectionStack.Push(tokens[0]);
            }
            else if (tokens.Count == 2)
            {
                // Key-value pair
                var fullKey = sectionStack.Count > 0
                    ? string.Join(".", sectionStack.Reverse()) + "." + tokens[0]
                    : tokens[0];
                result[fullKey] = tokens[1];
            }
        }

        return result;
    }

    public static Dictionary<string, string> ParseFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static List<string> ExtractQuotedStrings(string line)
    {
        var tokens = new List<string>();
        var inQuote = false;
        var start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (!inQuote)
                {
                    inQuote = true;
                    start = i + 1;
                }
                else
                {
                    tokens.Add(line[start..i]);
                    inQuote = false;
                }
            }
        }

        return tokens;
    }
}
