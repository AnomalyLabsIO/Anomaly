using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using MelonLoader;
using MonoMod.RuntimeDetour;
using HostHarmony = HarmonyLib.Harmony;

namespace Anomaly.Bootstrap;

// This adapter deliberately targets the loader-provided HarmonyX 2.10.2 / MonoMod
// 22.7.31 contract. Missing internals are a coverage failure, never a clean audit.
internal static partial class RuntimeGuard
{
    private static readonly object Sync = new();
    private static readonly object InstallationSync = new();
    private static readonly List<(MethodBase Target, MethodInfo Prefix, MethodInfo Finalizer)> Hooks = new();
    private static readonly List<(MethodBase Target, MethodInfo Prefix)> RequiredStockHooks = new();
    private static readonly Dictionary<object, MethodBase> OwnedDetours = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<object, MethodBase> ObservedDetours = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<MethodBase> ProtectedBackendReplacements = new();
    private static readonly List<(FieldInfo Field, Delegate Guard, Delegate[] Previous)> Callbacks = new();
    private static readonly DeferredNativeObservations DeferredNative = new();
    private static readonly GuardAudit NotInstalled = new(false);
    private static GuardAudit _audit = NotInstalled;
    private static bool _installed;
    private static bool _installing;
    private static PatchInstallationTrace? _installationTrace;
    [ThreadStatic] private static Stack<Operation>? _operations;
    [ThreadStatic] private static MethodBase? _canaryTarget;
    [ThreadStatic] private static int _foreignCallbackDepth;

    internal static GuardSnapshot Snapshot => _audit.Snapshot;
    internal static string[] Diagnostics => _audit.Diagnostics;

    internal static void ReportCoverageGap(string reason) => _audit.Record(GuardAuditFlags.CoverageIncomplete, reason);
    internal static void ReportInventoryDivergence(string reason) => _audit.Record(GuardAuditFlags.InventoryDivergence, reason);
    internal static void Invalidate(string reason) => _audit.Record(GuardAuditFlags.CoverageIncomplete, reason, fatal: true);
    internal static void ReportNativeObservation(MethodBase target) => _audit.Record(GuardAuditFlags.ExternalDetourModification,
        "Deferred native modification resolved to protected source: " + Describe(target),
        fatal: ProtectedMethodCatalog.Classify(target) is ProtectionScope.Core or ProtectionScope.Infrastructure, occurrence: true);

