using Anomaly.Shared.Assets;
using UnityEngine;

namespace Anomaly.Client.Assets;

/// <summary>Maps supported stock CASSIE names to typed pack replacements.</summary>
internal static class PathOverrideRegistry
{
    internal static void Init() { }
    internal static void Shutdown() { }

    internal static bool TryGetAudioClip(string apiName, out AudioClip clip)
    {
        clip = null;
        if (string.IsNullOrEmpty(apiName) ||
            !AssetAddress.TryParse("game/cassie/" + apiName.ToLowerInvariant(), out var address) ||
            !StockAssetProfile.TryGetCassie(address, out var timing)) return false;
        clip = AssetBundleManager.Resolve(address, AssetKind.AudioClip)?.TryCast<AudioClip>();
        if (clip == null) return false;
        // Scheduling metadata remains stock-authoritative. Match the supported
        // physical clip interval; do not mutate CassieLine.Duration or ApiName.
        if (!timing.MatchesClip(clip.samples, clip.frequency))
        {
            clip = null;
            return false;
        }
        return true;
    }
}
