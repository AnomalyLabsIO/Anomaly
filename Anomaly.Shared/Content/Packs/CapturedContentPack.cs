using System;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;

namespace Anomaly.Shared.Content.Packs;

/// <summary>Validated metadata and exact source bytes; the bundle remains a separately verified blob.</summary>
public sealed class CapturedContentPack
{
    public ContentPackDescriptor Descriptor;
    public ScriptPackIndex ScriptIndex;
    public byte[] ScriptIndexBytes;
    public byte[] SourceBytes;
    public string SourceText;
    public AssetPackIndex AssetIndex;
    public byte[] AssetIndexBytes;
    public string BundlePath;
    public PackItemDefinition[] Items = Array.Empty<PackItemDefinition>();
}
