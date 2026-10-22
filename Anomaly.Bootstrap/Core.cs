using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Anomaly.Bootstrap.Core), "Anomaly Bootstrap", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: PatchShield]

namespace Anomaly.Bootstrap;

public sealed class Core : MelonPlugin
{
    public static StartupState State { get; private set; } = StartupState.NotStarted;
    
    public override void OnPreModsLoaded()
    {
        State = StartupState.Running;
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
        {
            State = StartupState.Failed;
            MelonLogger.Error("The pre-load trust gate could not contain every unapproved assembly. Exiting before MelonMods are loaded.");
            Environment.Exit(78);
        }
        State = StartupState.Ready;
    }
}

public enum StartupState
{
    NotStarted,
    Running,
    Failed,
    Ready
}
