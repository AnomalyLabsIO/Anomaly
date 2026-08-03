using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NVorbis;

namespace Anomaly.Tools.AssetCatalog;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: AssetCatalog <path-to-ExportedProject> [--out <dir>] [--docs <anomaly-docs-root>]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Walks an AssetRipper export and emits asset catalogs (audio + texture + mesh)");
            Console.Error.WriteLine("as JSON to <out>/ and as Starlight .mdx pages into <docs>/src/content/docs/reference/.");
            return 64;
        }

        string exportRoot = Path.GetFullPath(args[0]);
        string outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
        string? docsRoot = null;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--docs" when i + 1 < args.Length: docsRoot = args[++i]; break;
            }
        }

        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);

        if (!Directory.Exists(exportRoot))
        {
            Console.Error.WriteLine($"error: export root does not exist: {exportRoot}");
            return 1;
        }

        Console.WriteLine($"Export root: {exportRoot}");
        Console.WriteLine($"Output dir : {outDir}");
        if (docsRoot != null)
            Console.WriteLine($"Docs root  : {Path.GetFullPath(docsRoot)}");
        Console.WriteLine();

        var audio = AudioCatalog.Build(exportRoot);
        Console.WriteLine($"Audio    : {audio.Count} entries ({audio.Count(e => e.Category == "Cassie")} CASSIE).");

        var textures = PlaceholderCatalog.BuildTextures(exportRoot);
        Console.WriteLine($"Textures : {textures.Count} entries (engine-loadable scope).");

        var meshes = PlaceholderCatalog.BuildMeshes(exportRoot);
        Console.WriteLine($"Meshes   : {meshes.Count} entries (enumeration only — overrides not yet supported).");

        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        File.WriteAllText(Path.Combine(outDir, "audio_catalog.json"), JsonSerializer.Serialize(audio, jsonOpts));
        File.WriteAllText(Path.Combine(outDir, "texture_catalog.json"), JsonSerializer.Serialize(textures, jsonOpts));
        File.WriteAllText(Path.Combine(outDir, "mesh_catalog.json"), JsonSerializer.Serialize(meshes, jsonOpts));
        Console.WriteLine($"Wrote JSON catalogs to {outDir}");

        if (docsRoot != null)
        {
            string docsRefDir = Path.Combine(docsRoot, "src", "content", "docs", "reference");
            if (!Directory.Exists(docsRefDir))
            {
                Console.Error.WriteLine($"warning: docs reference dir not found: {docsRefDir}");
            }
            else
            {
                File.WriteAllText(Path.Combine(docsRefDir, "07-asset-catalog-audio.mdx"), AudioCatalog.RenderMdx(audio, exportRoot));
                File.WriteAllText(Path.Combine(docsRefDir, "08-asset-catalog-textures.mdx"), PlaceholderCatalog.RenderTexturesMdx(textures, exportRoot));
                File.WriteAllText(Path.Combine(docsRefDir, "09-asset-catalog-meshes.mdx"), PlaceholderCatalog.RenderMeshesMdx(meshes, exportRoot));
                Console.WriteLine($"Wrote .mdx pages to {docsRefDir}");
            }
        }

        return 0;
    }
}

// ---------------------------------------------------------------------------
// Audio catalog
// ---------------------------------------------------------------------------

internal sealed record AudioEntry(
    string Kind,
    string AssetPath,
    string OverrideAt,
    string Category,
    string? CassieType,
    string? ApiName,
    double? Duration,
    int Channels,
    int SampleRate,
    long Size,
    string Sha256);

internal static class AudioCatalog
{
    public static List<AudioEntry> Build(string exportRoot)
    {
        string audioDir = Path.Combine(exportRoot, "Assets", "AudioClip");
        if (!Directory.Exists(audioDir))
        {
            Console.Error.WriteLine($"warning: missing {audioDir} — audio catalog will be empty.");
            return new List<AudioEntry>();
        }

        // 1. guid -> filename map from .meta sidecars.
        var guidToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in Directory.EnumerateFiles(audioDir, "*.ogg.meta", SearchOption.AllDirectories))
        {
            var guid = ParseGuid(meta);
            if (guid != null)
            {
                var oggPath = meta.Substring(0, meta.Length - ".meta".Length);
                guidToFile[guid] = oggPath;
            }
        }

