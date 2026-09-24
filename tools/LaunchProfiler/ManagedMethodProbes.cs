using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace Anomaly.LaunchProfiler;

internal static class ManagedMethodProbes
{
    private static readonly HarmonyLib.Harmony Owner = new("anomaly.launch-profiler.managed");
    internal readonly record struct Call(long Started, string Name);

    internal static void Initialize()
    {
        var targets = new (string Type, string[] Methods)[]
        {
            ("Anomaly.Client.Core", new[] { "OnInitializeMelon", "InitializeFeature", "VerifyMandatoryPatches" }),
            ("Anomaly.Client.Auth.AuthHandler", new[] { "Init" }),
            ("Anomaly.Client.Auth.PlayerAuthProfileStore", new[] { "LoadExisting", "PrepareExisting", "ValidatePrivateIdentity", "ValidateStoredIdentityLineage", "SynchronizeIdentityPublic" }),
            ("Anomaly.Client.Internal.Modding.ManagedModRuntime", new[] { "Initialize", "DiscoverCandidates", "Load", "WriteJsonAtomic" }),
            ("Anomaly.Client.Internal.UI.PreloaderSkinBootstrap", new[] { "ApplySkin" }),
            ("Anomaly.Client.Assets.AssetBundleManager", new[] { "Initialize", "PreparePack", "Materialize", "ClearRemote" }),
            ("Anomaly.AssetValidation.BundleValidator", new[] { "Validate" }),
            ("Anomaly.Client.Assets.NativeAssetBundle", new[] { "LoadFromFile", "Unload" }),
            ("Anomaly.Client.Diagnostics.DiagnosticBootstrap", new[] { "Initialize" }),
            ("Anomaly.Client.Diagnostics.CrashReportWriter", new[] { "CleanupOldReports" }),
            ("Anomaly.Client.Patches.Menu.ModSettings.ModSettingsTabInstaller", new[] { "PopulateNewlyInstalled" }),
            ("Anomaly.Client.Patches.Menu.ModSettings.ModSettingsRenderer", new[] { "Attach", "PopulateInto" }),
            ("Anomaly.Client.Internal.Bootstrap.ClientRuntime", new[] { "Initialize", "HandleSceneLoaded" }),
            ("Anomaly.Client.Api.Localization.LocalizationService", new[] { "EnsureAnomalyExtracted", "ResolveLocale", "LoadAnomalyTranslations", "LoadYamlFile" }),
        };
        foreach (var target in targets)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(target.Type, false))
                .FirstOrDefault(t => t != null);
            if (type == null) { Core.Mark("probe.unavailable", new { target = target.Type }); continue; }
            foreach (string methodName in target.Methods)
            {
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == methodName && !m.ContainsGenericParameters && !m.IsAbstract).ToArray();
                if (methods.Length == 0) Core.Mark("probe.unavailable", new { target = target.Type + "." + methodName });
                foreach (var method in methods) Install(method);
            }
        }
    }

    private static void Install(MethodInfo method)
    {
        string name = method.DeclaringType!.FullName + "." + method.Name;
        try
        {
            if (HasShield(method) || HasShield(method.DeclaringType!) || HasShield(method.Module.Assembly))
            {
                Core.Mark("probe.protected", new { target = name });
                return;
            }
            Owner.Patch(method, prefix: new HarmonyMethod(typeof(ManagedMethodProbes), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(ManagedMethodProbes), nameof(Finalizer)));
            var owners = HarmonyLib.Harmony.GetPatchInfo(method)?.Owners;
            Core.Mark(owners?.Contains(Owner.Id) == true ? "probe.installed" : "probe.unavailable", new { target = name });
        }
        catch (Exception exception) { Core.Mark("probe.unavailable", new { target = name, reason = exception.GetType().Name }); }
    }

    private static bool HasShield(MemberInfo member) => member.CustomAttributes.Any(a => a.AttributeType.Name == "PatchShield");
    private static bool HasShield(Assembly assembly) => assembly.CustomAttributes.Any(a => a.AttributeType.Name == "PatchShield");

    private static void Prefix(MethodBase __originalMethod, object[] __args, out Call __state)
    {
        long start = Stopwatch.GetTimestamp();
        string name = __originalMethod.DeclaringType!.FullName + "." + __originalMethod.Name;
        if (__originalMethod.Name == "InitializeFeature" && __args.Length > 0 && __args[0] is string feature)
            name = "anomaly.feature." + feature;
        __state = new Call(start, name);
    }

    private static Exception? Finalizer(Exception? __exception, Call __state)
    {
        try
        {
            if (__state.Started != 0)
                Core.EndSpan(__state.Name, __state.Started, __exception == null ? null : new { fault = __exception.GetType().Name });
        }
        catch { /* Preserve the original method's exception and result. */ }
        return __exception;
    }

    internal static void Shutdown() => Owner.UnpatchSelf();
}
