using System.Collections;
using System.Reflection;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using MonoMod.RuntimeDetour;

namespace Anomaly.Bootstrap;

internal static class GuardRuntimeAdapters
{
    internal static readonly object HarmonyLocker = Field(typeof(PatchProcessor), "locker").GetValue(null)!;
    internal static readonly FieldInfo DetourMap = Field(typeof(Detour), "_DetourMap");
    internal static readonly FieldInfo IlHookMap = Field(typeof(ILHook), "_Map");
    internal static readonly Type IlContext = typeof(ILHook).GetNestedType("Context", BindingFlags.NonPublic)!;
    internal static readonly FieldInfo ContextChain = Field(IlContext, "Chain");
    internal static readonly FieldInfo ContextMethod = Field(IlContext, "Method");
    internal static readonly FieldInfo ContextDetour = Field(IlContext, "Detour");
    internal static readonly FieldInfo TopDetour = Field(typeof(Detour), "_TopDetour");
    internal static readonly FieldInfo HarmonyIlHook = Field(typeof(ManagedMethodPatcher), "ilHook");
    internal static readonly MethodInfo IlHookApply = Method(typeof(ILHook), nameof(ILHook.Apply), Type.EmptyTypes);
    internal static readonly MethodInfo RuntimeChainRefresh = Method(IlContext, "Refresh", Type.EmptyTypes);
    internal static readonly MethodInfo RuntimeChainRefreshReplacement = Method(
        typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.Internal.RuntimeFixes.StackTraceFixes", throwOnError: true)!,
        "OnILChainRefresh", new[] { typeof(object) });
    private static readonly Type ClassPatchJob = typeof(PatchClassProcessor)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(method => method.Name == "ProcessPatchJob").GetParameters()[0].ParameterType;
    private static readonly Dictionary<(Type Type, string Name), FieldInfo?> ProcessorFields = BuildProcessorFields();

    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name);

    private static MethodInfo Method(Type type, string name, Type[] arguments) => type.GetMethod(name,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, types: arguments, modifiers: null)
        ?? throw new MissingMethodException(type.FullName, name);

    private static Dictionary<(Type Type, string Name), FieldInfo?> BuildProcessorFields()
    {
        var fields = new Dictionary<(Type, string), FieldInfo?>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string name in new[] { "original", "prefix", "postfix", "transpiler", "finalizer", "ilmanipulator" })
            fields.Add((typeof(PatchProcessor), name), typeof(PatchProcessor).GetField(name, flags));
        foreach (string name in new[] { "original", "prefixes", "postfixes", "transpilers", "finalizers", "ilmanipulators" })
            fields.Add((ClassPatchJob, name), ClassPatchJob.GetField(name, flags));
        return fields;
    }

    internal static object? ReadProcessorField(object instance, string name)
    {
        // The original fields of a derived processor still drive the base Harmony
        // implementation. No arbitrary runtime type/name can grow this cache.
        Type type = instance is PatchProcessor ? typeof(PatchProcessor) : instance.GetType();
        return ProcessorFields.TryGetValue((type, name), out FieldInfo? field) ? field?.GetValue(instance) : null;
    }

    // PatchInfo arrays are replaced by supported Harmony operations. Capture the
    // references under its writer lock, then inspect individual records outside it.
    internal static Patch[][] CapturePatches(MethodBase target)
    {
        lock (HarmonyLocker)
        {
            PatchInfo? info = PatchManager.GetPatchInfo(target);
            return info == null ? Array.Empty<Patch[]>() :
                new[] { info.prefixes, info.postfixes, info.transpilers, info.finalizers, info.ilmanipulators };
        }
    }

    internal static object? ReadChainItem(MethodBase identifiableTarget, bool ilHook, int index, out bool completed)
    {
        object map = (ilHook ? IlHookMap : DetourMap).GetValue(null)!;
        lock (map)
        {
            object? value = ((IDictionary)map)[identifiableTarget];
            if (value == null) { completed = true; return null; }
            var chain = (IList)(ilHook ? ContextChain.GetValue(value)! : value);
            lock (chain)
            {
                completed = index >= chain.Count;
                return completed ? null : chain[index];
            }
        }
    }

    // Entries came directly from the chain; IsValid would repeat Chain.IndexOf
    // for every item and turn a long chain audit into quadratic work.
    internal static bool IsChainItemApplied(object item) => item switch
    {
        Detour detour => detour.IsApplied,
        ILHook hook => hook.IsApplied,
        _ => false
    };
}
