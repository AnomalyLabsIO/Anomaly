using MelonLoader;

[assembly: MelonInfo(typeof(Anomaly.Bootstrap.Core), "Anomaly Bootstrap", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: PatchShield]
[assembly: MelonPriority(int.MinValue)]
[assembly: HarmonyDontPatchAll]

namespace Anomaly.Bootstrap;

public sealed class Core : MelonPlugin
{
    public static StartupState State { get; private set; } = StartupState.NotStarted;
    private long _nextDiagnostics;
    private string? _lastGuardDiagnostic;
    private string[] _diagnosticEntries = Array.Empty<string>();
    private int _diagnosticIndex;
    private Func<bool>? _emitDiagnostic;
    private bool _deinitializing;

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
            if (!RuntimeGuard.Verify(requireComplete: false))
                throw new InvalidOperationException("Required Bootstrap protection is unavailable.");
            State = StartupState.Ready;
        }
        catch (Exception ex)
        {
            Fail("Bootstrap protection failed before MelonMods were loaded.", ex);
        }
    }

    public override void OnApplicationStarted()
    {
        if (State != StartupState.Ready)
        {
            Fail("Bootstrap lifecycle state changed before the game started.");
            return;
        }
        try
        {
            ProtectedMethodCatalog.ResolveNativeTargetsAfterSupportReady();
            RuntimeGuard.Verify();
        }
        catch (Exception ex)
        {
            Fail("Bootstrap protection failed when the game started.", ex);
            return;
        }
        if (HandleGuardFailure()) return;
        AdmissionInventoryLedger.Checkpoint();
        while (TryLogGuardDiagnostic()) { }
    }

    public override void OnUpdate()
    {
        if (_deinitializing && State == StartupState.Failed && !RuntimeGuard.Snapshot.Healthy) return;
        if (State != StartupState.Ready)
        {
            Fail("Bootstrap lifecycle state changed after protection became ready.");
            return;
        }
        if (HandleGuardFailure()) return;
        RuntimeGuard.Update(_emitDiagnostic ??= TryLogGuardDiagnostic);
        if (HandleGuardFailure()) return;
        GuardTimingDiagnostics.ReportIfDue();
    }

    private static bool HandleGuardFailure()
    {
        // Worker/detour callbacks only latch evidence. The main thread presents
        // the warning after verification releases its runtime and catalog locks.
        string? reason = RuntimeGuard.FatalReason;
        if (reason == null) return false;
        Fail("Required Bootstrap protection failed. " + reason);
        return true;
    }

    private bool TryLogGuardDiagnostic()
    {
        if (_diagnosticIndex >= _diagnosticEntries.Length)
        {
            if (Environment.TickCount64 < _nextDiagnostics) return false;
            _nextDiagnostics = Environment.TickCount64 + 1000;
            _diagnosticEntries = RuntimeGuard.Diagnostics;
            int previous = _lastGuardDiagnostic == null ? -1 : Array.IndexOf(_diagnosticEntries, _lastGuardDiagnostic);
            _diagnosticIndex = previous + 1;
        }
        if (_diagnosticIndex >= _diagnosticEntries.Length) return false;
        using var timing = GuardTimingDiagnostics.Begin(GuardTimingPhase.CoreLogDiagnostics);
        _lastGuardDiagnostic = _diagnosticEntries[_diagnosticIndex++];
        MelonLogger.Warning("Authentication guard: " + _lastGuardDiagnostic);
        return true;
    }

    public override void OnDeinitializeMelon()
    {
        _deinitializing = true;
        State = StartupState.Failed;
        RuntimeGuard.Invalidate("Bootstrap was unregistered or the loader shut down.");
    }

    private static void Fail(string message, Exception? ex = null)
    {
        string warning = ex == null ? message : message + "\r\n\r\n" + ex.Message;
        string? cause = RuntimeGuard.FatalReason;
        if (cause != null && !warning.Contains(cause, StringComparison.Ordinal))
            warning += "\r\n\r\n" + cause;
        try
        {
            State = StartupState.Failed;
            RuntimeGuard.Invalidate(message);
            MelonLogger.Error(ex == null ? message : message + " " + ex);
        }
        finally
        {
            BlockingFailureDialog.ShowAndExit(warning);
        }
    }
}

public enum StartupState
{
    NotStarted,
    Running,
    Failed,
    Ready
}
