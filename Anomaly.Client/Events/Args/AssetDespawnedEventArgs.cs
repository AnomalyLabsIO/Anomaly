namespace Anomaly.Client.Events.Args;

public class AssetDespawnedEventArgs : EventArgs
{
    public AssetDespawnedEventArgs(uint objectId)
    {
        ObjectId = objectId;
    }

    public uint ObjectId { get; }
}
