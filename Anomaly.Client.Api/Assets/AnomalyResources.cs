using System;
using Anomaly.Shared.Assets;
using UnityEngine;

namespace Anomaly.Client.Api.Assets;

/// <summary>
/// Resolves typed catalog addresses through the active server and local packs.
/// Returned objects are borrowed: mods must not destroy them or retain remote
/// objects after disconnect. Call on the Unity thread. Stock fallback remains
/// owned by each supported replacement hook.
/// </summary>
public static class AnomalyResources
{
    private static Func<AssetAddress, AssetKind, UnityEngine.Object> _resolver;
    internal static void SetResolver(Func<AssetAddress, AssetKind, UnityEngine.Object> resolver) => _resolver = resolver;

    public static bool TryGetTexture(AssetAddress address, out Texture2D texture) => TryGet(address, AssetKind.Texture2D, out texture);
    public static bool TryGetSprite(AssetAddress address, out Sprite sprite) => TryGet(address, AssetKind.Sprite, out sprite);
    public static bool TryGetAudioClip(AssetAddress address, out AudioClip clip) => TryGet(address, AssetKind.AudioClip, out clip);
    public static bool TryGetMesh(AssetAddress address, out Mesh mesh) => TryGet(address, AssetKind.Mesh, out mesh);

    private static bool TryGet<T>(AssetAddress address, AssetKind kind, out T result) where T : UnityEngine.Object
    {
        result = null;
        if (!address.IsValid || _resolver == null) return false;
        UnityEngine.Object value = _resolver(address, kind);
        if (ReferenceEquals(value, null) || value == null) return false;
        result = value.TryCast<T>();
        return result != null;
    }
}
