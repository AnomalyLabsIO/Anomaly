using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Bootstrap.Core), "Anomaly Bootstrap", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: PatchShield]
[assembly: MelonPriority(int.MinValue)]
[assembly: HarmonyDontPatchAll]

namespace Anomaly.Bootstrap;

public sealed class Core : MelonPlugin
{
    public static StartupState State { get; private set; } = StartupState.NotStarted;
    private long _nextVerification;
    private string? _lastGuardDiagnostic;

    public override void OnEarlyInitializeMelon()
    {
        State = StartupState.Running;
        try
        {
            // MelonLoader 0.7.3 inserts dotted category IDs as TOML paths but reads
            // them as literal table names, silently losing entries on save.
            var category = MelonPreferences.CreateCategory("AnomalyBootstrap", "Anomaly security");
            bool allowExternal = category.CreateEntry("AllowExternalAuthenticationModifications", false,
                description: "Allow modifications to external authentication dependencies and report them to the server. Requires restart; Anomaly code and monitoring remain protected.").Value;
            category.SaveToFile(printmsg: false);
            RuntimeGuard.Install(HarmonyInstance, allowExternal);
            AdmissionInventoryLedger.Install();
        }
        catch (Exception ex)
        {
            Fail("Early Bootstrap protection could not initialize.", ex);
        }
    }

    public override void OnPreModsLoaded()
    {
        try
        {
            ApplyTrustGate();
            if (!RuntimeGuard.Verify(requireComplete: false))
                throw new InvalidOperationException("Required Bootstrap protection is unavailable.");
            State = StartupState.Ready;
        }
        catch (Exception ex)
        {
            Fail("The pre-load trust gate or protection failed before MelonMods were loaded.", ex);
        }
    }

    private static void ApplyTrustGate()
    {
        var anomalyRoot = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly");
        var gate = new ModTrustGate(
            MelonEnvironment.ModsDirectory,
            Path.Combine(anomalyRoot, "Modules"),
            Path.Combine(anomalyRoot, "mod-trust.json"),
            Path.Combine(anomalyRoot, "Quarantine"));

        var result = gate.Apply();
        foreach (var approved in result.Approved)
            MelonLogger.Msg($"Mod trust approved: {approved.FileName} ({approved.Sha256}).");
        foreach (var warning in result.Warnings)
            MelonLogger.Warning(warning);
        foreach (var quarantined in result.Quarantined)
            MelonLogger.Warning($"Unapproved module quarantined before load: {quarantined.FileName} ({quarantined.Sha256}).");
        foreach (var failure in result.Failures)
            MelonLogger.Error(failure);

        if (result.Failures.Count > 0)
            throw new InvalidOperationException("The pre-load trust gate could not contain every unapproved assembly.");
    }

    public override void OnApplicationStarted()
    {
        ProtectedMethodCatalog.ResolveNativeTargetsAfterSupportReady();
        RuntimeGuard.Verify();
        LogGuardDiagnostics();
    }

    public override void OnUpdate()
    {
        if (State != StartupState.Ready || Environment.TickCount64 < _nextVerification)
            return;
        _nextVerification = Environment.TickCount64 + 1000;
        ProtectedMethodCatalog.ResolveNativeTargetsAfterSupportReady();
        RuntimeGuard.Verify();
        LogGuardDiagnostics();
    }

    private void LogGuardDiagnostics()
    {
        string[] entries = RuntimeGuard.Diagnostics;
        int previous = _lastGuardDiagnostic == null ? -1 : Array.IndexOf(entries, _lastGuardDiagnostic);
        foreach (string entry in entries.Skip(previous + 1))
            MelonLogger.Warning("Authentication guard: " + entry);
        if (entries.Length != 0) _lastGuardDiagnostic = entries[^1];
    }

    public override void OnDeinitializeMelon()
    {
        State = StartupState.Failed;
        RuntimeGuard.Invalidate("Bootstrap was unregistered or the loader shut down.");
    }

    private static void Fail(string message, Exception ex)
    {
        State = StartupState.Failed;
        RuntimeGuard.Invalidate(message);
        MelonLogger.Error(message + " " + ex);
        Environment.Exit(78);
    }
}

public enum StartupState
{
    NotStarted,
    Running,
    Failed,
    Ready
}
