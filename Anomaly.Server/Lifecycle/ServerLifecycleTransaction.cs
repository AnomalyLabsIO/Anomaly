using System;
using System.Collections.Generic;
using System.Linq;

namespace Anomaly.Server.Lifecycle;

internal enum ServerInitializationStage
{
    CaptureRuntimeState,
    OffThreadLogging,
    IdentityAndAuthentication,
    MandatoryPatches,
    PasswordPolicy,
    MessageRegistry,
    FeatureBridges,
    AssetsAndContent,
    LifecycleSubscriptions,
    RoundAndFacility,
    RuntimeOverrides,
    ConfigReloadSubscription,
}

/// <summary>
/// Executes the production server initialization stages in one reviewed order.
/// Admission is closed before the first stage and again before rollback; it is
/// opened only by the final commit callback.
/// </summary>
internal static class ServerLifecycleTransaction
{
    internal const string AnomalyHarmonyOwnerId = "Anomaly.Server";

    private static readonly ServerInitializationStage[] OrderedStages =
    {
        ServerInitializationStage.CaptureRuntimeState,
        ServerInitializationStage.OffThreadLogging,
        ServerInitializationStage.IdentityAndAuthentication,
        ServerInitializationStage.MandatoryPatches,
        ServerInitializationStage.PasswordPolicy,
        ServerInitializationStage.MessageRegistry,
        ServerInitializationStage.FeatureBridges,
        ServerInitializationStage.AssetsAndContent,
        ServerInitializationStage.LifecycleSubscriptions,
        ServerInitializationStage.RoundAndFacility,
        ServerInitializationStage.RuntimeOverrides,
        ServerInitializationStage.ConfigReloadSubscription,
    };

    internal static IReadOnlyList<ServerInitializationStage> Stages => OrderedStages;

    internal static bool Execute(
        IReadOnlyDictionary<ServerInitializationStage, Action> stages,
        Action closeAdmission,
        Action commitOperational,
        Action<Exception> rollback)
    {
        if (stages == null)
            throw new ArgumentNullException(nameof(stages));
        if (closeAdmission == null)
            throw new ArgumentNullException(nameof(closeAdmission));
        if (commitOperational == null)
            throw new ArgumentNullException(nameof(commitOperational));
        if (rollback == null)
            throw new ArgumentNullException(nameof(rollback));

        try
        {
            closeAdmission();
            var expected = new HashSet<ServerInitializationStage>(OrderedStages);
            if (stages.Count != OrderedStages.Length || stages.Keys.Any(stage => !expected.Contains(stage)))
                throw new InvalidOperationException("Server lifecycle transaction stages are incomplete or unexpected.");
            foreach (ServerInitializationStage stage in OrderedStages)
            {
                Action action = stages[stage]
                                ?? throw new InvalidOperationException($"Server lifecycle stage '{stage}' is null.");
                action();
            }
            commitOperational();
            return true;
        }
        catch (Exception exception)
        {
            // A failing commit or stage must close the gate before any restore
            // or unpatch callback can expose vanilla offline admission.
            closeAdmission();
            rollback(exception);
            return false;
        }
    }
}

/// <summary>
/// Couples restoration to owner-scoped unpatching. The Anomaly owner is never
/// removed unless restoration succeeds; failure retains the deny-all patch.
/// </summary>
internal static class ServerLifecycleRollbackPolicy
{
    internal static bool TryRestoreAndOwnerUnpatch(
        Func<bool> restoreRuntimeState,
        Func<bool> unpatchAnomalyOwner,
        Action retainDenyAll,
        Action<string, Exception> reportFailure)
    {
        if (restoreRuntimeState == null)
            throw new ArgumentNullException(nameof(restoreRuntimeState));
        if (unpatchAnomalyOwner == null)
            throw new ArgumentNullException(nameof(unpatchAnomalyOwner));
        if (retainDenyAll == null)
            throw new ArgumentNullException(nameof(retainDenyAll));
        if (reportFailure == null)
            throw new ArgumentNullException(nameof(reportFailure));

        bool restored;
        try
        {
            restored = restoreRuntimeState();
        }
        catch (Exception exception)
        {
            reportFailure("runtime state restoration", exception);
            restored = false;
        }
        if (!restored)
        {
            retainDenyAll();
            return false;
        }

        bool unpatched;
        try
        {
            unpatched = unpatchAnomalyOwner();
        }
        catch (Exception exception)
        {
            reportFailure("Anomaly owner unpatch", exception);
            unpatched = false;
        }
        if (!unpatched)
        {
            retainDenyAll();
            return false;
        }

        return true;
    }
}
