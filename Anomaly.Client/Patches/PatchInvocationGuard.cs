using System.Collections.Generic;
using UnityEngine;

namespace Anomaly.Client.Patches;

/// <summary>
/// Deduplicates Harmony-patch callbacks that fire more than once per frame for the
/// same instance. In IL2CPP, patching a virtual method (Mirror's <c>OnStartClient</c>,
/// Unity's <c>OnDestroy</c> when a derived class overrides it, etc.) can route a single
/// logical call through multiple vtable slots, producing duplicate invocations.
///
/// Each call pair (instance, kind) fires at most once per Unity frame. Legitimate
/// cross-frame re-invocations are unaffected.
/// </summary>
internal static class PatchInvocationGuard
{
    private static readonly HashSet<long> _fired = new();
    private static int _currentFrame = -1;

    // Kind codes — small ints so they pack into the upper byte of the dedup key.
    public const int PlayerAdded = 1;
    public const int PlayerRemoved = 2;
    public const int PickupAdded = 3;
    public const int PickupRemoved = 4;
    public const int AdminToyAdded = 5;
    public const int AdminToyRemoved = 6;
    public const int InventoryStart = 7;
    public const int RoleInitialized = 8;

    public static bool ShouldFire(UnityEngine.Object instance, int kind)
    {
        if (instance == null)
            return false;

        int frame = Time.frameCount;
        if (frame != _currentFrame)
        {
            _fired.Clear();
            _currentFrame = frame;
        }

        long key = ((long)instance.GetInstanceID() << 8) | (uint)(kind & 0xFF);
        return _fired.Add(key);
    }
}
