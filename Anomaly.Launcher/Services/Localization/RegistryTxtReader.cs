namespace Anomaly.Launcher.Services.Localization;

/// <summary>
/// Reads the SCPSL registry.txt at %APPDATA%/SCP Secret Laboratory/registry.txt to
/// find the player's language preference (the translation_path field).
/// </summary>
internal static class RegistryTxtReader
{
    private const string Separator = "::-%(|::";

    /// <summary>
    /// Returns the normalized locale code from the SCPSL registry, or null if not readable.
    /// Examples:
    ///   "English (default)"   -> "en"
    ///   "fr"                   -> "fr"
    ///   "pt_BR"                -> "pt_BR" (caller handles prefix fallback)
    ///   "zh_Hans"              -> "zh_Hans"
    /// </summary>
    public static string? ReadLocale()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SCP Secret Laboratory",
                "registry.txt");

            if (!File.Exists(path))
                return null;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // Format: <2-char-prefix><key>::-%(|::<value>
                // e.g.: 13translation_path::-%(|::English (default)
                if (line.Length <= 2) continue;

                var keyAndValue = line.AsSpan(2);
                var sepIndex = keyAndValue.IndexOf(Separator);
                if (sepIndex < 0) continue;

                var key = keyAndValue.Slice(0, sepIndex).ToString();
                if (key != "translation_path") continue;

                var value = keyAndValue.Slice(sepIndex + Separator.Length).ToString().Trim();
                return Normalize(value);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string raw)
    {
        // Special case: SCPSL writes "English (default)" for English.
        if (raw.Equals("English (default)", StringComparison.OrdinalIgnoreCase))
            return "en";

        return raw;
    }
}
