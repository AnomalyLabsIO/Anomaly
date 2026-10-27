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
    /// <summary>Descriptor-declared local content packs, authored offline.</summary>
    public static string ContentPacksPath { get; }
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
        ContentPacksPath = Path.Combine(AnomalyPath, "ContentPacks");
        SkinsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Skins");
        OverridesPath = Path.Combine(AnomalyPath, "Overrides");
        AudioOverridesPath = Path.Combine(OverridesPath, "AudioClip");
        Directory.CreateDirectory(ContentPacksPath);
        try
        {
            string readme = Path.Combine(ContentPacksPath, "README.md");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    "# Anomaly ContentPacks\n\n" +
                    "Place each pack at <namespace>/<pack>/pack.json. Restart the client after changes.\n" +
                    "A schema-version 1 descriptor declares PackId, Version and optional Scripts, Assets or Data components.\n" +
                    "Scripts declares PackId, EntryPoint and optional Messages; only that Lua entrypoint executes.\n" +
                    "Assets declares PackId and Index; its .index.json declares the adjacent .bundle file.\n" +
                    "Data declares Items, a typed item-definition JSON file consumed by the server.\n" +
                    "Unlisted Lua and index files are never discovered. Missing or invalid descriptors reject the local set.\n\n" +
                    "Author bundles offline with Unity 6000.0.43f1 for Windows x64; native contents are validated before loading.\n" +
                    "Skin replacements are local-only. Bundles cannot supply scripts or prefabs.\n" +
                    "Lua runs only after verified local-host proof; remote scripts require separate consent and qualification.\n" +
                    "Raw PNG/OGG/WAV files are not loaded at runtime. Keep originals for offline conversion.\n");
        }
        catch
        {
            // Documentation is best-effort; existing owner instructions are never overwritten.
        }
    }
}
