using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.AssetValidation;
using Anomaly.Shared.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Editor-only tooling. None of these classes or source hierarchies are bundle roots.
public static class AssetPackExporter
{
    private const string GameProfile = "scpsl-14.2.7-unity-6000.0.43f1-windows-x64-v1";
    private static readonly Regex NamespacePattern = new Regex(@"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$");

    public static void Build()
    {
        if (Application.unityVersion != BundleValidator.UnityVersion)
            throw new InvalidOperationException("Use Unity " + BundleValidator.UnityVersion + ".");
        var configPath = Path.GetFullPath(Argument("-anomalyPack"));
        var output = Path.GetFullPath(Argument("-anomalyOutput"));
        if (new FileInfo(configPath).Length > 256 * 1024) throw new InvalidDataException("Pack config exceeds 256 KiB.");
        var config = JsonUtility.FromJson<PackConfig>(File.ReadAllText(configPath));
        CheckConfig(config);
        Directory.CreateDirectory(output);
        var generated = "Assets/AnomalyGenerated/" + Hash(Encoding.UTF8.GetBytes(config.PackId)).Substring(0, 20);
        Directory.CreateDirectory(generated);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        var paths = new List<string>();
        var rootObjects = new List<Object>();
        for (var i = 0; i < config.Assets.Length; i++)
        {
            var source = config.Assets[i];
            var original = SourcePath(source.SourcePath);
            Object asset;
            switch (source.Kind)
            {
                case "Texture2D": asset = Texture(original, generated, textures); break;
                case "Sprite":
                    var texture = Texture(original, generated, textures);
                    if (!Finite(source.PixelsPerUnit) || source.PixelsPerUnit < 0.01f || source.PixelsPerUnit > 100000)
                        throw new InvalidDataException("Invalid sprite pixels per unit.");
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), source.PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = Leaf(source.Address);
                    asset = Save(sprite, generated + "/sprite-" + i + ".asset");
                    break;
                case "AudioClip": asset = Audio(original, source.SampleRate, generated); break;
                case "Mesh": asset = MeshAsset(original, source.SubAssetName, generated + "/mesh-" + i + ".asset", Leaf(source.Address)); break;
                default: throw new InvalidDataException("Unsupported asset kind.");
            }
            paths.Add(AssetDatabase.GetAssetPath(asset)); rootObjects.Add(asset);
        }
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
            throw new InvalidDataException("Each native root must have one public address. Share a texture through a sprite dependency, not duplicate texture aliases.");
        AssetDatabase.SaveAssets();
        var allowedPaths = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (var texture in textures.Values) allowedPaths.Add(AssetDatabase.GetAssetPath(texture));
        foreach (var dependency in AssetDatabase.GetDependencies(paths.ToArray(), true))
            if (!allowedPaths.Contains(dependency)) throw new InvalidDataException("Unexpected dependency: " + dependency);

