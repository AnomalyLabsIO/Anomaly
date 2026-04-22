namespace Anomaly.Client.Api.Events.Args;

public class AssetReloadedEventArgs : EventArgs
{
    public AssetReloadedEventArgs(byte[] oldHash, byte[] newHash, int affectedSpawns, int affectedOverrides)
    {
        OldHash = oldHash;
        NewHash = newHash;
        AffectedSpawns = affectedSpawns;
        AffectedOverrides = affectedOverrides;
    }

    public byte[] OldHash { get; }
    public byte[] NewHash { get; }

    /// <summary>Number of spawned objects whose source asset pointed at the old hash.</summary>
    public int AffectedSpawns { get; }

    /// <summary>Number of active overrides whose texture hash pointed at the old hash.</summary>
    public int AffectedOverrides { get; }
}
