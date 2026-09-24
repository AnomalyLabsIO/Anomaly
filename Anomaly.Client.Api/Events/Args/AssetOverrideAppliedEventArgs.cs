using Anomaly.Shared.Assets;

namespace Anomaly.Client.Api.Events.Args;

public class AssetOverrideAppliedEventArgs : EventArgs
{
    public AssetOverrideAppliedEventArgs(uint netId, string componentPath, int materialIndex, AssetAddress textureAddress)
    {
        NetId = netId;
        ComponentPath = componentPath;
        MaterialIndex = materialIndex;
        TextureAddress = textureAddress;
    }

    public uint NetId { get; }
    public string ComponentPath { get; }
    public int MaterialIndex { get; }
    public AssetAddress TextureAddress { get; }
}
