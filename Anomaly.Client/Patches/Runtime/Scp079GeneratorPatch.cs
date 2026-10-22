using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using HarmonyLib;
using Il2CppMapGeneration.Distributors;
using MelonLoader;

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
    public static void DeserializeSyncVarsPrefix(Scp079Generator __instance, out byte? __state)
    {
        __state = null;
        try { if (__instance != null) __state = __instance.Network_flags; }
        catch (Exception ex) { MelonLogger.Error($"Scp079GeneratorPatch.DeserializeSyncVarsPrefix: {ex}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.DeserializeSyncVars))]
    public static void DeserializeSyncVarsPostfix(Scp079Generator __instance, bool initialState, byte? __state)
    {
        try
        {
            if (initialState || __instance == null || !__state.HasValue)
                return;

            byte previous = __state.Value;
            byte current = __instance.Network_flags;
            if (current != previous && Generator.Get(__instance) is { } generator)
                GeneratorEvents.OnStateChanged(new GeneratorStateChangedEventArgs(generator,
                    (Scp079Generator.GeneratorFlags)previous, (Scp079Generator.GeneratorFlags)current));

            const Scp079Generator.GeneratorFlags engaged = Scp079Generator.GeneratorFlags.Engaged;
            bool wasEngaged = ((previous & (byte)engaged) != 0);
            bool isEngaged = ((current & (byte)engaged) != 0);

            if (!wasEngaged && isEngaged)
                ClientRuntime.HandleGeneratorEngaged(__instance);
        }
        catch (Exception ex) { MelonLogger.Error($"Scp079GeneratorPatch.DeserializeSyncVarsPostfix: {ex}"); }
    }
}