    internal static void Install(HostHarmony harmony, bool allowExternal)
    {
        lock (InstallationSync)
        {
            if (_installed || _installing)
                throw new InvalidOperationException("The Bootstrap runtime guard may only be installed once.");
            _installing = true;
            _audit = new GuardAudit(allowExternal);
            using var trace = PatchInstallationTrace.Open();
            _installationTrace = trace;
            try
            {
                if (typeof(HostHarmony).Assembly.GetName().Version != new Version(2, 10, 2, 0) ||
                    typeof(Detour).Assembly.GetName().Version != new Version(22, 7, 31, 1))
                    throw new NotSupportedException("Unsupported loader Harmony/MonoMod contract.");

                ProtectedMethodCatalog.ResolveLoadedAssemblies();
                ProtectBackendInfrastructure();
                RecordRequiredStockHooks();
                InstallHook(harmony, RequiredMethod(typeof(PatchProcessor), "Patch", Type.EmptyTypes), nameof(PatchPrefix), nameof(HarmonyInstallFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(PatchClassProcessor), "ProcessPatchJob"), nameof(ClassPatchPrefix), nameof(HarmonyFinalizer));
                foreach (MethodInfo method in typeof(PatchProcessor).GetMethods().Where(m => m.Name == "Unpatch"))
                    InstallHook(harmony, method, nameof(UnpatchPrefix), nameof(HarmonyFinalizer));
                Type functions = typeof(HostHarmony).Assembly.GetType("HarmonyLib.PatchFunctions", throwOnError: true)!;
                InstallHook(harmony, RequiredMethod(functions, "ReversePatch"), nameof(ReversePrefix), nameof(HarmonyFinalizer));

                // Apply/Free boundaries are required in addition to public callbacks:
                // Free removes MonoMod bookkeeping even if an OnUndo callback vetoes it.
                foreach (Type type in new[] { typeof(Hook), typeof(ILHook), typeof(Detour), typeof(NativeDetour) })
                    foreach (string name in new[] { "Apply", "Undo", "Free", "Dispose" })
                        InstallHook(harmony, RequiredMethod(type, name, Type.EmptyTypes), nameof(DetourPrefix), nameof(DetourFinalizer));
                foreach (Type type in new[] { typeof(ILHook), typeof(Detour) })
                    foreach (string name in new[] { "Priority", "ID", "Before", "After" })
                        InstallHook(harmony, type.GetProperty(name)!.SetMethod!, nameof(DetourPrefix), nameof(DetourFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(NativeDetour), "ChangeSource"), nameof(DetourPrefix), nameof(DetourFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(NativeDetour), "ChangeTarget"), nameof(DetourPrefix), nameof(DetourFinalizer));
                foreach (ConstructorInfo constructor in typeof(NativeDetour).GetConstructors().Where(c =>
                             c.GetParameters()[0].ParameterType == typeof(MethodBase) || c.GetParameters()[0].ParameterType == typeof(Delegate)))
                    InstallHook(harmony, constructor, nameof(NativeConstructorPrefix), nameof(DetourFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(Detour), "_OnCompileMethod"), nameof(NativeRecompilePrefix), nameof(HarmonyFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(ILHook).GetNestedType("Context", BindingFlags.NonPublic)!, "InvokeManipulator"), nameof(ForeignCodePrefix), nameof(ForeignCodeFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(HarmonyManipulator), "Process"), nameof(ForeignCodePrefix), nameof(ForeignCodeFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(PatchManager), "GetMethodPatcher"), nameof(ForeignCodePrefix), nameof(ForeignCodeFinalizer));

                AddCallback(typeof(Hook), "OnDetour", new Func<Hook, MethodBase, MethodBase, object, bool>(ObserveHook));
                AddCallback(typeof(ILHook), "OnDetour", new Func<ILHook, MethodBase, MonoMod.Cil.ILContext.Manipulator, bool>(ObserveILHook));
                AddCallback(typeof(Detour), "OnDetour", new Func<Detour, MethodBase, MethodBase, bool>(ObserveDetour));
                AddCallback(typeof(NativeDetour), "OnDetour", new Func<NativeDetour, MethodBase, IntPtr, IntPtr, bool>(ObserveNative));
                AddCallback(typeof(Hook), "OnUndo", new Func<Hook, bool>(_ => true));
                AddCallback(typeof(ILHook), "OnUndo", new Func<ILHook, bool>(_ => true));
                AddCallback(typeof(Detour), "OnUndo", new Func<Detour, bool>(_ => true));
                AddCallback(typeof(NativeDetour), "OnUndo", new Func<NativeDetour, bool>(_ => true));
                AddCallback(typeof(Hook), "OnGenerateTrampoline", new Func<Hook, MethodBase, MethodBase>((_, _) => null!));
                AddCallback(typeof(Detour), "OnGenerateTrampoline", new Func<Detour, MethodBase, MethodBase>((_, _) => null!));
                AddCallback(typeof(NativeDetour), "OnGenerateTrampoline", new Func<NativeDetour, MethodBase, MethodBase>((_, _) => null!));
                // Intercept dispatch instead of replacing subscribers: ordinary +=
                // and -= retain their normal delegate-identity semantics.
                InstallHook(harmony, RequiredMethod(typeof(MonoMod.Utils.Extensions), "InvokeWhileTrue"), nameof(CallbackTruePrefix), nameof(HarmonyFinalizer));
                InstallHook(harmony, RequiredMethod(typeof(MonoMod.Utils.Extensions), "InvokeWhileNull").MakeGenericMethod(typeof(MethodBase)), nameof(CallbackNullPrefix), nameof(HarmonyFinalizer));
                _installed = true;
                _audit.Ready();
                InitializeVerification();
                AuditPreexisting();
                RunCanary(harmony);
                if (!Verify(requireComplete: false))
                    throw new InvalidOperationException("Bootstrap guard installation verification failed.");
                trace?.Write($"INSTALL_COMPLETE patches={Hooks.Count}");
            }
            catch (Exception ex)
            {
                trace?.Write("INSTALL_FAILED " + ex);
                Invalidate("Guard installation failed: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
            finally { _installationTrace = null; _installing = false; }
        }
    }

    internal static bool Verify(bool requireComplete = true)
    {
        return VerifyCheckpoint(requireComplete);
    }

    private static void ProtectBackendInfrastructure()
    {
        foreach (Type type in new[] { typeof(Hook), typeof(ILHook), typeof(Detour), typeof(NativeDetour), typeof(ManagedMethodPatcher), typeof(NativeDetourMethodPatcher), typeof(PatchManager) })
            ProtectBackendType(type);
        foreach (string name in new[] { "HarmonyLib.PatchFunctions", "HarmonyLib.Public.Patching.HarmonyManipulator", "HarmonyLib.Internal.RuntimeFixes.StackTraceFixes" })
            ProtectBackendType(typeof(HostHarmony).Assembly.GetType(name, throwOnError: true)!);
    }

    private static void ProtectBackendType(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            ProtectedMethodCatalog.RegisterInfrastructureTarget(method);
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            ProtectBackendType(nested);
    }

    private static void InstallHook(HostHarmony harmony, MethodBase target, string prefixName, string finalizerName)
    {
        MethodInfo prefix = RequiredMethod(typeof(RuntimeGuard), prefixName);
        MethodInfo finalizer = RequiredMethod(typeof(RuntimeGuard), finalizerName);
        ProtectedMethodCatalog.RegisterInfrastructureTarget(target);
        ProtectedMethodCatalog.RegisterTrustedPatch(target, prefix, HarmonyPatchType.Prefix);
        ProtectedMethodCatalog.RegisterTrustedPatch(target, finalizer, HarmonyPatchType.Finalizer);
        var operation = new Operation(target, trusted: true, external: false, harmony: true);
        int ordinal = Hooks.Count + 1;
        _installationTrace?.Begin(ordinal, target, prefixName, finalizerName);
        Push(operation);
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(prefix) { priority = int.MaxValue, before = new[] { "MelonLoader" } },
                finalizer: new HarmonyMethod(finalizer) { priority = int.MinValue });
            Hooks.Add((target, prefix, finalizer));
            _installationTrace?.Write($"END patch={ordinal}");
        }
        catch (Exception exception)
        {
            _installationTrace?.Write($"FAILED patch={ordinal} {exception}");
            throw;
        }
        finally { Pop(operation); }
    }

