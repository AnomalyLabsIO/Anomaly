using Anomaly.Client.Api.Scripting;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace Anomaly.Client.Patches.Runtime;

// Intercept the conversion itself: checking a returned managed string is too late.
// A null pointer follows the interop method's existing null-return path unchanged.
[HarmonyPatch(typeof(IL2CPP), nameof(IL2CPP.Il2CppStringToManaged), new[] { typeof(IntPtr) })]
internal static class NativeScriptStringPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(IntPtr __0)
    {
        if (!ScriptHostAccess.IsActive || __0 == IntPtr.Zero)
            return;
        ScriptHostAccess.ThrowIfFailed();
        ScriptHostAccess.CheckNativeStringLength(IL2CPP.il2cpp_string_length(__0));
    }
}
