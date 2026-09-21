using Anomaly.Client.Api.Internal;
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
    private static readonly FrameInvocationGate GeneralGate = new();
    private static readonly FrameInvocationGate AuthenticationGate = new();

    // Kind codes — small ints so they pack into the upper byte of the dedup key.
    public const int PlayerAdded = 1;
    public const int PlayerRemoved = 2;
    public const int PickupAdded = 3;
    public const int PickupRemoved = 4;
    public const int AdminToyAdded = 5;
    public const int AdminToyRemoved = 6;
    public const int InventoryStart = 7;
    public const int RoleInitialized = 8;
    public const int ConnectionRefused = 9;
    public const int ClientDisconnect = 10;

    public static bool ShouldFire(UnityEngine.Object instance, int kind)
    {
        if (instance == null)
            return false;

        return GeneralGate.ShouldFire(instance.GetInstanceID(), kind, Time.frameCount);
    }

    public static bool ShouldFireAuthentication(UnityEngine.Object instance, int kind)
    {
        if (instance == null)
            return false;

        return AuthenticationGate.ShouldFire(instance.GetInstanceID(), kind, Time.frameCount);
    }

    public static void BeginAuthenticationScope()
    {
        AuthenticationGate.Reset();
    }
}
