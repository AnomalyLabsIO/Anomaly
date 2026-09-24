using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;

namespace Anomaly.LaunchProfiler;

// These hooks are opt-in experiments, installed only in the runner's private stage.
internal static class ExperimentHooks
{
    private static readonly HarmonyLib.Harmony Patches = new("Anomaly.LaunchProfiler.Experiments");
    private static MethodInfo? _populate;
    private static FieldInfo? _pendingController;
    private static FieldInfo? _pendingContent;
    private static readonly List<(object Controller, object Content)> PendingSettings = new();
    private static bool _replayingSettings;
    private static MethodInfo? _cleanup;
    private static bool _cleanupPending;
    private static bool _replayingCleanup;
    private static bool _menuReady;
    private static bool _readyUpdateSeen;

    internal static void Initialize()
    {
        try { Install(); }
        catch
        {
            Patches.UnpatchSelf();
            throw;
        }
    }

    private static void Install()
    {
        if (Core.Experiment == "deferred-settings")
        {
            Type installer = RequireType("Anomaly.Client.Patches.Menu.ModSettings.ModSettingsTabInstaller");
            _populate = RequireMethod(installer, "PopulateNewlyInstalled");
            _pendingController = RequireField(installer, "_pendingPopulate");
            _pendingContent = RequireField(installer, "_pendingContent");
            Patches.Patch(_populate, prefix: Hook(nameof(DeferSettings)));

            // Supports ordinary manual interaction as well as the scripted Mods-tab check.
            Type categories = _populate.GetParameters()[0].ParameterType;
            MethodInfo? select = categories.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == "SelectCategory" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(int));
            if (select == null) throw new MissingMethodException(categories.FullName, "SelectCategory(int)");
            Patches.Patch(select, prefix: Hook(nameof(BeforeCategorySelection)));
        }
        else if (Core.Experiment == "deferred-cleanup")
        {
            _cleanup = RequireMethod(RequireType("Anomaly.Client.Diagnostics.CrashReportWriter"), "CleanupOldReports");
            Patches.Patch(_cleanup, prefix: Hook(nameof(DeferCleanup)));
        }
        Core.Mark("experiment.installed", new { experiment = Core.Experiment });
    }

    private static bool DeferSettings(object __0)
    {
        if (_replayingSettings) return true;
        if (!SameController(_pendingController!.GetValue(null), __0)) return true;
        object? content = _pendingContent!.GetValue(null);
        if (content == null) return true;
        PendingSettings.RemoveAll(p => SameController(p.Controller, __0));
        PendingSettings.Add((__0, content));
        _pendingController.SetValue(null, null);
        _pendingContent.SetValue(null, null);
        Core.Mark("experiment.settings.deferred");
        return false;
    }

    private static void BeforeCategorySelection(object __instance, int __0)
    {
        object? categories = Member(__instance, "_categories");
        if (categories == null || __0 < 0 || Member(categories, "Length") is not int length || __0 >= length)
            return;
        object? category = categories?.GetType().GetProperty("Item")?.GetValue(categories, new object[] { __0 });
        object? group = category == null ? null : Member(category, "Group");
        if (group != null && string.Equals(Member(group, "name") as string, "ModsGroup", StringComparison.Ordinal))
            ReplaySettings(__instance);
    }

    internal static void BeforeSettingsOpen()
    {
        if (Core.Experiment != "deferred-settings") return;
        foreach (var item in PendingSettings.ToArray())
        {
            object? gameObject = Member(item.Controller, "gameObject");
            if (gameObject != null && Member(gameObject, "activeInHierarchy") is true)
                ReplaySettings(item.Controller);
        }
    }

    private static void ReplaySettings(object controller)
    {
        int index = PendingSettings.FindIndex(p => SameController(p.Controller, controller));
        if (index < 0 || _replayingSettings) return;
        var item = PendingSettings[index];
        PendingSettings.RemoveAt(index);
        _pendingController!.SetValue(null, item.Controller);
        _pendingContent!.SetValue(null, item.Content);
        long start = Core.BeginSpan("experiment.settings.populate");
        _replayingSettings = true;
        try { InvokeOriginal(_populate!, item.Controller); }
        finally
        {
            _replayingSettings = false;
            Core.EndSpan("experiment.settings.populate", start);
        }
    }

    private static bool DeferCleanup()
    {
        if (_replayingCleanup || _menuReady) return true;
        _cleanupPending = true;
        Core.Mark("experiment.cleanup.deferred");
        return false;
    }

    internal static void AfterMenuReady() => _menuReady = true;

    internal static void Update()
    {
        if (!_menuReady || !_cleanupPending) return;
        if (!_readyUpdateSeen) { _readyUpdateSeen = true; return; }
        ReplayCleanup();
    }

    private static void ReplayCleanup()
    {
        if (!_cleanupPending || _cleanup == null) return;
        _cleanupPending = false;
        long start = Core.BeginSpan("experiment.cleanup.run");
        _replayingCleanup = true;
        try { InvokeOriginal(_cleanup); }
        finally
        {
            _replayingCleanup = false;
            Core.EndSpan("experiment.cleanup.run", start);
        }
    }

    internal static void Shutdown()
    {
        try { ReplayCleanup(); }
        finally
        {
            PendingSettings.Clear();
            Patches.UnpatchSelf();
        }
    }

    private static HarmonyMethod Hook(string name) => new(RequireMethod(typeof(ExperimentHooks), name));
    private static Type RequireType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, throwOnError: false)).FirstOrDefault(t => t != null)
        ?? throw new TypeLoadException(name);
    private static MethodInfo RequireMethod(Type type, string name) => type.GetMethods(
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        .Single(m => m.Name == name);
    private static FieldInfo RequireField(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(type.FullName, name);
    private static object? Member(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance)
        ?? instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
    private static bool SameController(object? left, object right) => ReferenceEquals(left, right)
        || (left != null && Member(left, "Pointer") is IntPtr pointer && pointer != IntPtr.Zero
            && Member(right, "Pointer") is IntPtr other && pointer == other);
    private static void InvokeOriginal(MethodInfo method, params object[] arguments)
    {
        try { method.Invoke(null, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
