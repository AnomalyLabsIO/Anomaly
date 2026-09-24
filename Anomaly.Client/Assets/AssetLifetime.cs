using UnityEngine;

namespace Anomaly.Client.Assets;

/// <summary>Retains stock objects while a replacement temporarily removes their engine reference.</summary>
internal static class AssetLifetime
{
    private sealed class Lease
    {
        internal UnityEngine.Object Asset;
        internal HideFlags OriginalFlags;
        internal int Owners;
    }
    private static readonly Dictionary<int, Lease> Leases = new();

    internal static void Retain(UnityEngine.Object asset)
    {
        if (asset == null) return;
        int id = asset.GetInstanceID();
        if (Leases.TryGetValue(id, out var lease)) { lease.Owners++; return; }
        lease = new Lease { Asset = asset, OriginalFlags = asset.hideFlags, Owners = 1 };
        asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Leases.Add(id, lease);
    }

    internal static void Release(UnityEngine.Object asset)
    {
        if (ReferenceEquals(asset, null) || !Leases.TryGetValue(asset.GetInstanceID(), out var lease)) return;
        if (--lease.Owners > 0) return;
        if (lease.Asset != null) lease.Asset.hideFlags = lease.OriginalFlags;
        Leases.Remove(asset.GetInstanceID());
    }
}
