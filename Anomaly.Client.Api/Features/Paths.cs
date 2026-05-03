using MelonLoader.Utils;

namespace Anomaly.Client.Api.Features;

public static class Paths
{
    public static string ApplicationDataPath { get; }
    public static string AnomalyPath { get; }

    public static string FileCachePath { get; }
    public static string CrashReportsPath { get; }

    public static string AnomalyUserDataPath { get; }
    public static string SkinsPath { get; }

    /// <summary>
    /// Root for player-supplied asset overrides. Layout mirrors the engine:
    /// <c>Overrides/&lt;AssetKind&gt;/&lt;unity-path&gt;.&lt;ext&gt;</c>
    /// (e.g. <c>Overrides/AudioClip/ALERT.ogg</c>).
    /// </summary>
    public static string OverridesPath { get; }
    public static string AudioOverridesPath { get; }
    public static string TextureOverridesPath { get; }
    public static string SpriteOverridesPath { get; }
    public static string MeshOverridesPath { get; }

    static Paths()
    {
        ApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AnomalyPath = Path.Combine(ApplicationDataPath, "Anomaly");
        FileCachePath = Path.Combine(AnomalyPath, "FileCache");
        CrashReportsPath = Path.Combine(AnomalyPath, "CrashReports");
        if (!Directory.Exists(AnomalyPath))
            Directory.CreateDirectory(AnomalyPath);

        AnomalyUserDataPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        SkinsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "Skins");
        if (!Directory.Exists(AnomalyUserDataPath))
            Directory.CreateDirectory(AnomalyUserDataPath);
        if (!Directory.Exists(SkinsPath))
            Directory.CreateDirectory(SkinsPath);

        OverridesPath = Path.Combine(AnomalyUserDataPath, "Overrides");
        AudioOverridesPath = Path.Combine(OverridesPath, "AudioClip");
        TextureOverridesPath = Path.Combine(OverridesPath, "Texture2D");
        SpriteOverridesPath = Path.Combine(OverridesPath, "Sprite");
        MeshOverridesPath = Path.Combine(OverridesPath, "Mesh");

        bool createdOverridesRoot = !Directory.Exists(OverridesPath);
        Directory.CreateDirectory(OverridesPath);
        Directory.CreateDirectory(AudioOverridesPath);
        Directory.CreateDirectory(TextureOverridesPath);
        Directory.CreateDirectory(SpriteOverridesPath);
        Directory.CreateDirectory(MeshOverridesPath);

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
                "Drop your replacement files here to override stock SCP:SL assets.\n\n" +
                "## Layout\n\n" +
                "Files live under `<AssetKind>/<unity-path>.<ext>` where `<AssetKind>` matches\n" +
                "the engine's asset kind (`AudioClip`, `Texture2D`, `Sprite`, `Mesh`) and\n" +
                "`<unity-path>` mirrors the path the engine uses to look up the asset.\n\n" +
                "Examples:\n\n" +
                "```\n" +
                "AudioClip/ALERT.ogg              ← replaces CASSIE \"ALERT\" voiceline\n" +
                "AudioClip/SCP/096_idle.ogg       ← replaces SCP-096 idle sound\n" +
                "Texture2D/UI/Loading.png         ← replaces a UI loading texture (Phase 2)\n" +
                "```\n\n" +
                "See the full asset catalog at https://docs.anomalylabs.io/reference/07-asset-catalog-audio/\n\n" +
                "## Supported formats\n\n" +
                "- Audio: `.ogg` (Vorbis, native game format), `.wav` (PCM int16/24/32, float32)\n" +
                "- Textures: `.png`\n" +
                "- Meshes: not yet supported\n\n" +
                "## Precedence\n\n" +
                "When a server pushes an override at the same path as your local file, the\n" +
                "server wins by default. Servers may opt-in per asset to allow your local\n" +
                "file to take priority.\n");

            string meshReadme = Path.Combine(MeshOverridesPath, "README.md");
            if (!File.Exists(meshReadme))
            {
                File.WriteAllText(meshReadme,
                    "Mesh overrides are not yet supported. Use AssetBundles via the Anomaly\n" +
                    "spawn pipeline if you need custom geometry today.\n");
            }
        }
        catch
        {
            // Readme creation is best-effort; never block the static ctor on it.
        }
    }
}
