using System.Collections.Generic;

namespace Anomaly.Shared.Assets;

/// <summary>Offline-produced metadata. Clients independently inspect every bundle object.</summary>
public sealed class AssetPackIndex
{
    public const int CurrentSchemaVersion = 1;
    public const string SupportedUnityVersion = "6000.0.43f1";
    public const string SupportedPlatform = "windows-x64";
    public const string SupportedGameProfile = "scpsl-14.2.7-unity-6000.0.43f1-windows-x64-v1";
    public int SchemaVersion = CurrentSchemaVersion;
    public string PackId = string.Empty;
    public string Namespace = string.Empty;
    public string UnityVersion = SupportedUnityVersion;
    public string Platform = SupportedPlatform;
    public string GameProfile = SupportedGameProfile;
    public string BundleFile = string.Empty;
    public string BundleSha256 = string.Empty;
    public long BundleBytes;
    public List<PackAssetEntry> Assets = new List<PackAssetEntry>();
    public List<DecorativeVisualDefinition> Visuals = new List<DecorativeVisualDefinition>();
}

public sealed class PackAssetEntry
{
    public string Address = string.Empty;
    public string Kind = string.Empty;
    public string EntryName = string.Empty;
    public int Width;
    public int Height;
    public int SampleFrames;
    public int SampleRate;
    public int Channels;
    public int VertexCount;
    public int TriangleCount;

    public AssetKind AssetKind => Kind switch
    {
        "Texture2D" => AssetKind.Texture2D,
        "Sprite" => AssetKind.Sprite,
        "AudioClip" => AssetKind.AudioClip,
        "Mesh" => AssetKind.Mesh,
        _ => AssetKind.Unknown,
    };
}

/// <summary>Contains no Unity prefab, component names, or arbitrary properties.</summary>
public sealed class DecorativeVisualDefinition
{
    public string Address = string.Empty;
    public List<DecorativeVisualNode> Nodes = new List<DecorativeVisualNode>();
}

public sealed class DecorativeVisualNode
{
    public string Name = string.Empty;
    public int Parent = -1;
    public float[] Position = { 0, 0, 0 };
    public float[] Rotation = { 0, 0, 0, 1 };
    public float[] Scale = { 1, 1, 1 };
    public string MeshAddress;
    public string TextureAddress;
    public string MaterialPreset;
    public float[] Color = { 1, 1, 1, 1 };
}
