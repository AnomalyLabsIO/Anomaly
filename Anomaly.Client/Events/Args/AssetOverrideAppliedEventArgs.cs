namespace Anomaly.Client.Events.Args;

public class AssetOverrideAppliedEventArgs : EventArgs
{
    public AssetOverrideAppliedEventArgs(uint netId, string componentPath, int materialIndex, byte[] textureHash)
    {
        NetId = netId;
        ComponentPath = componentPath;
        MaterialIndex = materialIndex;
        TextureHash = textureHash;
    }

    public uint NetId { get; }
    public string ComponentPath { get; }
    public int MaterialIndex { get; }
    public byte[] TextureHash { get; }
}
