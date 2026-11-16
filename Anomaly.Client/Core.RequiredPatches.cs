using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Anomaly.Client;

public partial class Core
{
    private readonly struct RequiredClientPatch
    {
        internal readonly MethodBase Target;
        internal readonly MethodInfo Patch;
        internal readonly HarmonyPatchType Kind;

        internal RequiredClientPatch(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
        {
            Target = target;
            Patch = patch;
            Kind = kind;
        }
    }

    private static void RequireExactPatch(string owner, RequiredClientPatch required)
    {
        HarmonyLib.Patches patchInfo = HarmonyLib.Harmony.GetPatchInfo(required.Target);
        var patches = required.Kind == HarmonyPatchType.Postfix ? patchInfo?.Postfixes : patchInfo?.Prefixes;
        int count = patches?.Count(patch => patch.owner == owner && patch.PatchMethod == required.Patch) ?? 0;
        if (count != 1)
            throw new InvalidOperationException(
                $"Mandatory Anomaly patch must be installed exactly once (found {count}): " +
                $"{required.Target.DeclaringType?.FullName}.{required.Target.Name} -> {required.Patch.Name}.");
    }
}
