using System;
using System.IO;

namespace Anomaly.Client.Api.Internal.Localization;

/// <summary>
/// Reads the SCPSL registry.txt at %APPDATA%/SCP Secret Laboratory/registry.txt
/// to find the player's language preference (translation_path field).
/// Internal — not part of the public mod API.
/// </summary>
internal static class RegistryTxtReader
{
    private const string Separator = "::-%(|::";

    /// <summary>
    /// Returns the normalized locale code from the SCPSL registry, or null if unreadable.
    /// "English (default)" → "en"; otherwise uses the raw folder name (e.g., "fr", "pt_BR").
    /// </summary>
    public static string ReadLocale()
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

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length <= 2) continue;

                var keyAndValue = line.Substring(2);
                var sepIndex = keyAndValue.IndexOf(Separator, StringComparison.Ordinal);
                if (sepIndex < 0) continue;

                var key = keyAndValue.Substring(0, sepIndex);
                if (key != "translation_path") continue;

                var value = keyAndValue.Substring(sepIndex + Separator.Length).Trim();
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
        if (string.Equals(raw, "English (default)", StringComparison.OrdinalIgnoreCase))
            return "en";
        return raw;
    }
}
