using MelonLoader.Utils;

namespace Anomaly.Client.Api.Features;

public static class Paths
{
    /// <summary>The operating system's application-data directory; not Anomaly's storage root.</summary>
    public static string ApplicationDataPath { get; }
    /// <summary>Instance-local Anomaly storage, equivalent to <see cref="AnomalyUserDataPath"/>.</summary>
    public static string AnomalyPath { get; }
    public static string FileCachePath { get; }
    public static string CrashReportsPath { get; }
    /// <summary>Anomaly's directory beneath MelonLoader's configured UserData directory.</summary>
    public static string AnomalyUserDataPath { get; }
    /// <summary>Completed local .bundle and .index.json pairs, authored offline.</summary>
    public static string AssetBundlesPath { get; }
    /// <summary>Legacy source-image location. Raw skins are no longer loaded or created here.</summary>
    public static string SkinsPath { get; }
    /// <summary>Legacy source-media location, retained for migration tools; no runtime loading.</summary>
    public static string OverridesPath { get; }
    /// <summary>Legacy source-audio location, retained for migration tools; no runtime loading.</summary>
    public static string AudioOverridesPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyUserDataPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        AnomalyPath = AnomalyUserDataPath;
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");
        AssetBundlesPath = Path.Combine(AnomalyPath, "AssetBundles");
        SkinsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Skins");
        OverridesPath = Path.Combine(AnomalyPath, "Overrides");
        AudioOverridesPath = Path.Combine(OverridesPath, "AudioClip");
        Directory.CreateDirectory(AssetBundlesPath);
        try
        {
            string readme = Path.Combine(AssetBundlesPath, "README.md");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    "# Anomaly AssetBundles\n\n" +
                    "Copy completed .bundle and .index.json pairs here. Restart the client after changes.\n" +
                    "Author packs offline with Unity 6000.0.43f1 for Windows x64; the client validates their contents before loading.\n\n" +
                    "Catalog addresses select assets, independently of source folders: game/skin/logo, game/skin/background,\n" +
                    "supported game/cassie/<name> replacements, or mods/<namespace>/<asset> additions.\n" +
                    "Skin replacements are local-only. Bundles cannot supply scripts or prefabs.\n\n" +
                    "Raw PNG/OGG/WAV files are no longer loaded at runtime. Keep originals for offline conversion.\n");
        }
        catch
        {
            // Documentation is best-effort; existing owner instructions are never overwritten.
        }
    }
}
