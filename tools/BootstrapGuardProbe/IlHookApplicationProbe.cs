using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

internal static class IlHookApplicationProbe
{
    private static int _nested;

    internal static void Run(Action<bool, string> check)
    {
        MethodInfo apply = typeof(ILHook).GetMethod(nameof(ILHook.Apply))!;
        check(!(HarmonyLib.Harmony.GetPatchInfo(apply)?.Prefixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(RuntimeGuard)) ?? false),
            "ILHook.Apply has no Bootstrap interceptor");
        MethodInfo target = Target(nameof(External));
        MethodInfo nestedTarget = Target(nameof(Nested));
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        ProtectedMethodCatalog.RegisterExternalTarget(nestedTarget);
        var manual = new ILHookConfig { ManualApply = true };
        int callbacks = 0;
        bool barrier = false, workerDenied = false, manipulatorBarrier = false, nestedCompleted = false;
        ILHook? nested = null;
        Func<ILHook, MethodBase, ILContext.Manipulator, bool> observer = (_, original, _) =>
        {
            if (original != target) return true;
            ++callbacks;
            barrier |= RuntimeGuard.Snapshot.MutationInProgress > 0;
            workerDenied |= Task.Run(() => !RuntimeGuard.Verify(requireComplete: false)).GetAwaiter().GetResult();
            if (Interlocked.Exchange(ref _nested, 1) == 0)
            {
                nested = new ILHook(nestedTarget, context => Replace(context, 9));
                nestedCompleted = Nested() == 9 && RuntimeGuard.Snapshot.MutationInProgress > 0;
            }
            return true;
        };
        using var hook = new ILHook(target, context =>
        {
            manipulatorBarrier |= RuntimeGuard.Snapshot.MutationInProgress > 0;
            Replace(context, 7);
        }, manual);
        long before = RuntimeGuard.Snapshot.Generation;
        ILHook.OnDetour += observer;
        try { hook.Apply(); }
        finally { ILHook.OnDetour -= observer; }
        check(hook.IsApplied && External() == 7 && callbacks == 1, "manual ILHook activation is observed and applied");
        check(barrier && workerDenied && manipulatorBarrier, "admission barriers cover callbacks and IL generation");
        check(nestedCompleted, "nested application from a foreign subscriber closes its own operation");
        check(RuntimeGuard.Snapshot.MutationInProgress == 0 && RuntimeGuard.Snapshot.Generation > before,
            "refresh completion commits risk before releasing all mutation barriers");
        nested?.Dispose();
        before = RuntimeGuard.Snapshot.Generation;
        ((IDetour)hook).Apply();
        check(RuntimeGuard.Snapshot.Generation == before && RuntimeGuard.Snapshot.MutationInProgress == 0,
            "already-applied interface call does not invent a mutation");
        hook.Undo();
        before = RuntimeGuard.Snapshot.Generation;
        ((IDetour)hook).Apply();
        check(External() == 7 && RuntimeGuard.Snapshot.Generation > before && RuntimeGuard.Snapshot.MutationInProgress == 0,
            "interface reapplication remains tracked after Undo");
        hook.Undo();

        MethodInfo vetoTarget = Target(nameof(Vetoed));
        ProtectedMethodCatalog.RegisterExternalTarget(vetoTarget);
        using var vetoed = new ILHook(vetoTarget, context => Replace(context, 8), manual);
        Func<ILHook, MethodBase, ILContext.Manipulator, bool> veto = (_, original, _) => original != vetoTarget;
        before = RuntimeGuard.Snapshot.Generation;
        ILHook.OnDetour += veto;
        try { vetoed.Apply(); }
        finally { ILHook.OnDetour -= veto; }
        check(!vetoed.IsApplied && Vetoed() == 1 && RuntimeGuard.Snapshot.Generation == before && RuntimeGuard.Snapshot.MutationInProgress == 0,
            "subscriber veto closes tracking without applied-risk evidence");

