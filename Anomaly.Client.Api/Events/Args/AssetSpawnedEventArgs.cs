using UnityEngine;

namespace Anomaly.Client.Api.Events.Args;

public class AssetSpawnedEventArgs : EventArgs
{
    public AssetSpawnedEventArgs(uint objectId, byte[] assetHash, GameObject gameObject)
    {
        ObjectId = objectId;
        AssetHash = assetHash;
        GameObject = gameObject;
    }

    /// <summary>Server-assigned spawn id.</summary>
    public uint ObjectId { get; }

    /// <summary>32-byte SHA-256 hash of the source asset.</summary>
    public byte[] AssetHash { get; }

    /// <summary>Instantiated GameObject on the client.</summary>
    public GameObject GameObject { get; }
}
