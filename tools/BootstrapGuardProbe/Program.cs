using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Anomaly.Bootstrap;
using HarmonyLib;
using MelonLoader;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
        try { return RunMain(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Bootstrap guard probe: " + exception);
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMain(string[] args)
    {
        if (args.Length != 2 || args[1] is not ("strict" or "allow" or "preexisting" or "stocktamper" or "scheduling" or "clr-stubs" or "platformtamper" or "ilhook-apply" or "ilhook-tamper" or "ilhook-abandoned"))
            throw new ArgumentException("Usage: Bootstrap.GuardProbe <loader-net6-directory> <strict|allow|preexisting|stocktamper|scheduling|clr-stubs|platformtamper|ilhook-apply|ilhook-tamper|ilhook-abandoned>");
        string libraries = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(libraries, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run(args[1]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string mode)
    {
        var loader = new HarmonyLib.Harmony("MelonLoader");
        Type loaderCore = typeof(PatchShield).Assembly.GetType("MelonLoader.Core", true)!;
        loaderCore.GetField("HarmonyInstance", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, loader);
        typeof(PatchShield).GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        var foreign = new HarmonyLib.Harmony("anomaly.guard.probe.foreign");
        MethodInfo target = typeof(Fixture).GetMethod(nameof(Fixture.Critical))!;
        MethodInfo protectedTarget = typeof(Fixture).GetMethod(nameof(Fixture.Protected))!;
        MethodInfo control = typeof(Fixture).GetMethod(nameof(Fixture.Control))!;
        MethodInfo prefix = typeof(Program).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        ProtectedMethodCatalog.RegisterExternalTarget(target);
        ProtectedMethodCatalog.RegisterInfrastructureTarget(protectedTarget);
        if (mode == "preexisting")
            foreign.Patch(target, prefix: new HarmonyMethod(prefix));
        try { RuntimeGuard.Install(new HarmonyLib.Harmony("anomaly.guard.probe"), mode is "allow" or "scheduling" or "ilhook-apply" or "ilhook-tamper" or "ilhook-abandoned"); }
        catch { Console.WriteLine(string.Join("\n", RuntimeGuard.Diagnostics)); throw; }
        Check(RuntimeGuard.Snapshot.Healthy, "guard healthy after install");
        if (mode is "ilhook-apply" or "ilhook-tamper" or "ilhook-abandoned")
        {
            if (mode == "ilhook-apply") IlHookApplicationProbe.Run(Check);
            else if (mode == "ilhook-tamper") IlHookApplicationProbe.Tamper(Check);
            else IlHookApplicationProbe.Abandon(Check);
            Console.WriteLine("PASS " + mode + " flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
            return 0;
        }
        if (mode is "clr-stubs" or "platformtamper")
        {
            if (mode == "clr-stubs") ClrEntryStubProbe.Run(Check);
            else ClrEntryStubProbe.Tamper(Check);
            Console.WriteLine("PASS " + mode + " flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
            return 0;
        }
        if (mode == "scheduling")
        {
            VerificationSchedulingProbe.Run(Check);
            Console.WriteLine("PASS scheduling flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
            return 0;
        }
        if (mode == "preexisting")
        {
            Check((RuntimeGuard.Snapshot.Flags & 1) != 0, "preexisting patch audited");
            Console.WriteLine("PASS preexisting flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
            return 0;
        }
        if (mode == "stocktamper")
        {
            MethodInfo processor = typeof(PatchProcessor).GetMethod("Patch")!;
            var info = HarmonyLib.Public.Patching.PatchManager.GetPatchInfo(processor);
            info.prefixes = info.prefixes.Where(p => p.PatchMethod.DeclaringType != typeof(PatchShield)).ToArray();
            Check(!RuntimeGuard.Verify(requireComplete: false), "missing stock Shield prefix is fatal");
            Console.WriteLine("PASS stocktamper flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
            return 0;
        }

        TrustedLeaseProbe.Run(Check);
        AllowedRebuildProbe.Run(mode, Check);
        OutcomeProbe.Run(mode, Check);
        foreign.Patch(control, prefix: new HarmonyMethod(prefix));
        Check(Fixture.Control() == 99, "control patch applies");
        foreign.Unpatch(control, prefix);
        Check(Fixture.Control() == 1, "control patch removed");

        long before = RuntimeGuard.Snapshot.Generation;
        foreign.Patch(protectedTarget, prefix: new HarmonyMethod(prefix));
        Check(Fixture.Protected() == 1, "direct protected patch blocked");
        foreign.CreateClassProcessor(typeof(ProtectedPatch)).Patch();
        Check(Fixture.Protected() == 1, "class protected patch blocked");
        Check((HarmonyLib.Harmony.GetPatchInfo(protectedTarget)?.Prefixes.Count ?? 0) == 0, "blocked class patch leaves no latent PatchInfo");
        Check(RuntimeGuard.Snapshot.Generation == before, "blocked attempt does not invalidate admission generation");
        Check((RuntimeGuard.Snapshot.Flags & 16) != 0, "blocked attempt audited separately");
        foreach (HarmonyPatchType kind in new[] { HarmonyPatchType.Prefix, HarmonyPatchType.Postfix, HarmonyPatchType.Transpiler, HarmonyPatchType.Finalizer, HarmonyPatchType.ILManipulator })
        {
            var processor = new PatchProcessor(foreign, protectedTarget);
            switch (kind)
            {
                case HarmonyPatchType.Prefix: processor.AddPrefix(prefix); break;
                case HarmonyPatchType.Postfix: processor.AddPostfix(prefix); break;
                case HarmonyPatchType.Transpiler: processor.AddTranspiler(prefix); break;
                case HarmonyPatchType.Finalizer: processor.AddFinalizer(prefix); break;
                case HarmonyPatchType.ILManipulator: processor.AddILManipulator(prefix); break;
            }
            processor.Patch();
        }
        Check(HarmonyLib.Harmony.GetPatchInfo(protectedTarget) == null, "all blocked patch kinds leave no state");
        MethodInfo reverse = typeof(Fixture).GetMethod(nameof(Fixture.Reverse))!;
        foreign.CreateReversePatcher(protectedTarget, new HarmonyMethod(reverse)).Patch();
        Check(Fixture.Reverse() == -1, "protected reverse source blocked");
        foreign.CreateReversePatcher(control, new HarmonyMethod(protectedTarget)).Patch();
        Check(Fixture.Protected() == 1, "protected reverse destination blocked");
        foreign.CreateClassProcessor(typeof(BulkPatch)).Patch();
        Check(Fixture.Protected() == 1 && Fixture.Control() == 88, "bulk job separates protected and unprotected targets");
        foreign.Unpatch(control, HarmonyPatchType.All, foreign.Id);

        foreign.Patch(target, prefix: new HarmonyMethod(prefix));
        Check(Fixture.Critical() == (mode == "allow" ? 99 : 1), "external policy enforced");
        if (mode == "allow")
        {
            Check((RuntimeGuard.Snapshot.Flags & 1) != 0, "allowed Harmony change audited");
            long first = RuntimeGuard.Snapshot.Generation;
            foreign.Unpatch(target, prefix);
            foreign.Patch(target, prefix: new HarmonyMethod(prefix));
            Check(RuntimeGuard.Snapshot.Generation > first, "repeat same target invalidates admission generation");
            foreign.Unpatch(target, prefix);
        }
        else
            Check((RuntimeGuard.Snapshot.Flags & 15) == 0, "blocked changes are not applied risk");

        using (var hook = new Hook(protectedTarget, (Func<int>)(() => 77)))
        {
            Check(!hook.IsApplied && Fixture.Protected() == 1, "direct MonoMod hook blocked");
        }
        using (var hook = new Hook(target, (Func<int>)(() => 77)))
        {
            Check(hook.IsApplied == (mode == "allow"), "external detour policy");
            Check(Fixture.Critical() == (mode == "allow" ? 77 : 1), "external detour outcome");
        }
        using (var hook = new ILHook(protectedTarget, ctx =>
        {
            ctx.Body.Instructions.Clear();
            var il = ctx.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 76);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        }))
            Check(!hook.IsApplied && Fixture.Protected() == 1, "direct ILHook blocked");

        MethodInfo nativeReplacement = typeof(Fixture).GetMethod(nameof(Fixture.NativeReplacement))!;
        using (var native = new NativeDetour(protectedTarget, nativeReplacement.GetNativeStart()))
            Check(!native.IsValid && Fixture.Protected() == 1, "managed-metadata native detour blocked");
        using (var native = new NativeDetour(protectedTarget, nativeReplacement))
            Check(!native.IsValid && Fixture.Protected() == 1, "MethodBase native constructor remains protected before pointer catalog");
        using (var native = new NativeDetour(target, nativeReplacement.GetNativeStart()))
        {
            Check(native.IsValid == (mode == "allow"), "native external policy");
            Check(Fixture.Critical() == (mode == "allow" ? 55 : 1), "native detour outcome");
            if (mode == "allow")
            {
                long applied = RuntimeGuard.Snapshot.Generation;
                native.Undo();
                native.Apply();
                native.ChangeTarget(control.GetNativeStart());
                Check(RuntimeGuard.Snapshot.Generation > applied, "native undo reapply and retarget audited");
            }
        }

        NativeIdentityProbe.Run(mode, Check);
        MethodInfo guardTarget = typeof(PatchProcessor).GetMethod("Patch")!;
        var beforeRemoval = HarmonyLib.Harmony.GetPatchInfo(guardTarget)!;
        MethodInfo guardPrefix = beforeRemoval.Prefixes.Single(p => p.PatchMethod.DeclaringType == typeof(RuntimeGuard)).PatchMethod;
        foreign.Unpatch(guardTarget, guardPrefix);
        foreign.Unpatch(guardTarget, HarmonyPatchType.All, "*");
        HarmonyLib.Harmony.UnpatchID("anomaly.guard.probe");
        Check(HarmonyLib.Harmony.GetPatchInfo(guardTarget)!.Prefixes.Any(p => p.PatchMethod == guardPrefix), "required interceptor survives every unpatch selector");
        Check(RuntimeGuard.Verify(requireComplete: false), "guard survives patch/unpatch/detour operations");
        Hook.OnDetour = null;
        Check(!RuntimeGuard.Verify(requireComplete: false) && !RuntimeGuard.Snapshot.Healthy, "callback tampering makes guard permanently unhealthy");
        Console.WriteLine("PASS " + mode + " flags=" + RuntimeGuard.Snapshot.Flags + " generation=" + RuntimeGuard.Snapshot.Generation);
        return 0;
    }

    private static bool Prefix(ref int __result) { __result = 99; return false; }
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message + "\n" + string.Join("\n", RuntimeGuard.Diagnostics));
        Console.WriteLine("PASS " + message);
    }

    [HarmonyPatch(typeof(Fixture), nameof(Fixture.Protected))]
    private static class ProtectedPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref int __result) { __result = 88; return false; }
    }

    [HarmonyPatch]
    private static class BulkPatch
    {
        [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets() => new[] { typeof(Fixture).GetMethod(nameof(Fixture.Protected))!, typeof(Fixture).GetMethod(nameof(Fixture.Control))! };
        [HarmonyPrefix] private static bool Prefix(ref int __result) { __result = 88; return false; }
    }
}

internal static class Fixture
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Critical() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Protected() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Control() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Reverse() => -1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int NativeReplacement() => 55;
}
