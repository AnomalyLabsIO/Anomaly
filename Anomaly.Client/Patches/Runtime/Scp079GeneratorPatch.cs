using Anomaly.Client.Internal.Bootstrap;
using HarmonyLib;
using Il2CppMapGeneration.Distributors;

namespace Anomaly.Client.Patches.Runtime;

// Class-level [HarmonyPatch] is REQUIRED: HarmonyX's PatchAll skips any type whose
// container has no class-level Harmony attribute (allowUnannotatedType:false), even
// when its methods carry [HarmonyPatch]. Without this the generator-engaged hook
// (and GeneratorEvents.OnEngaged) silently never fires.
[HarmonyPatch]
public static class Scp079GeneratorPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.DeserializeSyncVars))]
    public static void DeserializeSyncVarsPrefix(Scp079Generator __instance, out byte __state)
    {
        __state = __instance == null ? (byte)0 : __instance.Network_flags;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.DeserializeSyncVars))]
    public static void DeserializeSyncVarsPostfix(Scp079Generator __instance, bool initialState, byte __state)
    {
        if (initialState || __instance == null)
            return;

        const Scp079Generator.GeneratorFlags engaged = Scp079Generator.GeneratorFlags.Engaged;
        bool wasEngaged = ((__state & (byte)engaged) != 0);
        bool isEngaged = ((__instance.Network_flags & (byte)engaged) != 0);

        if (!wasEngaged && isEngaged)
            ClientRuntime.HandleGeneratorEngaged(__instance);
    }
}
