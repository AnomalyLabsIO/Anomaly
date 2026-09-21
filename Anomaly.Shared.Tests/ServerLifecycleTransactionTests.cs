using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Anomaly.Server.Lifecycle;
using HarmonyLib;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ServerLifecycleTransactionTests
{
    private const string UnrelatedOwnerId = "Anomaly.Tests.UnrelatedOwner";

    public static IEnumerable<object[]> InitializationStages =>
        ServerLifecycleTransaction.Stages.Select(stage => new object[] { (int)stage });

    [Theory]
    [MemberData(nameof(InitializationStages))]
    public void Every_initialization_stage_failure_restores_snapshot_before_owner_unpatch(
        int failingStageValue)
    {
        var failingStage = (ServerInitializationStage)failingStageValue;
        var original = new RuntimeAuthState(onlineMode: true, ipPassthrough: true, modded: false);
        RuntimeAuthState current = original;
        RuntimeAuthState captured = default;
        bool snapshotCaptured = false;
        bool admissionOpen = true;
        bool committed = false;
        bool rollbackCalled = false;
        bool unpatchCalled = false;
        bool retainedDenyAll = false;
        var executed = new List<ServerInitializationStage>();

        var actions = ServerLifecycleTransaction.Stages.ToDictionary(
            stage => stage,
            stage => (Action)(() =>
            {
                executed.Add(stage);
                if (stage == ServerInitializationStage.CaptureRuntimeState)
                {
                    captured = current;
                    snapshotCaptured = true;
                }

                // Model a stage that made a partial runtime mutation before it
                // failed. The production runner must use the captured values,
                // not assumptions about which stage is normally side-effectful.
                current = new RuntimeAuthState(onlineMode: false, ipPassthrough: false, modded: true);
                if (stage == failingStage)
                    throw new InvalidOperationException("injected failure at " + stage);
            }));

        bool succeeded = ServerLifecycleTransaction.Execute(
            actions,
            closeAdmission: () => admissionOpen = false,
            commitOperational: () =>
            {
                committed = true;
                admissionOpen = true;
            },
            rollback: _ =>
            {
                rollbackCalled = true;
                bool removed = ServerLifecycleRollbackPolicy.TryRestoreAndOwnerUnpatch(
                    restoreRuntimeState: () =>
                    {
                        Assert.True(snapshotCaptured);
                        current = captured;
                        return true;
                    },
                    unpatchAnomalyOwner: () =>
                    {
                        Assert.Equal(original, current);
                        unpatchCalled = true;
                        return true;
                    },
                    retainDenyAll: () => retainedDenyAll = true,
                    reportFailure: (_, _) => { });
                Assert.True(removed);
            });

        int failureIndex = ServerLifecycleTransaction.Stages.ToList().IndexOf(failingStage);
        Assert.False(succeeded);
        Assert.False(admissionOpen);
        Assert.False(committed);
        Assert.True(rollbackCalled);
        Assert.True(unpatchCalled);
        Assert.False(retainedDenyAll);
        Assert.Equal(original, current);
        Assert.Equal(ServerLifecycleTransaction.Stages.Take(failureIndex + 1), executed);
    }

    [Fact]
    public void Owner_scoped_unpatch_preserves_unrelated_harmony_owner()
    {
        MethodInfo target = AccessTools.Method(typeof(ServerLifecycleTransactionTests), nameof(PatchTarget));
        var anomalyHarmony = new Harmony(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
        var unrelatedHarmony = new Harmony(UnrelatedOwnerId);
        try
        {
            anomalyHarmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(ServerLifecycleTransactionTests),
                    nameof(AnomalyPrefix)));
            unrelatedHarmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(ServerLifecycleTransactionTests),
                    nameof(UnrelatedPostfix)));
            AssertOwners(target, anomaly: true, unrelated: true);

            bool retainedDenyAll = false;
            bool removed = ServerLifecycleRollbackPolicy.TryRestoreAndOwnerUnpatch(
                restoreRuntimeState: () => true,
                unpatchAnomalyOwner: () =>
                {
                    anomalyHarmony.UnpatchAll(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
                    return true;
                },
                retainDenyAll: () => retainedDenyAll = true,
                reportFailure: (_, _) => { });

            Assert.True(removed);
            Assert.False(retainedDenyAll);
            AssertOwners(target, anomaly: false, unrelated: true);
        }
        finally
        {
            anomalyHarmony.UnpatchAll(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
            unrelatedHarmony.UnpatchAll(UnrelatedOwnerId);
        }
    }

    [Fact]
    public void Restoration_failure_keeps_admission_closed_and_retains_anomaly_patch()
    {
        MethodInfo target = AccessTools.Method(typeof(ServerLifecycleTransactionTests), nameof(PatchTarget));
        var anomalyHarmony = new Harmony(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
        var unrelatedHarmony = new Harmony(UnrelatedOwnerId);
        try
        {
            anomalyHarmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(ServerLifecycleTransactionTests),
                    nameof(AnomalyPrefix)));
            unrelatedHarmony.Patch(
                target,
                postfix: new HarmonyMethod(
                    typeof(ServerLifecycleTransactionTests),
                    nameof(UnrelatedPostfix)));

            bool admissionOpen = true;
            bool unpatchCalled = false;
            bool retainedDenyAll = false;
            var actions = ServerLifecycleTransaction.Stages.ToDictionary(
                stage => stage,
                stage => (Action)(() =>
                {
                    if (stage == ServerInitializationStage.RuntimeOverrides)
                        throw new InvalidOperationException("injected runtime-restore precursor failure");
                }));

            bool succeeded = ServerLifecycleTransaction.Execute(
                actions,
                closeAdmission: () => admissionOpen = false,
                commitOperational: () => admissionOpen = true,
                rollback: _ =>
                {
                    bool removed = ServerLifecycleRollbackPolicy.TryRestoreAndOwnerUnpatch(
                        restoreRuntimeState: () => false,
                        unpatchAnomalyOwner: () =>
                        {
                            unpatchCalled = true;
                            anomalyHarmony.UnpatchAll(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
                            return true;
                        },
                        retainDenyAll: () => retainedDenyAll = true,
                        reportFailure: (_, _) => { });
                    Assert.False(removed);
                });

            Assert.False(succeeded);
            Assert.False(admissionOpen);
            Assert.False(unpatchCalled);
            Assert.True(retainedDenyAll);
            AssertOwners(target, anomaly: true, unrelated: true);
        }
        finally
        {
            anomalyHarmony.UnpatchAll(ServerLifecycleTransaction.AnomalyHarmonyOwnerId);
            unrelatedHarmony.UnpatchAll(UnrelatedOwnerId);
        }
    }

    [Fact]
    public void Commit_failure_closes_admission_and_uses_the_same_restore_path()
    {
        var original = new RuntimeAuthState(onlineMode: true, ipPassthrough: false, modded: false);
        RuntimeAuthState current = original;
        RuntimeAuthState captured = default;
        bool admissionOpen = true;
        var executed = new List<ServerInitializationStage>();
        var actions = ServerLifecycleTransaction.Stages.ToDictionary(
            stage => stage,
            stage => (Action)(() =>
            {
                executed.Add(stage);
                if (stage == ServerInitializationStage.CaptureRuntimeState)
                    captured = current;
                if (stage == ServerInitializationStage.RuntimeOverrides)
                    current = new RuntimeAuthState(onlineMode: false, ipPassthrough: false, modded: true);
            }));

        bool succeeded = ServerLifecycleTransaction.Execute(
            actions,
            closeAdmission: () => admissionOpen = false,
            commitOperational: () => throw new InvalidOperationException("injected commit failure"),
            rollback: _ =>
            {
                bool removed = ServerLifecycleRollbackPolicy.TryRestoreAndOwnerUnpatch(
                    restoreRuntimeState: () =>
                    {
                        current = captured;
                        return true;
                    },
                    unpatchAnomalyOwner: () => true,
                    retainDenyAll: () => { },
                    reportFailure: (_, _) => { });
                Assert.True(removed);
            });

        Assert.False(succeeded);
        Assert.False(admissionOpen);
        Assert.Equal(original, current);
        Assert.Equal(ServerLifecycleTransaction.Stages, executed);
    }

    private static void AssertOwners(MethodInfo target, bool anomaly, bool unrelated)
    {
        Patches patches = Harmony.GetPatchInfo(target);
        Assert.NotNull(patches);
        Assert.Equal(anomaly, patches.Owners.Contains(ServerLifecycleTransaction.AnomalyHarmonyOwnerId));
        Assert.Equal(unrelated, patches.Owners.Contains(UnrelatedOwnerId));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int PatchTarget(int value) => value + 1;

    private static void AnomalyPrefix()
    {
    }

    private static void UnrelatedPostfix()
    {
    }

    private readonly struct RuntimeAuthState
    {
        internal RuntimeAuthState(bool onlineMode, bool ipPassthrough, bool modded)
        {
            OnlineMode = onlineMode;
            IpPassthrough = ipPassthrough;
            Modded = modded;
        }

        private bool OnlineMode { get; }
        private bool IpPassthrough { get; }
        private bool Modded { get; }
    }
}
