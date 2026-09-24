using Anomaly.Client.Auth;
using Anomaly.Client.Internal.Map;
using HarmonyLib;
using Il2CppMapGeneration.Distributors;
using Il2CppMirror;
using UnityEngine;

namespace Anomaly.Client.Patches.Runtime;

[HarmonyPatch(typeof(StructurePositionSync), nameof(StructurePositionSync.Start))]
internal static class FacilityStructureRotationPatch
{
    private static void Prefix(StructurePositionSync __instance, out Quaternion? __state)
    {
        __state = null;
        if (AuthHandler.UsingAnomalyAuth && !NetworkServer.active && FacilityStructureRotation.Take(__instance))
            __state = __instance.transform.rotation;
    }

    private static void Postfix(StructurePositionSync __instance, Quaternion? __state)
    {
        // Mirror's initial spawn carries the full server quaternion. The native
        // Start overwrites it with a 5.625-degree SyncVar yaw, which loses arbitrary
        // planned room rotations. Keep native position initialization and retain
        // the authoritative rotation for these room-owned structures only.
        if (__state.HasValue) __instance.transform.rotation = __state.Value;
    }
}
