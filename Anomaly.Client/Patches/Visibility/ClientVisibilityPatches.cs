using Anomaly.Client.Api.Features;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMapGeneration;
using Il2CppProgressiveCulling;
using UnityEngine;

namespace Anomaly.Client.Patches.Visibility;

/// <summary>
/// Unions leased auxiliary views into the stock culling decisions. The stock
/// observer remains authoritative for its own frustum; these postfixes can only
/// turn a stock "hidden" result into "visible".
/// </summary>
internal static class ClientVisibilityPatches
{
    [HarmonyPatch(typeof(CullingCamera), "UpdateCulling")]
    private static class CullingCyclePatch
    {
        private static void Prefix()
        {
            // Sample every provider before the stock root/room/area walk. The
            // per-frame cache then makes all postfix decisions observe the same
            // portal-camera state without invoking mod code under a registry lock.
            ClientVisibility.RefreshForCullingCycle();
        }
    }

    [HarmonyPatch(typeof(CullingCamera), nameof(CullingCamera.CheckBoundsVisibility), new[] { typeof(Bounds) })]
    private static class DefaultBoundsPatch
    {
        private static void Postfix(Bounds bounds, ref bool __result)
        {
            if (!__result)
                __result = ClientVisibility.IsVisible(bounds);
        }
    }

    [HarmonyPatch(
        typeof(CullingCamera),
        nameof(CullingCamera.CheckBoundsVisibility),
        new[] { typeof(Il2CppStructArray<Plane>), typeof(float), typeof(Bounds) })]
    private static class ExplicitBoundsPatch
    {
        private static void Postfix(Bounds bounds, ref bool __result)
        {
            if (!__result)
                __result = ClientVisibility.IsVisible(bounds);
        }
    }

    [HarmonyPatch(typeof(RoomPreCuller), nameof(RoomPreCuller.ValidateCoords))]
    private static class RoomCoordinatePatch
    {
        private static void Postfix(Vector3Int coords, ref bool __result)
        {
            if (__result || RoomIdentifier.RoomsByCoords == null)
                return;

            try
            {
                if (RoomIdentifier.RoomsByCoords.TryGetValue(coords, out RoomIdentifier room) &&
                    room != null)
                {
                    __result = ClientVisibility.IsVisible(room.WorldspaceBounds);
                }
            }
            catch
            {
                // Culling must remain fail-soft while map generation replaces
                // the coordinate table.
            }
        }
    }

    [HarmonyPatch(typeof(CullableRoom), nameof(CullableRoom.ShouldBeVisible), MethodType.Getter)]
    private static class CullableRoomPatch
    {
        private static void Postfix(CullableRoom __instance, ref bool __result)
        {
            if (!__result && __instance != null)
                __result = ClientVisibility.IsVisible(__instance.WorldspaceBounds);
        }
    }

    [HarmonyPatch(typeof(CullableArea), nameof(CullableArea.ShouldBeVisible), MethodType.Getter)]
    private static class CullableAreaPatch
    {
        private static void Postfix(CullableArea __instance, ref bool __result)
        {
            if (!__result && __instance != null)
                __result = ClientVisibility.IsVisible(__instance.WorldspaceBounds);
        }
    }

    [HarmonyPatch(
        typeof(RoomCullingConnection),
        nameof(RoomCullingConnection.ShouldBeVisible),
        MethodType.Getter)]
    private static class RoomConnectionPatch
    {
        private static void Postfix(RoomCullingConnection __instance, ref bool __result)
        {
            if (!__result && __instance != null)
                __result = ClientVisibility.IsVisible(__instance.WorldspaceBounds);
        }
    }
}