    private static void AddCallback(Type type, string name, Delegate guard)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingFieldException(type.FullName, name);
        var previous = (Delegate?)field.GetValue(null);
        field.SetValue(null, Delegate.Combine(guard, previous));
        Callbacks.Add((field, guard, previous?.GetInvocationList() ?? Array.Empty<Delegate>()));
    }

    private static void RecordRequiredStockHooks()
    {
        foreach (MethodInfo target in typeof(PatchProcessor).GetMethods().Where(m => m.Name is "Patch" or "Unpatch"))
            RequiredStockHooks.Add((target, RequiredMethod(typeof(PatchShield), "PatchMethod_PatchProcessor_" + target.Name)));
        Type functions = typeof(HostHarmony).Assembly.GetType("HarmonyLib.PatchFunctions", true)!;
        RequiredStockHooks.Add((RequiredMethod(functions, "ReversePatch"), RequiredMethod(typeof(PatchShield), "PatchMethod_PatchFunctions_ReversePatch")));
    }

    private static bool IsStockShieldCallback(Delegate callback)
    {
        Type? declaringType = callback.Method.DeclaringType;
        return callback.Method.Module.Assembly == typeof(PatchShield).Assembly && declaringType?.DeclaringType == typeof(PatchShield) &&
            callback.Method.Name.StartsWith("<Install>b__", StringComparison.Ordinal);
    }

    private static bool PatchPrefix(object __instance, ref Operation? __state)
    {
        MethodBase target = Read<MethodBase>(__instance, "original");
        var patches = new List<(MethodInfo, HarmonyPatchType)>();
        AddPatch(patches, ReadOptional<HarmonyMethod>(__instance, "prefix"), HarmonyPatchType.Prefix);
        AddPatch(patches, ReadOptional<HarmonyMethod>(__instance, "postfix"), HarmonyPatchType.Postfix);
        AddPatch(patches, ReadOptional<HarmonyMethod>(__instance, "transpiler"), HarmonyPatchType.Transpiler);
        AddPatch(patches, ReadOptional<HarmonyMethod>(__instance, "finalizer"), HarmonyPatchType.Finalizer);
        AddPatch(patches, ReadOptional<HarmonyMethod>(__instance, "ilmanipulator"), HarmonyPatchType.ILManipulator);
        bool allowed = BeginHarmony(target, patches, ref __state);
        if (__state != null) __state.Installation = true;
        return allowed;
    }

    private static bool ClassPatchPrefix(object __0, ref Operation? __state)
    {
        MethodBase target = Read<MethodBase>(__0, "original");
        var patches = new List<(MethodInfo, HarmonyPatchType)>();
        foreach (var entry in new[] { ("prefixes", HarmonyPatchType.Prefix), ("postfixes", HarmonyPatchType.Postfix),
                     ("transpilers", HarmonyPatchType.Transpiler), ("finalizers", HarmonyPatchType.Finalizer), ("ilmanipulators", HarmonyPatchType.ILManipulator) })
            foreach (HarmonyMethod patch in Read<IEnumerable>(__0, entry.Item1))
                AddPatch(patches, patch, entry.Item2);
        bool allowed = BeginHarmony(target, patches, ref __state);
        if (__state != null) { __state.Installation = true; __state.PatchJob = __0; }
        return allowed;
    }

    private static bool BeginHarmony(MethodBase target, List<(MethodInfo Method, HarmonyPatchType Kind)> patches, ref Operation? state)
    {
        ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
        bool trusted = patches.Count > 0 && patches.All(p => ProtectedMethodCatalog.IsTrustedPatch(target, p.Method, p.Kind));
        if (trusted && _foreignCallbackDepth != 0)
            return Block(target, "Required patch installation from a foreign callback");
        Patches? existing = HostHarmony.GetPatchInfo(target);
        if (trusted && existing != null && patches.Any(p => Enumerate(existing).Any(e => e.Kind == p.Kind && e.Patch.PatchMethod == p.Method)))
            return Block(target, "Duplicate required patch installation");
        if (!Authorize(target, scope, trusted, "Harmony patch"))
            return false;
        if (trusted && existing != null && Enumerate(existing).Any(p => !ProtectedMethodCatalog.IsTrustedPatch(target, p.Patch.PatchMethod, p.Kind)))
            trusted = false;
        lock (Sync)
            if (trusted && ObservedDetours.Any(d => d.Value == target && !OwnedDetours.ContainsKey(d.Key) && IsApplied(d.Key)))
                trusted = false;
        state = new Operation(target, trusted, scope == ProtectionScope.External && !trusted, harmony: true)
        {
            BeforePatches = SnapshotPatches(target),
            Origin = string.Join(", ", patches.Select(p => p.Kind + " " + Describe(p.Method) + " [" + p.Method.Module.Assembly.GetName().Name + "]"))
        };
        if (state.External) _audit.BeginMutation();
        Push(state);
        return true;
    }

    private static bool UnpatchPrefix(PatchProcessor __instance, object[] __args, ref PatchProcessor __result, ref Operation? __state)
    {
        __result = __instance;
        MethodBase target = Read<MethodBase>(__instance, "original");
        Patches? patches = HostHarmony.GetPatchInfo(target);
        var selectedPatches = new List<MethodInfo>();
        if (patches != null)
        {
            foreach (var item in Enumerate(patches))
            {
                bool selected = __args[0] is MethodInfo method ? item.Patch.PatchMethod == method :
                    (__args[0] is HarmonyPatchType kind && (kind == HarmonyPatchType.All || kind == item.Kind) &&
                     (__args[1] is not string owner || owner == "*" || owner == item.Patch.owner));
                if (selected && ProtectedMethodCatalog.IsTrustedPatch(target, item.Patch.PatchMethod, item.Kind))
                    return Block(target, "Removal of required patch " + Describe(item.Patch.PatchMethod));
                if (selected) selectedPatches.Add(item.Patch.PatchMethod);
            }
        }
        ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
        // HarmonyX rebuilds the wrapper even when the selector matches nothing.
        // Preserve the fluent return value while avoiding that unintended mutation.
        if (selectedPatches.Count == 0) return false;
        bool trusted = false; // Backend rebuilds do not require public Harmony unpatch calls.
        if (!Authorize(target, scope, trusted, "Harmony unpatch"))
            return false;
        __state = new Operation(target, trusted, scope == ProtectionScope.External && !trusted, harmony: true)
        {
            BeforePatches = SnapshotPatches(target),
            Origin = "unpatch " + string.Join(", ", selectedPatches.Select(p => Describe(p) + " [" + p.Module.Assembly.GetName().Name + "]"))
        };
        if (__state.External) _audit.BeginMutation();
        Push(__state);
        return true;
    }

    private static bool ReversePrefix(HarmonyMethod __0, MethodBase __1, ref Operation? __state)
    {
        MethodInfo destination = __0.method;
        // Reverse patch copies the source and modifies the stand-in. Neither end may
        // tunnel around core protection, and there are no required reverse patches.
        if (!Authorize(__1, ProtectedMethodCatalog.Classify(__1), false, "Reverse patch source") ||
            !Authorize(destination, ProtectedMethodCatalog.Classify(destination), false, "Reverse patch destination"))
            return false;
        bool external = ProtectedMethodCatalog.Classify(__1) == ProtectionScope.External || ProtectedMethodCatalog.Classify(destination) == ProtectionScope.External;
        __state = new Operation(destination, false, external, harmony: true) { Origin = "reverse from " + Describe(__1), Reverse = true };
        if (__state.External) _audit.BeginMutation();
        Push(__state);
        return true;
    }

    private static Exception? HarmonyFinalizer(Exception? __exception, Operation? __state, bool __runOriginal)
    {
        if (__state != null)
        {
            Pop(__state);
            if (__state.External)
            {
                // Once an allowed critical operation began, an exception can occur
                // after PatchInfo or native state changed. Conservatively retain risk.
                bool changed = __state.Reverse ? __runOriginal : !__state.BeforePatches.SequenceEqual(SnapshotPatches(__state.Target));
                _audit.EndMutation(__exception != null ? GuardAuditFlags.ExternalHarmonyModification | GuardAuditFlags.CoverageIncomplete :
                    changed ? GuardAuditFlags.ExternalHarmonyModification : GuardAuditFlags.None,
                    "Allowed critical Harmony operation" + (__exception == null ? ": " : " (failed or partial): ") + Describe(__state.Target) + " <- " + __state.Origin);
            }
            if (!__runOriginal && !__state.Trusted)
                Block(__state.Target, "Harmony operation vetoed by another guard");
            QueueProtectedTarget(__state.Target);
            if (__runOriginal && __exception == null && !__state.Trusted && !__state.Reverse &&
                (!__state.Installation || __state.PatchJob != null))
                PatchPerformanceDiagnostics.CompleteHarmonyOperation(__state.Target, __state.BeforePatches,
                    __state.Installation && PatchPerformanceDiagnostics.IsClassInstallationCompleted(__state.PatchJob!));
        }
        return __exception;
    }

    private static Exception? HarmonyInstallFinalizer(Exception? __exception, Operation? __state, bool __runOriginal, MethodInfo? __result)
    {
        HarmonyFinalizer(__exception, __state, __runOriginal);
        if (__state != null && !__state.Trusted && __runOriginal && __exception == null && __result != null)
            PatchPerformanceDiagnostics.CompleteHarmonyOperation(__state.Target, __state.BeforePatches, installationCompleted: true);
        return __exception;
    }

    private static bool DetourPrefix(object __instance, MethodBase __originalMethod, object[] __args, ref Operation? __state)
    {
        MethodBase? target = GetDetourTarget(__instance);
        if (__instance is NativeDetour && __originalMethod.Name == "ChangeSource" && __args.Length != 0 &&
            __args[0] is IntPtr source && ProtectedMethodCatalog.TryGetNativeTarget(source, out MethodBase mapped))
            target = SelectNativeTarget(target, mapped);
        if (target == null && __instance is NativeDetour && _operations?.Count > 0 &&
            _operations.Peek() is { Trusted: true, Detour: Detour parent } operation && _foreignCallbackDepth == 0)
            target = parent.Method;
        if (target == null)
        {
            __state = new Operation(__originalMethod, false, false, false) { Detour = __instance, UnknownNative = true };
            Push(__state);
            return true; // Retain successful raw sources for deferred catalog resolution.
        }
        bool removing = __originalMethod.Name is "Undo" or "Free" or "Dispose";
        bool trusted = HasTrustedLease(target, __instance) || HasHarmonyBackendLease(target, __instance);
        lock (Sync)
        {
            if (OwnedDetours.ContainsKey(__instance) && !trusted)
                return Block(target, "Modification of required detour (" + __originalMethod.Name + ")");
            if (removing && !OwnedDetours.ContainsKey(__instance) && !IsApplied(__instance))
                return true; // A rejected construction must still release its resources.
        }
        ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
        if (!Authorize(target, scope, trusted, "MonoMod " + __originalMethod.Name))
            return false;
        __state = new Operation(target, trusted, scope == ProtectionScope.External && !trusted, harmony: false);
        if (__state.External) _audit.BeginMutation();
        __state.Detour = __instance;
        __state.Removing = removing;
        __state.BeforeDetour = SnapshotDetour(__instance);
        __state.Origin = __originalMethod.Name + " " + DescribeDetour(__instance);
        if (_operations?.Count > 0) __state.RestrictedDetour = _operations.Peek().RestrictedDetour;
        Push(__state);
        return true;
    }

    private static bool NativeConstructorPrefix(object __instance, object[] __args, ref Operation? __state)
    {
        // The MethodBase/MethodBase overload chains to a raw-pointer constructor
        // and passes a null Method. Preserve its source before that information is
        // discarded, including before IL2CPP is ready for native catalog mapping.
        MethodBase? target = __args[0] as MethodBase ?? (__args[0] as Delegate)?.Method;
        // The (MethodBase, IntPtr from, IntPtr to, ...) constructors allow the
        // supplied metadata identity to disagree with the actual source address.
        if (__args.Length >= 3 && __args[1] is IntPtr from && __args[2] is IntPtr)
            target = ResolveNativeTarget(__instance, target, from);
        if (target == null)
            return true;
        bool trusted = HasTrustedLease(target);
        ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
        if (!Authorize(target, scope, trusted, "Native detour construction"))
            return false;
        __state = new Operation(target, trusted, scope == ProtectionScope.External && !trusted, harmony: false) { Detour = __instance };
        __state.BeforeDetour = SnapshotDetour(__instance);
        __state.Origin = "native constructor " + string.Join(", ", __args.OfType<MethodBase>().Select(Describe));
        if (__state.External) _audit.BeginMutation();
        lock (Sync)
        {
            ObservedDetours[__instance] = target;
            if (trusted) OwnedDetours[__instance] = target;
        }
        Push(__state);
        return true;
    }

    private static bool NativeRecompilePrefix(MethodBase __0, IntPtr __1, ref Operation? __state)
    {
        if (__0 == null || _foreignCallbackDepth != 0) return true;
        ProtectedMethodCatalog.RegisterCompiledNativeTarget(__0, __1);
        object map = GuardRuntimeAdapters.DetourMap.GetValue(null)!;
        Detour[] entries;
        lock (map)
        {
            if (((IDictionary)map)[__0] is not IList chain) return true;
            lock (chain) entries = chain.Cast<Detour>().ToArray();
        }
        foreach (Detour entry in entries.Reverse())
        {
            if (GuardRuntimeAdapters.TopDetour.GetValue(entry) is not NativeDetour native)
                continue;
            lock (Sync)
            {
                if (!OwnedDetours.ContainsKey(entry)) return true;
                OwnedDetours[native] = __0;
                ObservedDetours[native] = __0;
            }
            __state = new Operation(__0, true, false, false) { RestrictedDetour = native };
            Push(__state);
            return true;
        }
        return true;
    }

    private static void ForeignCodePrefix(ref bool __state)
    {
        _foreignCallbackDepth++;
        __state = true;
    }

    private static Exception? ForeignCodeFinalizer(Exception? __exception, bool __state)
    {
        if (__state) _foreignCallbackDepth--;
        return __exception;
    }

    private static Exception? DetourFinalizer(Exception? __exception, Operation? __state)
    {
        if (__state != null)
        {
            Pop(__state);
            if (__state.Detour != null)
            {
                bool applied = IsApplied(__state.Detour);
                if (__state.UnknownNative)
                {
                    if (__state.Detour is NativeDetour raw)
                        DeferredNative.Record(raw.Data.Method, applied);
                    if (__exception != null) ReportCoverageGap("Indeterminate unmapped native detour operation.");
                    return __exception;
                }
                lock (Sync)
                {
                    if (applied)
                    {
                        ObservedDetours[__state.Detour] = __state.Target;
                        if (__state.Trusted)
                            OwnedDetours[__state.Detour] = __state.Target;
                        if (__state.Trusted && __state.Detour is Detour managed &&
                            GuardRuntimeAdapters.TopDetour.GetValue(managed) is NativeDetour top)
                        {
                            OwnedDetours[top] = __state.Target;
                            ObservedDetours[top] = __state.Target;
                        }
                    }
                    else if (__state.Removing)
                    {
                        ObservedDetours.Remove(__state.Detour);
                        OwnedDetours.Remove(__state.Detour);
                    }
                }
                if (__state.External)
                    _audit.EndMutation(__exception != null ? GuardAuditFlags.ExternalDetourModification | GuardAuditFlags.CoverageIncomplete :
                        __state.BeforeDetour != SnapshotDetour(__state.Detour) ? GuardAuditFlags.ExternalDetourModification : GuardAuditFlags.None,
                        "Allowed critical detour operation" + (__exception == null ? ": " : " (failed or partial): ") + Describe(__state.Target) + " <- " + __state.Origin);
                QueueProtectedTarget(__state.Target);
            }
        }
        return __exception;
    }

    private static bool ObserveHook(Hook detour, MethodBase original, MethodBase patch, object target) => ObserveManaged(detour, original);
    private static bool ObserveILHook(ILHook detour, MethodBase original, MonoMod.Cil.ILContext.Manipulator manipulator) => ObserveManaged(detour, original);
    private static bool ObserveDetour(Detour detour, MethodBase original, MethodBase patch)
    {
        if (!ObserveManaged(detour, original)) return false;
        bool owned;
        lock (Sync) owned = OwnedDetours.ContainsKey(detour);
        if (owned) ProtectBackendReplacement(detour);
        return true;
    }
    private static bool ObserveManaged(object detour, MethodBase original)
    {
        bool trusted = HasTrustedLease(original, detour);
        if (!Authorize(original, ProtectedMethodCatalog.Classify(original), trusted, "MonoMod apply"))
            return false;
        lock (Sync)
        {
            ObservedDetours[detour] = original;
            if (trusted)
                OwnedDetours[detour] = original;
        }
        return true;
    }

    private static bool ObserveNative(NativeDetour detour, MethodBase? method, IntPtr from, IntPtr to)
    {
        MethodBase? target = ResolveNativeTarget(detour, method, from);
        return target == null || ObserveManaged(detour, target);
    }

    private static bool Authorize(MethodBase target, ProtectionScope scope, bool trusted, string operation)
    {
        if (scope != ProtectionScope.None && IsShielded(target))
            return Block(target, operation + " (upstream PatchShield)");
        if (scope == ProtectionScope.None || trusted)
            return true;
        if (scope == ProtectionScope.External && Snapshot.AllowExternalModifications)
            return true;
        return Block(target, operation);
    }

    private static bool IsShielded(MethodBase method)
    {
        // Dynamic methods cannot carry these attributes; their runtime wrappers
        // can reject CustomAttributeData queries. Their explicit catalog entry
        // still enforces infrastructure protection.
        if (ProtectedMethodCatalog.IsDynamicNativeTarget(method)) return false;
        static bool Shield(IEnumerable<CustomAttributeData> attributes) =>
            attributes.Any(a => typeof(PatchShield).IsAssignableFrom(a.AttributeType));
        return Shield(method.GetCustomAttributesData()) || (method.DeclaringType != null &&
            (Shield(method.DeclaringType.GetCustomAttributesData()) || Shield(method.DeclaringType.Assembly.GetCustomAttributesData())));
    }

    private static bool Block(MethodBase target, string reason)
    {
        if (target != _canaryTarget)
            _audit.Record(GuardAuditFlags.BlockedAttemptObserved, reason + " blocked: " + Describe(target));
        return false;
    }

    private static void RunCanary(HostHarmony harmony)
    {
        // The control and protected target share an ordinary generated assembly,
        // so loader-wide failure to patch cannot masquerade as guard success.
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Anomaly.GuardCanary"), AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule("Canary").DefineType("GuardCanary", TypeAttributes.Public);
        foreach (string name in new[] { "Control", "Protected" })
        {
            MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            method.SetImplementationFlags(MethodImplAttributes.NoInlining);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
        Type fixture = type.CreateType()!;
        MethodInfo control = fixture.GetMethod("Control")!;
        MethodInfo sentinel = fixture.GetMethod("Protected")!;
        MethodInfo prefix = RequiredMethod(typeof(CanaryPatch), "Prefix");
        ProtectedMethodCatalog.RegisterInfrastructureTarget(sentinel);
        _canaryTarget = sentinel;
        try
        {
            harmony.Patch(control, prefix: new HarmonyMethod(prefix));
            if ((int)control.Invoke(null, null)! != 2)
                throw new InvalidOperationException("Unprotected guard canary could not be patched.");
            harmony.Unpatch(control, prefix);
            if ((int)control.Invoke(null, null)! != 1)
                throw new InvalidOperationException("Unprotected guard canary could not be restored.");
            harmony.Patch(sentinel, prefix: new HarmonyMethod(prefix));
            harmony.CreateClassProcessor(typeof(CanaryPatch)).Patch();
            using var hook = new Hook(sentinel, (Func<int>)(() => 3));
            if (hook.IsApplied || (int)sentinel.Invoke(null, null)! != 1 ||
                (HostHarmony.GetPatchInfo(sentinel)?.Prefixes.Count ?? 0) != 0)
                throw new InvalidOperationException("Protected guard canary accepted a modification.");
        }
        finally { _canaryTarget = null; }
    }

    [HarmonyPatch]
    private static class CanaryPatch
    {
        [HarmonyTargetMethod] private static MethodBase TargetMethod() => _canaryTarget!;
        [HarmonyPrefix] private static bool Prefix(ref int __result) { __result = 2; return false; }
    }

    private static void AuditPreexisting()
    {
        using (GuardTimingDiagnostics.Begin(GuardTimingPhase.AuditHarmony))
        foreach (MethodBase method in HostHarmony.GetAllPatchedMethods().ToArray())
        {
            GuardTimingDiagnostics.Count(GuardTimingCount.PatchedMethods);
            ProtectionScope scope = ProtectedMethodCatalog.Classify(method);
            if (scope == ProtectionScope.None)
                continue;
            Patches? info = HostHarmony.GetPatchInfo(method);
            if (info != null)
                AuditPatches(method, info);
        }
        // These private maps are the only complete enumerable managed-detour state
        // in the supported MonoMod version. A missing map invalidates the adapter.
        using (GuardTimingDiagnostics.Begin(GuardTimingPhase.AuditManagedDetours))
            AuditDetourMap(ilHook: false);
        using (GuardTimingDiagnostics.Begin(GuardTimingPhase.AuditIlHooks))
            AuditDetourMap(ilHook: true);
    }

    private static void AuditPatches(MethodBase target, Patches info)
    {
        ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
        foreach (var entry in Enumerate(info))
        {
            if (ProtectedMethodCatalog.IsTrustedPatch(target, entry.Patch.PatchMethod, entry.Kind))
                continue;
            // PatchShield's exact known prefix is legitimate infrastructure on our
            // interception targets; catalog owns its explicit allowlist.
            bool fatal = scope is ProtectionScope.Core or ProtectionScope.Infrastructure;
            _audit.Record(GuardAuditFlags.ExternalHarmonyModification,
                "Existing critical patch: " + Describe(target) + " <- " + Describe(entry.Patch.PatchMethod), fatal);
        }
    }

    private static void AuditDetourMap(bool ilHook)
    {
        object map = (ilHook ? GuardRuntimeAdapters.IlHookMap : GuardRuntimeAdapters.DetourMap).GetValue(null)!;
        var snapshot = new List<object>();
        lock (map)
        {
            foreach (DictionaryEntry item in (IDictionary)map)
            {
                if (GuardTimingDiagnostics.Enabled)
                    GuardTimingDiagnostics.Count(ilHook ? GuardTimingCount.IlHookMapEntries : GuardTimingCount.ManagedMapEntries);
                object value = item.Value!;
                object chain = ilHook ? GuardRuntimeAdapters.ContextChain.GetValue(value)! : value;
                lock (chain)
                    foreach (object detour in (IEnumerable)chain)
                        snapshot.Add(detour);
            }
        }
        // Never take the catalog/guard locks while holding MonoMod's map or chain
        // locks: patch callbacks acquire those in the opposite order.
        foreach (object detour in snapshot)
        {
            if (GuardTimingDiagnostics.Enabled)
                GuardTimingDiagnostics.Count(ilHook ? GuardTimingCount.IlHooks : GuardTimingCount.ManagedDetours);
            MethodBase? target = GetDetourTarget(detour);
            if (target == null || !GuardRuntimeAdapters.IsChainItemApplied(detour) || ProtectedMethodCatalog.Classify(target) == ProtectionScope.None)
                continue;
            lock (Sync)
                if (OwnedDetours.ContainsKey(detour))
                    continue;
            if (IsSupportedRuntimeDetour(detour, target))
            {
                lock (Sync) OwnedDetours[detour] = target;
                ProtectBackendReplacement(detour);
                continue;
            }
            // Harmony's preexisting backend is approved only when every patch on
            // its exact target is trusted and its actual backend object matches.
            Patches? patches = HostHarmony.GetPatchInfo(target);
            if (patches != null && Enumerate(patches).Any() && Enumerate(patches).All(p => ProtectedMethodCatalog.IsTrustedPatch(target, p.Patch.PatchMethod, p.Kind)) &&
                IsHarmonyBackend(detour, target))
            {
                lock (Sync) OwnedDetours[detour] = target;
                ProtectBackendReplacement(detour);
                continue;
            }
            lock (Sync) ObservedDetours[detour] = target;
            ProtectionScope scope = ProtectedMethodCatalog.Classify(target);
            _audit.Record(GuardAuditFlags.ExternalDetourModification, "Existing critical detour: " + Describe(target),
                fatal: scope is ProtectionScope.Core or ProtectionScope.Infrastructure);
        }
    }

    private static bool IsHarmonyBackend(object detour, MethodBase target)
    {
        MethodPatcher patcher = PatchManager.GetMethodPatcher(target);
        if (patcher.GetType() != typeof(ManagedMethodPatcher))
            return false;
        var hook = GuardRuntimeAdapters.HarmonyIlHook.GetValue(patcher) as ILHook;
        if (hook == null)
            return false;
        if (ReferenceEquals(detour, hook))
            return true;
        object map = GuardRuntimeAdapters.IlHookMap.GetValue(null)!;
        lock (map)
        {
            var dictionary = (IDictionary)map;
            object? context = dictionary[target];
            if (context == null || !ReferenceEquals(GuardRuntimeAdapters.ContextDetour.GetValue(context), detour))
                return false;
            var chain = (IList)GuardRuntimeAdapters.ContextChain.GetValue(context)!;
            lock (chain)
                return chain.Count == 1 && ReferenceEquals(chain[0], hook);
        }
    }

    private static bool IsSupportedRuntimeDetour(object detour, MethodBase target)
    {
        // HarmonyX installs this exact chain-maintenance shim before plugins run.
        // Its actual source/replacement identities, not an owner string, identify it.
        return detour is Detour managed && target == GuardRuntimeAdapters.RuntimeChainRefresh &&
            managed.Target == GuardRuntimeAdapters.RuntimeChainRefreshReplacement;
    }

    private static MethodBase? GetDetourTarget(object detour)
    {
        if (detour is NativeDetour native)
            return ResolveNativeTarget(native, native.Method, native.Data.Method);
        lock (Sync)
            if (ObservedDetours.TryGetValue(detour, out MethodBase? observed))
                return observed;
        return detour switch
        {
            Hook hook => hook.Method,
            Detour managed => managed.Method,
            ILHook hook => hook.Method,
            _ => null
        };
    }

    private static MethodBase? ResolveNativeTarget(object detour, MethodBase? declared, IntPtr source)
    {
        // NativeDetour.Method is caller supplied and remains unchanged after
        // ChangeSource. Never let it hide a stronger cataloged source identity.
        ProtectedMethodCatalog.TryGetNativeTarget(source, out MethodBase mapped);
        MethodBase? observed;
        lock (Sync) ObservedDetours.TryGetValue(detour, out observed);
        return SelectNativeTarget(SelectNativeTarget(mapped, declared), observed);
    }

    private static MethodBase? SelectNativeTarget(MethodBase? first, MethodBase? second)
    {
        if (first == null) return second;
        if (second == null || first == second) return first;
        static int Strength(MethodBase method)
        {
            ProtectionScope scope = ProtectedMethodCatalog.Classify(method);
            if (scope == ProtectionScope.None) return 0;
            if (IsShielded(method)) return 3;
            return scope == ProtectionScope.External ? 1 : 2;
        }
        return Strength(second) > Strength(first) ? second : first;
    }

    private static bool IsApplied(object detour) => detour switch
    {
        Hook hook => hook.IsValid && hook.IsApplied,
        ILHook hook => hook.IsValid && hook.IsApplied,
        Detour hook => hook.IsValid && hook.IsApplied,
        NativeDetour hook => hook.IsValid && hook.IsApplied,
        _ => false
    };

    private static string SnapshotDetour(object detour)
    {
        if (!IsApplied(detour)) return "inactive";
        if (detour is Hook hook) return SnapshotDetour(hook.Detour);
        if (detour is NativeDetour native)
            return native.IsValid + ":" + native.IsApplied + ":" + native.Data.Method + ":" + native.Data.Target;
        if (detour is ISortableDetour sortable)
            return sortable.IsValid + ":" + sortable.IsApplied + ":" + sortable.Priority + ":" + sortable.ID + ":" +
                string.Join(";", sortable.Before) + ":" + string.Join(";", sortable.After);
        return "unknown";
    }

    private static void ProtectBackendReplacement(object detour)
    {
        if (detour is not Detour managed || ProtectedMethodCatalog.Classify(managed.Method) == ProtectionScope.None)
            return;
        foreach (MethodBase replacement in new[] { managed.Target, managed.TargetReal }.Distinct())
        {
            lock (Sync)
                if (!ProtectedBackendReplacements.Add(replacement)) continue;
            // OnDetour publishes this method to ordinary subscribers. Protect its
            // identity and entry address before the next callback can retain/use it.
            ProtectedMethodCatalog.RegisterInfrastructureTarget(replacement);
            try
            {
                IntPtr address = replacement.GetNativeStart();
                if (address == IntPtr.Zero) throw new InvalidOperationException("Backend replacement has no native entry address.");
                ProtectedMethodCatalog.RegisterCompiledNativeTarget(replacement, address);
            }
            catch (Exception ex)
            {
                Invalidate("Required generated backend could not be protected: " + Describe(replacement) + ": " + ex.Message);
                throw;
            }
        }
    }

    private static string DescribeDetour(object detour)
    {
        MethodBase? replacement = detour switch
        {
            Hook hook => hook.Target,
            ILHook hook => hook.Manipulator.Method,
            Detour managed => managed.Target,
            _ => null
        };
        return replacement != null ? Describe(replacement) + " [" + replacement.Module.Assembly.GetName().Name + "]" : detour.GetType().FullName ?? "unknown";
    }

    private static IEnumerable<(Patch Patch, HarmonyPatchType Kind)> Enumerate(Patches patches)
    {
        foreach (Patch patch in patches.Prefixes) yield return (patch, HarmonyPatchType.Prefix);
        foreach (Patch patch in patches.Postfixes) yield return (patch, HarmonyPatchType.Postfix);
        foreach (Patch patch in patches.Transpilers) yield return (patch, HarmonyPatchType.Transpiler);
        foreach (Patch patch in patches.Finalizers) yield return (patch, HarmonyPatchType.Finalizer);
        foreach (Patch patch in patches.ILManipulators) yield return (patch, HarmonyPatchType.ILManipulator);
    }

    private static (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] SnapshotPatches(MethodBase target)
    {
        Patches? patches = HostHarmony.GetPatchInfo(target);
        return patches == null ? Array.Empty<(MethodInfo, HarmonyPatchType, string, int)>() :
            Enumerate(patches).Select(p => (p.Patch.PatchMethod, p.Kind, p.Patch.owner, p.Patch.index)).ToArray();
    }

    private static void AddPatch(List<(MethodInfo, HarmonyPatchType)> patches, HarmonyMethod? method, HarmonyPatchType kind)
    {
        if (method?.method != null)
            patches.Add((method.method, kind));
    }

    private static T Read<T>(object instance, string field) where T : class => ReadOptional<T>(instance, field)
        ?? throw new MissingFieldException(instance.GetType().FullName, field);
    private static T? ReadOptional<T>(object instance, string field) where T : class =>
        GuardRuntimeAdapters.ReadProcessorField(instance, field) as T;

    private static MethodInfo RequiredMethod(Type type, string name, Type[]? arguments = null) =>
        (arguments == null ? type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Single(m => m.Name == name) :
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, arguments, null))
        ?? throw new MissingMethodException(type.FullName, name);

    private static string Describe(MethodBase method) => (method.DeclaringType?.FullName ?? "<dynamic>") + "." + method.Name;
    private static void Push(Operation operation)
    {
        if (ProtectedMethodCatalog.Classify(operation.Target) != ProtectionScope.None)
        {
            operation.VerificationTracked = true;
            Interlocked.Increment(ref _verificationOperations);
            Interlocked.Increment(ref _operationRevision);
            EnqueueTargetAudit(operation.Target);
        }
        (_operations ??= new Stack<Operation>()).Push(operation);
    }
    private static void Pop(Operation operation)
    {
        if (_operations?.Count > 0 && ReferenceEquals(_operations.Peek(), operation))
            _operations.Pop();
        else
            Invalidate("Guard operation lease ordering changed.");
        if (operation.VerificationTracked)
        {
            EnqueueTargetAudit(operation.Target);
            Interlocked.Increment(ref _operationRevision);
            Interlocked.Decrement(ref _verificationOperations);
        }
    }
    private static bool HasTrustedLease(MethodBase target, object? detour = null) => _foreignCallbackDepth == 0 && _operations?.Count > 0 &&
        _operations.Peek().Trusted && _operations.Peek().Target == target &&
        (_operations.Peek().RestrictedDetour == null || ReferenceEquals(_operations.Peek().RestrictedDetour, detour));

    private static bool HasHarmonyBackendLease(MethodBase target, object detour) => _foreignCallbackDepth == 0 &&
        _operations?.Count > 0 && _operations.Peek().Harmony && _operations.Peek().Target == target && IsHarmonyBackend(detour, target);

    private static bool IsGuardedDispatch(MulticastDelegate? callbacks, out Delegate[] entries)
    {
        entries = callbacks?.GetInvocationList() ?? Array.Empty<Delegate>();
        Delegate? first = entries.FirstOrDefault();
        if (first == null) return false;
        if (Callbacks.Any(slot => slot.Guard.Equals(first))) return true;
        foreach (var slot in Callbacks)
            if (Equals(slot.Field.GetValue(null), callbacks))
            {
                // A public field replacement must not borrow a live backend lease
                // during the interval before the periodic invariant check.
                Invalidate("Guard callback removed/reordered during dispatch: " + slot.Field.DeclaringType?.Name + "." + slot.Field.Name);
                entries = Array.Empty<Delegate>();
                return true;
            }
        return false;
    }

    private static bool CallbackTruePrefix(MulticastDelegate? __0, object[] __1, ref bool __result)
    {
        if (!IsGuardedDispatch(__0, out Delegate[] entries))
            return true;
        __result = entries.Length != 0;
        foreach (Delegate entry in entries)
        {
            bool accepted = Callbacks.Any(slot => slot.Guard.Equals(entry)) || IsStockShieldCallback(entry)
                ? (bool)entry.DynamicInvoke(__1)!
                : Foreign(() => (bool)entry.DynamicInvoke(__1)!);
            if (!accepted) { __result = false; break; }
        }
        return false;
    }

    private static bool CallbackNullPrefix(MulticastDelegate? __0, object[] __1, ref MethodBase? __result)
    {
        if (!IsGuardedDispatch(__0, out Delegate[] entries))
            return true;
        __result = null;
        foreach (Delegate entry in entries)
        {
            __result = Callbacks.Any(slot => slot.Guard.Equals(entry)) || IsStockShieldCallback(entry)
                ? (MethodBase?)entry.DynamicInvoke(__1)
                : Foreign(() => (MethodBase?)entry.DynamicInvoke(__1));
            if (__result != null) break;
        }
        return false;
    }

    private static T Foreign<T>(Func<T> callback)
    {
        _foreignCallbackDepth++;
        try { return callback(); }
        finally { _foreignCallbackDepth--; }
    }

    private sealed class Operation
    {
        internal bool VerificationTracked;
        internal Operation(MethodBase target, bool trusted, bool external, bool harmony)
        { Target = target; Trusted = trusted; External = external; Harmony = harmony; }
        internal MethodBase Target { get; }
        internal bool Trusted { get; }
        internal bool External { get; }
        internal bool Harmony { get; }
        internal object? Detour { get; set; }
        internal bool Removing { get; set; }
        internal string BeforeDetour { get; set; } = "";
        internal bool UnknownNative { get; set; }
        internal object? RestrictedDetour { get; set; }
        internal string Origin { get; set; } = "";
        internal bool Reverse { get; set; }
        internal bool Installation { get; set; }
        internal object? PatchJob { get; set; }
        internal (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] BeforePatches { get; set; } = Array.Empty<(MethodInfo, HarmonyPatchType, string, int)>();
    }
}