        MethodInfo failedTarget = Target(nameof(Failed));
        ProtectedMethodCatalog.RegisterExternalTarget(failedTarget);
        using var failed = new ILHook(failedTarget, _ => throw new InvalidOperationException("deliberate IL generation failure"), manual);
        before = RuntimeGuard.Snapshot.Generation;
        bool threw = false;
        try { failed.Apply(); }
        catch (Exception exception) when (exception.ToString().Contains("deliberate IL generation failure", StringComparison.Ordinal)) { threw = true; }
        check(threw && Failed() == 1 && RuntimeGuard.Snapshot.Generation > before && RuntimeGuard.Snapshot.MutationInProgress == 0 &&
            (RuntimeGuard.Snapshot.Flags & 4) != 0, "failed refresh commits incomplete evidence and closes tracking");
        failed.Undo();

        MethodInfo callbackTarget = Target(nameof(CallbackFailed));
        ProtectedMethodCatalog.RegisterExternalTarget(callbackTarget);
        using var callbackFailed = new ILHook(callbackTarget, context => Replace(context, 8), manual);
        Func<ILHook, MethodBase, ILContext.Manipulator, bool> throwing = (_, original, _) =>
            original == callbackTarget ? throw new InvalidOperationException("deliberate callback failure") : true;
        ILHook.OnDetour += throwing;
        threw = false;
        try { callbackFailed.Apply(); }
        catch (Exception exception) when (exception.ToString().Contains("deliberate callback failure", StringComparison.Ordinal)) { threw = true; }
        finally { ILHook.OnDetour -= throwing; }
        check(threw && !callbackFailed.IsApplied && CallbackFailed() == 1 && RuntimeGuard.Snapshot.MutationInProgress == 0,
            "throwing subscriber closes tracking before refresh");

        MethodInfo protectedTarget = Target(nameof(Protected));
        ProtectedMethodCatalog.RegisterInfrastructureTarget(protectedTarget);
        using var denied = new ILHook(protectedTarget, context => Replace(context, 88), manual);
        denied.Apply();
        check(!denied.IsApplied && Protected() == 1, "callback still blocks protected ILHook activation");
        TrustedLeaseProbe.Run(check);
        check(RuntimeGuard.Verify(requireComplete: false), "all remaining guard interceptors and callback identities verify");
    }

    internal static void Tamper(Action<bool, string> check)
    {
        MethodInfo target = Target(nameof(External));
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        var hook = new ILHook(target, context => Replace(context, 8), new ILHookConfig { ManualApply = true });
        ILHook.OnDetour = null;
        hook.Apply();
        check(External() == 1 && !RuntimeGuard.Snapshot.Healthy && !RuntimeGuard.Verify(requireComplete: false),
            "missing ILHook callback fails closed at refresh before native application");
        check(RuntimeGuard.Snapshot.MutationInProgress == 0, "callback removal does not leave an accepted mutation lease");
    }

    internal static void Abandon(Action<bool, string> check)
    {
        MethodInfo target = Target(nameof(External));
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        var hook = new ILHook(target, context => Replace(context, 8), new ILHookConfig { ManualApply = true });
        Func<ILHook, MethodBase, ILContext.Manipulator, bool> removeContext = (_, original, _) =>
        {
            if (original == target)
            {
                var map = (System.Collections.IDictionary)GuardRuntimeAdapters.IlHookMap.GetValue(null)!;
                lock (map) map.Remove(target);
            }
            return true;
        };
        ILHook.OnDetour += removeContext;
        bool threw = false;
        try { hook.Apply(); }
        catch (NullReferenceException) { threw = true; }
        finally { ILHook.OnDetour -= removeContext; }
        check(threw && External() == 1 && RuntimeGuard.Snapshot.MutationInProgress > 0 && !RuntimeGuard.Verify(requireComplete: false),
            "exception before refresh leaves admission closed");
        RuntimeGuard.Update();
        check(!RuntimeGuard.Snapshot.Healthy && RuntimeGuard.Snapshot.MutationInProgress == 0,
            "next frame commits abandoned application failure before releasing its barrier");
    }

    private static MethodInfo Target(string name) => typeof(IlHookApplicationProbe).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Replace(ILContext context, int value)
    {
        context.Body.Instructions.Clear();
        var il = context.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, value);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int External() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Nested() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Vetoed() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Failed() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int CallbackFailed() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Protected() => 1;
}
