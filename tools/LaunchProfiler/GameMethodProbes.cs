using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace Anomaly.LaunchProfiler;

// Detailed runs only. A coroutine factory's duration is not the coroutine's
// lifetime: record execution slices and the separate first-to-final interval.
internal static class GameMethodProbes
{
    private static readonly HarmonyLib.Harmony Patches = new("Anomaly.LaunchProfiler.GameMethods");
    private static readonly Dictionary<nint, CoroutineObservation> Coroutines = new();
    private static long _sequence;
    private sealed class CoroutineObservation
    {
        internal long Start;
        internal long Id;
        internal int Steps;
        internal long ExecutionTicks;
        internal int LastState = int.MinValue;
        internal bool? LastActivation;
    }

    internal static void Initialize()
    {
        Patch(typeof(SimpleMenu), nameof(SimpleMenu.Awake));
        Patch(typeof(SimpleMenu), nameof(SimpleMenu.Start));
        Patch(typeof(SimpleMenu), nameof(SimpleMenu.StartLoad));
        Patch(typeof(NewMainMenu), nameof(NewMainMenu.Start));
        Patch(typeof(NewMainMenu), nameof(NewMainMenu.ChangeMenu), new[] { typeof(int) });
        var moveNext = AccessTools.Method(typeof(SimpleMenu._StartLoad_d__8), nameof(SimpleMenu._StartLoad_d__8.MoveNext));
        try
        {
            if (moveNext == null) throw new MissingMethodException("SimpleMenu.StartLoad.MoveNext");
            Patches.Patch(moveNext, prefix: Method(nameof(CoroutinePrefix)), postfix: Method(nameof(CoroutinePostfix)), finalizer: Method(nameof(CoroutineFinalizer)));
            Core.Mark("probe.installed", new { method = "SimpleMenu.StartLoad.MoveNext" });
        }
        catch (Exception exception) { Core.Mark("probe.unavailable", new { method = "SimpleMenu.StartLoad.MoveNext", error = exception.GetType().Name + ": " + exception.Message }); }
    }

    private static void Patch(Type type, string name, Type[]? arguments = null)
    {
        try
        {
            var target = AccessTools.Method(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
            Patches.Patch(target, prefix: Method(nameof(Prefix)), finalizer: Method(nameof(Finalizer)));
            Core.Mark("probe.installed", new { method = type.Name + "." + name });
        }
        catch (Exception exception) { Core.Mark("probe.unavailable", new { method = type.Name + "." + name, error = exception.GetType().Name + ": " + exception.Message }); }
    }

    private static HarmonyMethod Method(string name) => new(AccessTools.Method(typeof(GameMethodProbes), name));
    private static void Prefix(MethodBase __originalMethod, out long __state) => __state = Core.BeginSpan("game." + __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name);
    private static Exception? Finalizer(MethodBase __originalMethod, long __state, Exception? __exception)
    {
        try { Core.EndSpan("game." + __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name, __state,
            new { fault = __exception?.GetType().FullName }); }
        catch { /* Instrumentation must never replace the game's exception. */ }
        return __exception;
    }

    private static void CoroutinePrefix(SimpleMenu._StartLoad_d__8 __instance, out long __state)
    {
        __state = Stopwatch.GetTimestamp();
        try
        {
            var key = __instance.Pointer;
            if (!Coroutines.ContainsKey(key))
            {
                var item = new CoroutineObservation { Start = __state, Id = ++_sequence };
                Coroutines.Add(key, item);
                Core.Mark("game.SimpleMenu.StartLoad.lifetime.begin", new { id = item.Id, preloader = __instance.__4__this != null && __instance.__4__this.isPreloader });
            }
        }
        catch (Exception exception) { Core.Mark("probe.observation.error", new { hook = "StartLoad.prefix", error = exception.GetType().Name }); }
    }

    private static void CoroutinePostfix(SimpleMenu._StartLoad_d__8 __instance, bool __result, long __state)
    {
        var finished = Stopwatch.GetTimestamp();
        try
        {
            if (!Coroutines.TryGetValue(__instance.Pointer, out var item)) return;
            item.Steps++;
            item.ExecutionTicks += finished - __state;
            var state = __instance.__1__state;
            var operation = __instance._asyncOperation_5__2;
            bool? activation = operation == null ? null : operation.allowSceneActivation;
            if (state != item.LastState || activation != item.LastActivation || !__result)
            {
                Core.Mark("game.SimpleMenu.StartLoad.state", new
                {
                    id = item.Id,
                    state,
                    hasNext = __result,
                    yielded = __instance.__2__current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    activation,
                    progress = operation == null ? (float?)null : operation.progress,
                    done = operation == null ? (bool?)null : operation.isDone,
                });
                item.LastState = state;
                item.LastActivation = activation;
            }
            if (finished - __state >= Stopwatch.Frequency / 1000)
                Core.EndSpan("game.SimpleMenu.StartLoad.execution", __state, new { id = item.Id, step = item.Steps });
            if (!__result)
            {
                Core.EndSpan("game.SimpleMenu.StartLoad.lifetime", item.Start, new { id = item.Id, steps = item.Steps,
                    executionMilliseconds = item.ExecutionTicks * 1000d / Stopwatch.Frequency });
                Coroutines.Remove(__instance.Pointer);
            }
        }
        catch (Exception exception) { Core.Mark("probe.observation.error", new { hook = "StartLoad.postfix", error = exception.GetType().Name }); }
    }

    private static Exception? CoroutineFinalizer(SimpleMenu._StartLoad_d__8 __instance, long __state, Exception? __exception)
    {
        if (__exception == null) return null;
        try
        {
            if (Coroutines.Remove(__instance.Pointer, out var item))
            {
                item.ExecutionTicks += Stopwatch.GetTimestamp() - __state;
                Core.EndSpan("game.SimpleMenu.StartLoad.lifetime", item.Start, new { id = item.Id,
                    fault = __exception.GetType().FullName, steps = item.Steps + 1,
                    executionMilliseconds = item.ExecutionTicks * 1000d / Stopwatch.Frequency });
            }
        }
        catch { /* Keep the original exception and let the game handle it. */ }
        return __exception;
    }

    internal static void Shutdown()
    {
        foreach (var item in Coroutines.Values)
            Core.EndSpan("game.SimpleMenu.StartLoad.lifetime", item.Start, new { id = item.Id, incomplete = true, steps = item.Steps });
        Coroutines.Clear();
        Patches.UnpatchSelf();
    }
}