        var index = new PackIndex
        {
            PackId = config.PackId, Namespace = config.Namespace, BundleFile = config.BundleFile,
            Assets = config.Assets.Select((source, i) => Measure(source, rootObjects[i])).ToArray(),
            Visuals = config.Visuals ?? new Visual[0]
        };
        index.BundleBytes = 1; index.DecodedBytes = 1; index.BundleSha256 = new string('0', 64);
        AssetPackIndexCodec.Parse(Encoding.UTF8.GetBytes(JsonUtility.ToJson(index, false)), !config.LocalOnly);
        var definition = new AssetBundleBuild
        {
            assetBundleName = config.BundleFile,
            assetNames = paths.ToArray(),
            addressableNames = config.Assets.Select(source => source.Address).ToArray()
        };
        var options = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode
            | BuildAssetBundleOptions.DisableLoadAssetByFileName | BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { definition }, options, BuildTarget.StandaloneWindows64);
        if (manifest == null || manifest.GetAllDependencies(config.BundleFile).Length != 0)
            throw new InvalidDataException("Failed to build a self-contained bundle.");
        var bundlePath = Path.Combine(output, config.BundleFile);
        index.BundleBytes = new FileInfo(bundlePath).Length;
        using (var input = File.OpenRead(bundlePath))
        using (var sha = SHA256.Create()) index.BundleSha256 = Hex(sha.ComputeHash(input));
        var expectations = index.Assets.Select(asset => new BundleAssetExpectation(asset.Address, asset.EntryName, ClassId(asset.Kind), asset.Width, asset.Height,
            asset.SampleFrames, asset.SampleRate, asset.Channels, asset.VertexCount, asset.TriangleCount));
        using (var input = File.OpenRead(bundlePath))
        using (var validated = BundleValidator.Validate(input, new BundleValidationRequest(index.BundleSha256, expectations)))
        {
            index.DecodedBytes = validated.TotalDecodedBytes;
            Debug.Log("ANOMALY_PACK_VALIDATED profile=" + BundleValidator.ProfileId + " objects=" + validated.Assets.Count + " expanded=" + validated.ExpandedBytes + " decoded=" + validated.TotalDecodedBytes);
        }

        var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(index, false));
        AssetPackIndexCodec.Parse(json, !config.LocalOnly);
        // Publishing the index last prevents a failed validation from advertising a new pack.
        var indexPath = Path.Combine(output, Path.GetFileNameWithoutExtension(config.BundleFile) + ".index.json");
        var temporary = indexPath + ".tmp";
        File.WriteAllBytes(temporary, json);
        if (File.Exists(indexPath)) File.Replace(temporary, indexPath, null); else File.Move(temporary, indexPath);
        Debug.Log("ANOMALY_PACK_EXPORT PASS bundle=" + bundlePath + " index=" + indexPath + " sha256=" + index.BundleSha256);
    }

    private static Texture2D Texture(string source, string generated, Dictionary<string, Texture2D> textures)
    {
        if (textures.TryGetValue(source, out var cached)) return cached;
        var target = CopySource(source, generated, "texture");
        var importer = AssetImporter.GetAtPath(target) as TextureImporter;
        if (importer == null) throw new InvalidDataException("Texture/Sprite SourcePath must identify an image imported by Unity: " + source);
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.streamingMipmaps = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.isReadable = false;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.RGBA32,
            textureCompression = TextureImporterCompression.Uncompressed, crunchedCompression = false, allowsAlphaSplitting = false
        });
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
        if (texture == null || texture.width < 1 || texture.height < 1 || texture.width > 4096 || texture.height > 4096)
            throw new InvalidDataException("Texture dimensions outside supported bounds.");
        textures.Add(source, texture); return texture;
    }

    private static AudioClip Audio(string source, int sampleRate, string generated)
    {
        if (!new[] { 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 }.Contains(sampleRate))
            throw new InvalidDataException("Choose a supported common audio rate: 8000,11000,11025,16000,22050,24000,32000,44100,48000,96000.");
        var target = CopySource(source, generated, "audio");
        var importer = AssetImporter.GetAtPath(target) as AudioImporter;
        if (importer == null) throw new InvalidDataException("Audio SourcePath must identify a Unity-supported audio file.");
        importer.forceToMono = false; importer.ambisonic = false; importer.loadInBackground = false;
        var settings = new AudioImporterSampleSettings
        {
            loadType = AudioClipLoadType.CompressedInMemory, compressionFormat = AudioCompressionFormat.Vorbis,
            quality = 0.5f, sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate, sampleRateOverride = (uint)sampleRate,
            preloadAudioData = false
        };
        importer.defaultSampleSettings = settings; importer.SetOverrideSampleSettings("Standalone", settings); importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(target);
        if (clip == null || clip.channels < 1 || clip.channels > 2 || clip.samples <= 0 || clip.length > 30)
            throw new InvalidDataException("Audio must be mono/stereo and at most 30 seconds.");
        return clip;
    }

    private static Mesh MeshAsset(string source, string subAssetName, string destination, string name)
    {
        AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
        var meshes = AssetDatabase.LoadAllAssetsAtPath(source).OfType<Mesh>().Where(mesh => string.IsNullOrEmpty(subAssetName) || mesh.name == subAssetName).ToArray();
        if (meshes.Length != 1) throw new InvalidDataException("Mesh SourcePath must select exactly one readable Mesh; set SubAssetName for a model with multiple meshes.");
        var original = meshes[0];
        if (!original.isReadable) throw new InvalidDataException("Enable Read/Write on the source mesh importer before exporting.");
        if (original.vertexCount < 3 || original.vertexCount > 100000 || original.subMeshCount < 1 || original.subMeshCount > 32 || original.blendShapeCount != 0 || original.bindposes.Length != 0 || original.boneWeights.Length != 0)
            throw new InvalidDataException("Only bounded static unskinned meshes are supported.");
        var mesh = new Mesh { name = name, indexFormat = original.vertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
        mesh.vertices = original.vertices;
        if (original.normals.Length == original.vertexCount) mesh.normals = original.normals;
        if (original.tangents.Length == original.vertexCount) mesh.tangents = original.tangents;
        if (original.colors.Length == original.vertexCount) mesh.colors = original.colors;
        for (var channel = 0; channel < 8; channel++)
        {
            var uvs = new List<Vector2>(); original.GetUVs(channel, uvs);
            if (uvs.Count != 0) mesh.SetUVs(channel, uvs);
        }
        mesh.subMeshCount = original.subMeshCount;
        long triangles = 0;
        for (var submesh = 0; submesh < original.subMeshCount; submesh++)
        {
            if (original.GetTopology(submesh) != MeshTopology.Triangles) throw new InvalidDataException("Only triangle topology is supported.");
            var indices = original.GetIndices(submesh, true); triangles += indices.Length / 3;
            if (triangles > 200000) throw new InvalidDataException("Mesh triangle limit exceeded.");
            mesh.SetIndices(indices, MeshTopology.Triangles, submesh, true, 0);
        }
        if (mesh.normals.Length == 0) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return (Mesh)Save(mesh, destination);
    }

    private static AssetEntry Measure(SourceAsset source, Object asset)
    {
        var entry = new AssetEntry { Address = source.Address, EntryName = source.Address, Kind = source.Kind };
        if (asset is Texture2D texture) { entry.Width = texture.width; entry.Height = texture.height; }
        else if (asset is Sprite sprite) { entry.Width = (int)sprite.rect.width; entry.Height = (int)sprite.rect.height; }
        else if (asset is AudioClip clip) { entry.SampleFrames = clip.samples; entry.SampleRate = clip.frequency; entry.Channels = clip.channels; }
        else if (asset is Mesh mesh) { entry.VertexCount = mesh.vertexCount; entry.TriangleCount = mesh.triangles.Length / 3; }
        else throw new InvalidDataException("Unexpected root type.");
        return entry;
    }

    private static void CheckConfig(PackConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.Namespace) || config.Namespace.Length > 64 || !NamespacePattern.IsMatch(config.Namespace)
            || (!config.LocalOnly && (config.Namespace == "anomaly" || config.Namespace.StartsWith("anomaly.", StringComparison.Ordinal))))
            throw new InvalidDataException("Invalid pack namespace.");
        if (config.PackId == null || config.PackId.Length > 128 || !config.PackId.StartsWith(config.Namespace + ":", StringComparison.Ordinal) || !Segment(config.PackId.Substring(config.Namespace.Length + 1)))
            throw new InvalidDataException("PackId must be namespace:name.");
        if (!Segment(config.BundleFile) || config.BundleFile.Length > 128 || !config.BundleFile.EndsWith(".bundle", StringComparison.Ordinal))
            throw new InvalidDataException("BundleFile must be one lowercase .bundle filename.");
        if (config.Assets == null || config.Assets.Length < 1 || config.Assets.Length > 4095) throw new InvalidDataException("Invalid root count.");
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in config.Assets)
        {
            if (source == null || !Address(source.Address, config.Namespace, config.LocalOnly) || !addresses.Add(source.Address))
                throw new InvalidDataException("Invalid or duplicate source address.");
            ClassId(source.Kind);
            if (source.Kind == "AudioClip" && source.SampleRate == 0) source.SampleRate = 48000;
            if (source.Kind == "Sprite" && source.PixelsPerUnit == 0) source.PixelsPerUnit = 100;
            if (source.Address.StartsWith("game/skin/", StringComparison.Ordinal) && source.Kind != "Texture2D") throw new InvalidDataException("Skin roots must be Texture2D.");
            if (source.Address.StartsWith("game/cassie/", StringComparison.Ordinal) && source.Kind != "AudioClip") throw new InvalidDataException("CASSIE roots must be AudioClip.");
        }
    }

    private static bool Address(string value, string packNamespace, bool local)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 255 || value.Split('/').Any(part => !Segment(part))) return false;
        if (value.StartsWith("mods/" + packNamespace + "/", StringComparison.Ordinal)) return true;
        if (local && (value == "game/skin/logo" || value == "game/skin/background")) return true;
        return value.StartsWith("game/cassie/", StringComparison.Ordinal) && value.Split('/').Length == 3;
    }
    private static bool Segment(string value) => !string.IsNullOrEmpty(value) && value != "." && value != ".." && value.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.');
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static int ClassId(string kind)
    {
        switch (kind) { case "Texture2D": return 28; case "Sprite": return 213; case "AudioClip": return 83; case "Mesh": return 43; default: throw new InvalidDataException("Unsupported asset kind: " + kind); }
    }
    private static string SourcePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("\\") || path.Split('/').Any(part => part == "." || part == "..") || !File.Exists(path))
            throw new InvalidDataException("SourcePath must be an existing project Assets/ path.");
        return path;
    }
    private static string CopySource(string source, string generated, string prefix)
    {
        var target = generated + "/" + prefix + "-" + Hash(Encoding.UTF8.GetBytes(source)).Substring(0, 20) + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, target, true); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport); return target;
    }
    private static Object Save(Object value, string path)
    {
        var current = AssetDatabase.LoadMainAssetAtPath(path);
        if (current == null) { AssetDatabase.CreateAsset(value, path); return value; }
        if (current.GetType() != value.GetType()) throw new InvalidDataException("Generated asset type changed.");
        EditorUtility.CopySerialized(value, current); EditorUtility.SetDirty(current); Object.DestroyImmediate(value); return current;
    }
    private static string Leaf(string path) => path.Substring(path.LastIndexOf('/') + 1);
    private static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        throw new ArgumentException("Missing " + name + ".");
    }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

    [Serializable] private sealed class PackConfig
    {
        public string PackId, Namespace, BundleFile;
        public bool LocalOnly;
        public SourceAsset[] Assets;
        public Visual[] Visuals;
    }
    [Serializable] private sealed class SourceAsset
    {
        public string Address, Kind, SourcePath, SubAssetName;
        public int SampleRate = 48000;
        public float PixelsPerUnit = 100;
    }
    [Serializable] private sealed class PackIndex
    {
        public int SchemaVersion = 2;
        public string PackId, Namespace;
        public string UnityVersion = BundleValidator.UnityVersion;
        public string Platform = "windows-x64";
        public string GameProfile = AssetPackExporter.GameProfile;
        public string BundleFile, BundleSha256;
        public long BundleBytes, DecodedBytes;
        public AssetEntry[] Assets;
        public Visual[] Visuals;
    }
    [Serializable] private sealed class AssetEntry
    {
        public string Address, Kind, EntryName;
        public int Width, Height, SampleFrames, SampleRate, Channels, VertexCount, TriangleCount;
    }
    [Serializable] private sealed class Visual { public string Address; public VisualNode[] Nodes; }
    [Serializable] private sealed class VisualNode
    {
        public string Name;
        public int Parent = -1;
        public float[] Position = { 0, 0, 0 };
        public float[] Rotation = { 0, 0, 0, 1 };
        public float[] Scale = { 1, 1, 1 };
        public string MeshAddress, TextureAddress, MaterialPreset;
        public float[] Color = { 1, 1, 1, 1 };
    }
}
