using Anomaly.Shared.Assets;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class AssetOverrideAppliedEventArgs : EventArgs
{
    public AssetOverrideAppliedEventArgs(uint netId, string componentPath, int materialIndex, AssetAddress textureAddress)
    {
        NetId = netId;
        ComponentPath = componentPath;
        MaterialIndex = materialIndex;
        TextureAddress = textureAddress;
    }

    [ScriptVisible]
    public uint NetId { get; }
    [ScriptVisible]
    public string ComponentPath { get; }
    [ScriptVisible]
    public int MaterialIndex { get; }
    public AssetAddress TextureAddress { get; }
}