        // 2. parse Cassie Words.asset to get ApiName -> (guid, type, duration).
        var cassieByGuid = new Dictionary<string, (string ApiName, int Type, double Duration)>(StringComparer.OrdinalIgnoreCase);
        string cassieAsset = Path.Combine(exportRoot, "Assets", "MonoBehaviour", "Cassie Words.asset");
        if (File.Exists(cassieAsset))
        {
            foreach (var entry in CassieWordsParser.Parse(cassieAsset))
                cassieByGuid[entry.Guid] = (entry.ApiName, entry.Type, entry.Duration);
        }
        else
        {
            Console.Error.WriteLine($"warning: missing {cassieAsset} — CASSIE entries will not be cross-referenced.");
        }

        // 3. enumerate every .ogg, decode header, classify.
        var bag = new ConcurrentBag<AudioEntry>();
        var oggFiles = Directory.EnumerateFiles(audioDir, "*.ogg", SearchOption.AllDirectories).ToArray();
        Console.WriteLine($"Decoding {oggFiles.Length} .ogg files...");

        Parallel.ForEach(oggFiles, ogg =>
        {
            try
            {
                string rel = Path.GetRelativePath(audioDir, ogg).Replace('\\', '/');
                string nameNoExt = StripExt(rel);
                string assetPath = "AudioClip/" + nameNoExt;
                string overrideAt = "Overrides/AudioClip/" + rel;

                long size = new FileInfo(ogg).Length;
                string sha = HashFile(ogg);

                int channels = 0;
                int sampleRate = 0;
                double? duration = null;
                try
                {
                    using var fs = File.OpenRead(ogg);
                    using var v = new VorbisReader(fs, closeOnDispose: false);
                    channels = v.Channels;
                    sampleRate = v.SampleRate;
                    duration = v.TotalTime.TotalSeconds;
                }
                catch
                {
                    // leave header fields as zero/null; record still useful for the listing
                }

                string? guid = TryGuidForFile(ogg);
                string category;
                string? apiName = null;
                string? cassieType = null;

                if (guid != null && cassieByGuid.TryGetValue(guid, out var c))
                {
                    category = "Cassie";
                    apiName = c.ApiName;
                    cassieType = c.Type switch
                    {
                        0 => "Word",
                        1 => "Glitch",
                        2 => "Other",
                        _ => "Type" + c.Type,
                    };
                    // Prefer the database-recorded duration when available.
                    if (c.Duration > 0)
                        duration = c.Duration;

                    // CASSIE clips are looked up by ApiName at runtime, NOT by the
                    // on-disk filename — the database resolves ApiName -> GUID -> file.
                    // So the override path must mirror the ApiName, not the file name
                    // (e.g. ApiName ".G7" -> ".G7.ogg", even though the on-disk source
                    // file is "welk_ree.ogg").
                    assetPath = "AudioClip/" + apiName;
                    overrideAt = "Overrides/AudioClip/" + apiName + ".ogg";
                }
                else
                {
                    category = ClassifyNonCassie(nameNoExt);
                }

                bag.Add(new AudioEntry(
                    Kind: "AudioClip",
                    AssetPath: assetPath,
                    OverrideAt: overrideAt,
                    Category: category,
                    CassieType: cassieType,
                    ApiName: apiName,
                    Duration: duration,
                    Channels: channels,
                    SampleRate: sampleRate,
                    Size: size,
                    Sha256: sha));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error processing {ogg}: {ex.Message}");
            }
        });

        return bag.OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(e => e.AssetPath, StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static string? ParseGuid(string metaPath)
    {
        try
        {
            foreach (var line in File.ReadLines(metaPath))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(5).Trim();
            }
        }
        catch { }
        return null;
    }

    private static string? TryGuidForFile(string assetFile)
    {
        var meta = assetFile + ".meta";
        return File.Exists(meta) ? ParseGuid(meta) : null;
    }

