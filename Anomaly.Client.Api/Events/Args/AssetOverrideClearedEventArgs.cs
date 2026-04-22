namespace Anomaly.Client.Api.Events.Args;

public class AssetOverrideClearedEventArgs : EventArgs
{
    public AssetOverrideClearedEventArgs(uint netId, string componentPath, int materialIndex)
    {
        NetId = netId;
        ComponentPath = componentPath;
        MaterialIndex = materialIndex;
    }

    public uint NetId { get; }
    public string ComponentPath { get; }
    public int MaterialIndex { get; }
}
