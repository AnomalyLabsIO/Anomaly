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
    public static string SkinsPath { get; }

    /// <summary>
    /// Root for player-supplied local audio overrides. Layout mirrors the
    /// engine's audio lookup path beneath <c>Overrides/AudioClip/</c>
    /// (for example, <c>Overrides/AudioClip/ALERT.ogg</c>).
    /// </summary>
    public static string OverridesPath { get; }
    public static string AudioOverridesPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyUserDataPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        AnomalyPath = AnomalyUserDataPath;
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");

        SkinsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Skins");
        if (!Directory.Exists(AnomalyUserDataPath))
            Directory.CreateDirectory(AnomalyUserDataPath);
        if (!Directory.Exists(SkinsPath))
            Directory.CreateDirectory(SkinsPath);

        OverridesPath = Path.Combine(AnomalyUserDataPath, "Overrides");
        AudioOverridesPath = Path.Combine(OverridesPath, "AudioClip");

        bool createdOverridesRoot = !Directory.Exists(OverridesPath);
        Directory.CreateDirectory(OverridesPath);
        Directory.CreateDirectory(AudioOverridesPath);

        if (createdOverridesRoot)
            WriteOverridesReadme();
    }

    private static void WriteOverridesReadme()
    {
        try
        {
            string readme = Path.Combine(OverridesPath, "README.md");
            if (File.Exists(readme))
                return;

            File.WriteAllText(readme,
                "# Anomaly Asset Overrides\n\n" +
                "Drop local audio replacements here to override stock SCP:SL clips.\n\n" +
                "## Layout\n\n" +
                "Files live under `AudioClip/<unity-path>.<ext>`, where `<unity-path>` mirrors\n" +
                "the path the engine uses to look up the clip.\n\n" +
                "Examples:\n\n" +
                "```\n" +
                "AudioClip/ALERT.ogg              ← replaces CASSIE \"ALERT\" voiceline\n" +
                "AudioClip/SCP/096_idle.ogg       ← replaces SCP-096 idle sound\n" +
                "```\n\n" +
                "See the full asset catalog at https://docs.anomalylabs.io/reference/07-asset-catalog-audio/\n\n" +
                "## Supported formats\n\n" +
                "- Local audio: `.ogg` (Vorbis) or `.wav` (PCM int16/24/32 or float32)\n\n" +
                "Authenticated server content is hash-addressed and does not use or replace\n" +
                "this local path-override surface.\n");
        }
        catch
        {
            // Readme creation is best-effort; never block the static ctor on it.
        }
    }
}