    private static string ClassifyNonCassie(string nameNoExt)
    {
        // Folder-first, then filename heuristics. Catch-all is "Other".
        string lower = nameNoExt.ToLowerInvariant();
        if (lower.StartsWith("scp/") || System.Text.RegularExpressions.Regex.IsMatch(lower, @"^\d{2,4}[/_ ]"))
            return "SCP";
        if (lower.Contains("/weapons/") || lower.Contains("fire") || lower.Contains("dryfire") || lower.Contains("reload") || lower.Contains("e11sr") || lower.Contains("crossvec") || lower.Contains("logicer"))
            return "Weapons";
        if (lower.Contains("/ambient") || lower.StartsWith("ambient") || lower.Contains("background"))
            return "Ambient";
        if (lower.Contains("/items/") || lower.Contains("inspect") || lower.Contains("equip") || lower.Contains("pickup"))
            return "Items";
        if (lower.Contains("/ui/") || lower.Contains("alarm") || lower.Contains("decont") || lower.Contains("keycard") || lower.Contains("button"))
            return "UI";
        return "Other";
    }

    private static string StripExt(string p)
    {
        int dot = p.LastIndexOf('.');
        int slash = p.LastIndexOf('/');
        return dot > slash && dot >= 0 ? p.Substring(0, dot) : p;
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    // -----------------------------------------------------------------------
    // Markdown rendering
    // -----------------------------------------------------------------------

    public static string RenderMdx(List<AudioEntry> entries, string exportRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: Asset Catalog — Audio");
        sb.AppendLine("description: Every overridable audio clip in SCP:SL by replacement path. Drop a file at the listed path under UserData/Anomaly/Overrides/ to replace it.");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"{{/* @generated by tools/AssetCatalog from {Path.GetFileName(exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))} on {DateTime.UtcNow:yyyy-MM-dd}. Do not edit manually. */}}");
        sb.AppendLine();
        sb.AppendLine("import { Aside } from '@astrojs/starlight/components';");
        sb.AppendLine();
        sb.AppendLine("This page lists every overridable audio clip in SCP:SL. Drop your replacement file at the listed override path under `MelonLoader/UserData/Anomaly/Overrides/` to substitute it. Both `.ogg` (Vorbis, the game's native format) and `.wav` (PCM int16/24/32 or float32) are accepted.");
        sb.AppendLine();
        sb.AppendLine("<Aside type=\"note\" title=\"Precedence\">");
        sb.AppendLine("When the server pushes an override at the same path, the server wins by default. Servers can opt-in per asset to allow your local override to take priority — see the [Asset and File-Transfer Lifecycle](/reference/04-asset-lifecycle/) chapter.");
        sb.AppendLine("</Aside>");
        sb.AppendLine();
        sb.AppendLine($"**Total entries:** {entries.Count}.");
        sb.AppendLine();

        var groups = entries.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                            .OrderBy(g => CategoryOrder(g.Key));

        foreach (var grp in groups)
        {
            sb.AppendLine($"## {grp.Key} ({grp.Count()})");
            sb.AppendLine();

            if (grp.Key.Equals("Cassie", StringComparison.OrdinalIgnoreCase))
            {
                var byType = grp.GroupBy(e => e.CassieType ?? "Word").OrderBy(g => g.Key);
                foreach (var typeGroup in byType)
                {
                    sb.AppendLine($"### {typeGroup.Key} ({typeGroup.Count()})");
                    sb.AppendLine();
                    sb.AppendLine("| ApiName | Override Path | Duration |");
                    sb.AppendLine("| --- | --- | --- |");
                    foreach (var e in typeGroup.OrderBy(e => e.ApiName, StringComparer.OrdinalIgnoreCase))
                    {
                        string dur = e.Duration.HasValue ? $"{e.Duration.Value.ToString("0.00", CultureInfo.InvariantCulture)}s" : "—";
                        sb.AppendLine($"| `{Escape(e.ApiName ?? "")}` | `{Escape(e.OverrideAt)}` | {dur} |");
                    }
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("| Asset | Override Path | Duration |");
                sb.AppendLine("| --- | --- | --- |");
                foreach (var e in grp.OrderBy(e => e.AssetPath, StringComparer.OrdinalIgnoreCase))
                {
                    string dur = e.Duration.HasValue ? $"{e.Duration.Value.ToString("0.00", CultureInfo.InvariantCulture)}s" : "—";
                    sb.AppendLine($"| `{Escape(e.AssetPath)}` | `{Escape(e.OverrideAt)}` | {dur} |");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static int CategoryOrder(string c) => c.ToLowerInvariant() switch
    {
        "cassie" => 0,
        "scp" => 1,
        "weapons" => 2,
        "ambient" => 3,
        "items" => 4,
        "ui" => 5,
        _ => 9,
    };

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("`", "\\`");
}

// ---------------------------------------------------------------------------
// Cassie Words.asset parser
// ---------------------------------------------------------------------------

internal sealed record CassieWordEntry(string ApiName, string Guid, int Type, double Duration);

internal static class CassieWordsParser
{
    /// <summary>
    /// Hand-rolls the parse rather than going through YamlDotNet because Unity's
    /// `.asset` files use `%TAG !u! tag:unity3d.com,2011:` headers that confuse
    /// generic YAML deserializers. The body is regular enough that line-by-line
    /// extraction is more reliable than fighting the parser.
    /// </summary>
    public static IEnumerable<CassieWordEntry> Parse(string assetPath)
    {
        string? apiName = null;
        string? guid = null;
        int type = 0;
        double duration = 0;

        foreach (var raw in File.ReadLines(assetPath))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("- ApiName:", StringComparison.Ordinal))
            {
                if (apiName != null && guid != null)
                    yield return new CassieWordEntry(apiName, guid, type, duration);

                apiName = line.Substring("- ApiName:".Length).Trim();
                guid = null;
                type = 0;
                duration = 0;
            }
            else if (line.StartsWith("Clip:", StringComparison.Ordinal))
            {
                int gIdx = line.IndexOf("guid:", StringComparison.Ordinal);
                if (gIdx >= 0)
                {
                    var g = line.Substring(gIdx + 5).Trim();
                    int comma = g.IndexOf(',');
                    if (comma >= 0) g = g.Substring(0, comma).Trim();
                    int brace = g.IndexOf('}');
                    if (brace >= 0) g = g.Substring(0, brace).Trim();
                    guid = g;
                }
            }
            else if (line.StartsWith("Duration:", StringComparison.Ordinal))
            {
                _ = double.TryParse(line.Substring("Duration:".Length).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            }
            else if (line.StartsWith("Type:", StringComparison.Ordinal))
            {
                _ = int.TryParse(line.Substring("Type:".Length).Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out type);
            }
        }

        if (apiName != null && guid != null)
            yield return new CassieWordEntry(apiName, guid, type, duration);
    }
}

// ---------------------------------------------------------------------------
// Texture / Mesh placeholder catalogs (Phase 1)
// ---------------------------------------------------------------------------

internal sealed record AssetEntry(string Kind, string AssetPath, string OverrideAt, long Size, string Sha256, string? Note);

internal static class PlaceholderCatalog
{
    public static List<AssetEntry> BuildTextures(string exportRoot)
    {
        var entries = new List<AssetEntry>();
        string resourcesDir = Path.Combine(exportRoot, "Assets", "Resources");
        if (!Directory.Exists(resourcesDir))
            return entries;

        foreach (var png in Directory.EnumerateFiles(resourcesDir, "*.png", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(resourcesDir, png).Replace('\\', '/');
            string assetPath = "Texture2D/" + StripExt(rel);
            entries.Add(new AssetEntry(
                Kind: "Texture2D",
                AssetPath: assetPath,
                OverrideAt: "Overrides/Texture2D/" + rel,
                Size: new FileInfo(png).Length,
                Sha256: "",
                Note: "Phase 2 — runtime hook not yet wired"));
        }

        return entries.OrderBy(e => e.AssetPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<AssetEntry> BuildMeshes(string exportRoot)
    {
        var entries = new List<AssetEntry>();
        string meshDir = Path.Combine(exportRoot, "Assets", "Mesh");
        if (!Directory.Exists(meshDir))
            return entries;

        foreach (var f in Directory.EnumerateFiles(meshDir, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext != ".obj" && ext != ".fbx" && ext != ".asset")
                continue;
            string rel = Path.GetRelativePath(meshDir, f).Replace('\\', '/');
            string assetPath = "Mesh/" + StripExt(rel);
            entries.Add(new AssetEntry(
                Kind: "Mesh",
                AssetPath: assetPath,
                OverrideAt: "Overrides/Mesh/" + rel,
                Size: new FileInfo(f).Length,
                Sha256: "",
                Note: "Mesh overrides not yet supported — use AssetBundles via the spawn pipeline"));
        }

        return entries.OrderBy(e => e.AssetPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string RenderTexturesMdx(List<AssetEntry> entries, string exportRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: Asset Catalog — Textures");
        sb.AppendLine("description: Engine-loadable textures and sprites in SCP:SL by replacement path. Path-keyed texture overrides land in Phase 2.");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"{{/* @generated by tools/AssetCatalog from {Path.GetFileName(exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))} on {DateTime.UtcNow:yyyy-MM-dd}. Do not edit manually. */}}");
        sb.AppendLine();
        sb.AppendLine("import { Aside } from '@astrojs/starlight/components';");
        sb.AppendLine();
        sb.AppendLine("Engine-loadable textures (those reachable via `Resources.Load<Texture2D>`) that will be overridable once the path-keyed texture hook lands in Phase 2.");
        sb.AppendLine();
        sb.AppendLine("<Aside type=\"caution\" title=\"Phase 2\">");
        sb.AppendLine("Path-keyed texture overrides are not yet wired at runtime. The existing per-NetId texture pipeline (player skins, specific door textures) continues to work via `AssetOverrideTextureMessage`. This catalog enumerates the assets that *will* be overridable via the unified `Overrides/Texture2D/` directory once Phase 2 ships.");
        sb.AppendLine("</Aside>");
        sb.AppendLine();
        sb.AppendLine($"**Total entries:** {entries.Count}.");
        sb.AppendLine();
        sb.AppendLine("| Asset | Override Path | Size |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var e in entries)
        {
            sb.AppendLine($"| `{Escape(e.AssetPath)}` | `{Escape(e.OverrideAt)}` | {FormatSize(e.Size)} |");
        }
        return sb.ToString();
    }

    public static string RenderMeshesMdx(List<AssetEntry> entries, string exportRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: Asset Catalog — Meshes");
        sb.AppendLine("description: Mesh assets in SCP:SL. Mesh overrides are not yet supported — use AssetBundles via the spawn pipeline.");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"{{/* @generated by tools/AssetCatalog from {Path.GetFileName(exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))} on {DateTime.UtcNow:yyyy-MM-dd}. Do not edit manually. */}}");
        sb.AppendLine();
        sb.AppendLine("import { Aside } from '@astrojs/starlight/components';");
        sb.AppendLine();
        sb.AppendLine("<Aside type=\"caution\" title=\"Not yet supported\">");
        sb.AppendLine("Mesh overrides via the `Overrides/Mesh/` directory are not implemented. Pure-managed `.obj`/`.fbx` parsing in Il2CppInterop is non-trivial; for now, ship custom geometry via the existing AssetBundle spawn pipeline.");
        sb.AppendLine();
        sb.AppendLine("This page lists known mesh assets so the directory layout is documented for the future.");
        sb.AppendLine("</Aside>");
        sb.AppendLine();
        sb.AppendLine($"**Total entries:** {entries.Count}.");
        sb.AppendLine();
        sb.AppendLine("| Asset | Reserved Override Path | Size |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var e in entries.Take(500))
        {
            sb.AppendLine($"| `{Escape(e.AssetPath)}` | `{Escape(e.OverrideAt)}` | {FormatSize(e.Size)} |");
        }
        if (entries.Count > 500)
            sb.AppendLine($"\n_(showing first 500 of {entries.Count}.)_");
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    private static string StripExt(string p)
    {
        int dot = p.LastIndexOf('.');
        int slash = p.LastIndexOf('/');
        return dot > slash && dot >= 0 ? p.Substring(0, dot) : p;
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("`", "\\`");
}
