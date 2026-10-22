using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Bootstrap;
using MonoMod.RuntimeDetour;

internal static class NativeIdentityProbe
{
    internal static void Run(string mode, Action<bool, string> check)
    {
        MethodInfo protectedTarget = typeof(NativeIdentityProbe).GetMethod(nameof(Protected), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo externalTarget = typeof(NativeIdentityProbe).GetMethod(nameof(External), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo decoy = typeof(NativeIdentityProbe).GetMethod(nameof(Decoy), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo replacement = typeof(NativeIdentityProbe).GetMethod(nameof(Replacement), BindingFlags.NonPublic | BindingFlags.Static)!;
        ProtectedMethodCatalog.RegisterInfrastructureTarget(protectedTarget);
        ProtectedMethodCatalog.RegisterExternalTarget(externalTarget);

        // Isolate the guard's address/metadata selection from IL2CPP startup. The
        // production catalog populates this same finite map after support is ready.
        var map = (Dictionary<IntPtr, MethodBase>)typeof(ProtectedMethodCatalog)
            .GetField("NativeTargets", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        map[protectedTarget.GetNativeStart()] = protectedTarget;
        map[externalTarget.GetNativeStart()] = externalTarget;

        long generation = RuntimeGuard.Snapshot.Generation;
        using (var native = new NativeDetour(decoy, protectedTarget.GetNativeStart(), replacement.GetNativeStart()))
            check(!native.IsValid && Protected() == 1, "native decoy metadata cannot hide a protected source address");
        check(RuntimeGuard.Snapshot.Generation == generation, "blocked native decoy does not create applied risk");

        var manual = new NativeDetourConfig { ManualApply = true };
        using (var native = new NativeDetour(decoy, decoy.GetNativeStart(), replacement.GetNativeStart(), manual))
        {
            IntPtr originalSource = native.Data.Method;
            native.ChangeSource(protectedTarget.GetNativeStart());
            check(native.Data.Method == originalSource && !native.IsApplied && Protected() == 1,
                "native retarget cannot replace a decoy source with a protected source");
        }

        generation = RuntimeGuard.Snapshot.Generation;
        using (var native = new NativeDetour(decoy, externalTarget.GetNativeStart(), replacement.GetNativeStart()))
        {
            check(native.IsValid == (mode == "allow"), "native decoy external source obeys client policy");
            check(External() == (mode == "allow" ? 55 : 1), "native decoy external source outcome");
            if (mode == "allow")
                check(RuntimeGuard.Snapshot.Generation > generation && (RuntimeGuard.Snapshot.Flags & 2) != 0,
                    "native decoy external source is audited");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static int Protected() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int External() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Decoy() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Replacement() => 55;
}
