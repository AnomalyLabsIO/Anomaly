using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

internal static class TrustedLeaseProbe
{
    internal static void Run(Action<bool, string> check)
    {
        var harmony = new HarmonyLib.Harmony("anomaly.guard.probe.trusted");
        MethodInfo target = typeof(TrustedLeaseProbe).GetMethod(nameof(Target), BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo prefix = typeof(TrustedLeaseProbe).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!;
        ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
        ProtectedMethodCatalog.RegisterTrustedPatch(target, prefix, HarmonyPatchType.Prefix);
        bool callbackSeen = false;
        bool nestedApplied = false;
        MethodBase? publishedReplacement = null;
        Func<Detour, MethodBase, MethodBase, bool> replacementObserver = (_, original, replacement) =>
        {
            if (original == target) publishedReplacement = replacement;
            return true;
        };
        Func<ILHook, MethodBase, MonoMod.Cil.ILContext.Manipulator, bool> callback = (_, original, _) =>
        {
            if (original != target) return true;
            callbackSeen = true;
            using var nested = new Hook(target, (Func<int>)(() => 700));
            nestedApplied |= nested.IsApplied;
            harmony.Unpatch(target, prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            return true;
        };
        long before = RuntimeGuard.Snapshot.Generation;
        ILHook.OnDetour += callback;
        Detour.OnDetour += replacementObserver;
        try { harmony.Patch(target, prefix: new HarmonyMethod(prefix)); }
        finally { ILHook.OnDetour -= callback; Detour.OnDetour -= replacementObserver; }
        check(callbackSeen, "ordinary foreign callback executes during required patch installation");
        check(!nestedApplied && Target() == 321, "foreign callback cannot borrow trusted backend authority");
        check(!ILHook.OnDetour!.GetInvocationList().Contains(callback), "ordinary callback unsubscribe preserves delegate identity");
        check(HarmonyLib.Harmony.GetPatchInfo(target)!.Prefixes.Count(p => p.PatchMethod == prefix) == 1, "required tuple cannot be duplicated from a callback");
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        check(HarmonyLib.Harmony.GetPatchInfo(target)!.Prefixes.Count(p => p.PatchMethod == prefix) == 1, "required tuple cannot be duplicated by a different public operation");
        var patcher = PatchManager.GetMethodPatcher(target);
        var owned = (ILHook)typeof(ManagedMethodPatcher).GetField("ilHook", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(patcher)!;
        string originalId = owned.ID;
        owned.ID = "foreign-reorder";
        check(owned.ID == originalId && Target() == 321, "required detour ordering metadata cannot be changed");
        check(publishedReplacement != null, "ordinary MonoMod observer can identify generated backend replacement");
        check(ProtectedMethodCatalog.Classify(publishedReplacement!) == ProtectionScope.Infrastructure, "generated required backend replacement is protected infrastructure");
        try { harmony.Patch(publishedReplacement!, prefix: new HarmonyMethod(prefix)); }
        catch (NullReferenceException) when (ReproducesStockDynamicDenial(harmony, publishedReplacement!))
        {
            // The preserved loader prefix can reject dynamic methods by throwing.
            // Accept only an independently reproduced stock failure after our
            // exact denial was recorded; the state checks below still must pass.
        }
        check(HarmonyLib.Harmony.GetPatchInfo(publishedReplacement!) == null, "direct Harmony patch of generated required wrapper is blocked");
        bool hookApplyObserved = false;
        Func<Hook, MethodBase, MethodBase, object, bool> observeHook = (_, original, _, _) =>
        {
            if (original == publishedReplacement) hookApplyObserved = true;
            return true;
        };
        Hook.OnDetour += observeHook;
        try
        {
            using var hook = new Hook(publishedReplacement!, (Func<int>)(() => 999));
            check(!hook.IsApplied && Target() == 321, "direct Hook of generated required wrapper is blocked");
        }
        catch (InvalidOperationException exception) when (ReproducesUnsupportedDynamicCopy(publishedReplacement!, exception))
        {
            check(!hookApplyObserved && Target() == 321, "installed MonoMod rejects unsupported DynamicMethod copy before Hook application");
        }
        finally { Hook.OnDetour -= observeHook; }
        MethodInfo nativeReplacement = typeof(Fixture).GetMethod(nameof(Fixture.NativeReplacement))!;
        using (var native = new NativeDetour(publishedReplacement!.GetNativeStart(), nativeReplacement.GetNativeStart()))
            check(!native.IsApplied && Target() == 321, "raw native detour of generated required wrapper is blocked");
        check(RuntimeGuard.Snapshot.Generation == before, "blocked callback and required-detour attempts do not change risk generation");
        check(RuntimeGuard.Snapshot.Healthy, "wrapper denial preserves mandatory guard health");
    }

    private static bool ReproducesStockDynamicDenial(HarmonyLib.Harmony harmony, MethodBase target)
    {
        if (!ProtectedMethodCatalog.IsDynamicNativeTarget(target)) return false;
        string denial = "Harmony patch blocked: " + (target.DeclaringType?.FullName ?? "<dynamic>") + "." + target.Name;
        if (!RuntimeGuard.Diagnostics.Any(entry => entry.Contains(denial, StringComparison.Ordinal))) return false;
        MethodInfo processorMethod = typeof(PatchProcessor).GetMethod(nameof(PatchProcessor.Patch))!;
        MethodInfo? stock = HarmonyLib.Harmony.GetPatchInfo(processorMethod)?.Prefixes
            .Select(patch => patch.PatchMethod).SingleOrDefault(method => method.DeclaringType == typeof(MelonLoader.PatchShield));
        if (stock == null) return false;
        ParameterInfo[] parameters = stock.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameter = parameters[i].ParameterType;
            if (parameter.IsByRef) parameter = parameter.GetElementType()!;
            if (parameters[i].Name == "__result") arguments[i] = null;
            else if (parameter == typeof(PatchProcessor)) arguments[i] = new PatchProcessor(harmony, target);
            else if (typeof(MethodBase).IsAssignableFrom(parameter)) arguments[i] = target;
            else return false;
        }
        try { stock.Invoke(null, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is NullReferenceException)
        {
            Console.WriteLine("PASS preserved stock PatchShield independently reproduces dynamic-method denial exception");
            return true;
        }
        return false;
    }

    private static bool ReproducesUnsupportedDynamicCopy(MethodBase target, InvalidOperationException observed)
    {
        static bool IsUnsupportedBody(Exception exception) => exception.TargetSite?.DeclaringType == typeof(MethodBase) &&
            exception.TargetSite.Name == nameof(MethodBase.GetMethodBody);
        if (!ProtectedMethodCatalog.IsDynamicNativeTarget(target) || !IsUnsupportedBody(observed)) return false;
        try { using var copy = new DynamicMethodDefinition(target); }
        catch (InvalidOperationException reproduced) when (IsUnsupportedBody(reproduced))
        {
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static int Target() => 1;
    private static bool Prefix(ref int __result) { __result = 321; return false; }
}
