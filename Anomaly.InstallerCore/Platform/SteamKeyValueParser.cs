namespace Anomaly.InstallerCore.Platform;

/// <summary>
/// Mirror of Anomaly.Launcher.Services.AcfParser.
/// Launcher and Installer intentionally stay separate, so Steam metadata parsing
/// is duplicated here instead of introducing a shared desktop helper project.
/// </summary>
public static class SteamKeyValueParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sectionStack = new Stack<string>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line == "{")
                continue;

            if (line == "}")
            {
                if (sectionStack.Count > 0)
                    sectionStack.Pop();
                continue;
            }

            List<string> tokens = ExtractQuotedStrings(line);
            if (tokens.Count == 1)
            {
                sectionStack.Push(tokens[0]);
            }
            else if (tokens.Count == 2)
            {
                string fullKey = sectionStack.Count > 0
                    ? string.Join('.', sectionStack.Reverse()) + "." + tokens[0]
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
        bool inQuote = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '"')
                continue;

            if (!inQuote)
            {
                inQuote = true;
                start = i + 1;
                continue;
            }

            tokens.Add(line[start..i]);
            inQuote = false;
        }

        return tokens;
    }
}
