using System.Reflection;

namespace Anomaly.Bootstrap;

internal static partial class RuntimeGuard
{
    private const int RequiredCallbackCount = 11;
    private static readonly StructuralBaseline Structural = new();

    private sealed class StructuralBaseline
    {
        internal GuardAudit? Audit;
        internal (MethodBase Target, MethodInfo Prefix, MethodInfo Finalizer)[] Hooks = Array.Empty<(MethodBase, MethodInfo, MethodInfo)>();
        internal (MethodBase Target, MethodInfo Prefix)[] StockHooks = Array.Empty<(MethodBase, MethodInfo)>();
        internal (FieldInfo Field, Delegate Guard, Delegate[] Previous)[] Callbacks = Array.Empty<(FieldInfo, Delegate, Delegate[])>();
        internal Dictionary<MethodBase, RequiredPatch[]> Required = new();
    }

    private static void CaptureStructuralBaseline()
    {
        Structural.Audit = _audit;
        Structural.Hooks = Hooks.ToArray();
        Structural.StockHooks = RequiredStockHooks.ToArray();
        Structural.Callbacks = Callbacks.Select(slot => (slot.Field, slot.Guard, slot.Previous.ToArray())).ToArray();
        lock (RequiredSync) RefreshRequiredBaselineLocked();
    }

    private static void RefreshRequiredBaselineLocked()
    {
        if (Structural.Audit == null) return;
        Structural.Required = RequiredPatches.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static bool CheckStructuralInvariants()
    {
        string? failure = null;
        if (!ReferenceEquals(_audit, Structural.Audit))
            failure = BootstrapLocalization.Get(TrKeys.Bootstrap_AuditReplaced);
        else if (!Hooks.SequenceEqual(Structural.Hooks) || !RequiredStockHooks.SequenceEqual(Structural.StockHooks))
            failure = BootstrapLocalization.Get(TrKeys.Bootstrap_InterceptorInventoryChanged);
        else if (Callbacks.Count != RequiredCallbackCount || Structural.Callbacks.Length != RequiredCallbackCount)
            failure = BootstrapLocalization.Get(TrKeys.Bootstrap_CallbackInventoryChanged);
        else
        {
            for (int i = 0; i < Callbacks.Count; ++i)
            {
                var actual = Callbacks[i];
                var expected = Structural.Callbacks[i];
                var requirement = RequiredCallback(i);
                FieldInfo? requiredField = requirement.Type.GetField(requirement.FieldName, BindingFlags.Public | BindingFlags.Static);
                MethodInfo requiredMethod = RequiredMethod(typeof(RuntimeGuard), requirement.MethodName);
                if (actual.Field != requiredField || actual.Guard.Target != null || actual.Guard.Method != requiredMethod ||
                    actual.Field != expected.Field || !Equals(actual.Guard, expected.Guard) ||
                    !actual.Previous.SequenceEqual(expected.Previous))
                {
                    failure = BootstrapLocalization.Get(TrKeys.Bootstrap_CallbackInventoryChanged);
                    break;
                }
            }
        }
        if (failure == null)
        {
            lock (RequiredSync)
            {
                if (RequiredPatches.Count != Structural.Required.Count ||
                    RequiredPatches.Any(pair => !Structural.Required.TryGetValue(pair.Key, out var expected) ||
                        !pair.Value.SequenceEqual(expected)))
                    failure = BootstrapLocalization.Get(TrKeys.Bootstrap_PatchInventoryChanged);
            }
        }
        if (failure == null && !ProtectedMethodCatalog.TrustedInventoryUnchanged())
            failure = BootstrapLocalization.Get(TrKeys.Bootstrap_TrustedInventoryChanged);
        if (failure == null && !NativeCodeIntegrity.Verify(out string? nativeFailure))
            failure = nativeFailure;
        if (failure == null) return true;
        Invalidate(failure);
        return false;
    }

    private static (Type Type, string FieldName, string MethodName) RequiredCallback(int index) => index switch
    {
        0 => (typeof(MonoMod.RuntimeDetour.Hook), "OnDetour", nameof(ObserveHook)),
        1 => (typeof(MonoMod.RuntimeDetour.ILHook), "OnDetour", nameof(ObserveILHook)),
        2 => (typeof(MonoMod.RuntimeDetour.Detour), "OnDetour", nameof(ObserveDetour)),
        3 => (typeof(MonoMod.RuntimeDetour.NativeDetour), "OnDetour", nameof(ObserveNative)),
        4 => (typeof(MonoMod.RuntimeDetour.Hook), "OnUndo", nameof(AllowHookUndo)),
        5 => (typeof(MonoMod.RuntimeDetour.ILHook), "OnUndo", nameof(AllowIlHookUndo)),
        6 => (typeof(MonoMod.RuntimeDetour.Detour), "OnUndo", nameof(AllowDetourUndo)),
        7 => (typeof(MonoMod.RuntimeDetour.NativeDetour), "OnUndo", nameof(AllowNativeDetourUndo)),
        8 => (typeof(MonoMod.RuntimeDetour.Hook), "OnGenerateTrampoline", nameof(KeepHookTrampoline)),
        9 => (typeof(MonoMod.RuntimeDetour.Detour), "OnGenerateTrampoline", nameof(KeepDetourTrampoline)),
        10 => (typeof(MonoMod.RuntimeDetour.NativeDetour), "OnGenerateTrampoline", nameof(KeepNativeDetourTrampoline)),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
