using System;
using System.Collections.Generic;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;

namespace Anomaly.Server.Api.Assets;

/// <summary>Metadata lookup for validated offline-produced packs. No server asset importing occurs.</summary>
public static class AssetRegistry
{
    public static bool IsReady => ServerAssetManager.IsReady;
    public static bool TryGetKind(AssetAddress address, out AssetKind kind)
    {
        if (ServerAssetManager.TryGetAsset(address, out var asset)) { kind = asset.Kind; return true; }
        kind = AssetKind.Unknown; return false;
    }
    public static bool Contains(AssetAddress address) => TryGetKind(address, out _);
    public static bool TryGetAudioDuration(AssetAddress address, out float durationSeconds)
    {
        durationSeconds = 0;
        if (!ServerAssetManager.TryGetAsset(address, out var asset) || asset.Kind != AssetKind.AudioClip) return false;
        durationSeconds = (float)((double)asset.Entry.SampleFrames / asset.Entry.SampleRate);
        return true;
    }
    public static IReadOnlyList<AssetAddress> All()
    {
        using (var snapshot = ServerAssetManager.AcquireSnapshot())
        {
            var addresses = new List<AssetAddress>();
            foreach (var asset in snapshot.Assets) addresses.Add(asset.Address);
            return addresses.AsReadOnly();
        }
    }
}
