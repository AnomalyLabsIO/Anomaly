using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class GuardAuditTests
{
    [Theory]
    [InlineData(1000, 0, false)]
    [InlineData(5000, 0, false)]
    [InlineData(5001, 0, true)]
    [InlineData(long.MaxValue, 1000, true)]
    public void Verification_deadline_must_remain_within_bounded_cadence(long deadline, long now, bool abnormal)
    {
        Assert.Equal(abnormal, RuntimeGuard.DeadlineBeyondCadence(deadline, now));
    }

    [Fact]
    public void Mutation_barrier_covers_nested_changes_and_commits_risk_before_reopening()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        long original = audit.Snapshot.Generation;
        audit.BeginMutation();
        audit.BeginMutation();
        Assert.Equal(2, audit.Snapshot.MutationInProgress);
        audit.EndMutation(GuardAuditFlags.None, "blocked by downstream guard");
        Assert.Equal(1, audit.Snapshot.MutationInProgress);
        Assert.Equal(original, audit.Snapshot.Generation);
        audit.EndMutation(GuardAuditFlags.ExternalHarmonyModification, "applied");
        GuardSnapshot result = audit.Snapshot;
        Assert.Equal(0, result.MutationInProgress);
        Assert.True(result.Generation > original);
        Assert.NotEqual(0, result.Flags & 1);
    }

    [Fact]
    public void Snapshot_is_immutable_and_risk_is_monotonic()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        GuardSnapshot clean = audit.Snapshot;
        audit.Record(GuardAuditFlags.ExternalHarmonyModification, "allowed critical patch");
        GuardSnapshot changed = audit.Snapshot;
        Assert.Equal(0, clean.Flags);
        Assert.True(changed.Healthy);
        Assert.True(changed.AllowExternalModifications);
        Assert.Equal((ushort)GuardAuditFlags.ExternalHarmonyModification, changed.Flags);
        Assert.True(changed.Generation > clean.Generation);
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "core patch denied");
        Assert.Equal((ushort)(GuardAuditFlags.ExternalHarmonyModification | GuardAuditFlags.BlockedAttemptObserved), audit.Snapshot.Flags);
        Assert.Equal(changed.Generation, audit.Snapshot.Generation);
    }

    [Fact]
    public void Repeated_real_changes_invalidate_frozen_generation_but_reconciliation_does_not()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        audit.Record(GuardAuditFlags.ExternalHarmonyModification, "same target", occurrence: true);
        long frozen = audit.Snapshot.Generation;
        audit.Record(GuardAuditFlags.ExternalHarmonyModification, "same target");
        Assert.Equal(frozen, audit.Snapshot.Generation);
        audit.Record(GuardAuditFlags.ExternalHarmonyModification, "same target", occurrence: true);
        Assert.True(audit.Snapshot.Generation > frozen);
    }

    [Fact]
    public void Blocked_attempt_is_distinct_from_risky_modification()
    {
        var audit = new GuardAudit(false);
        audit.Ready();
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "blocked");
        Assert.Equal(0, audit.Snapshot.Flags & 15);
        Assert.True(audit.Snapshot.Healthy);
    }

    [Fact]
    public void Fatal_monitor_failure_cannot_be_reset_by_ready_or_opt_in()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        audit.Record(GuardAuditFlags.CoverageIncomplete, "guard hook removed", fatal: true);
        audit.Ready();
        Assert.False(audit.Snapshot.Healthy);
        Assert.NotEqual(0, audit.Snapshot.Flags & (ushort)GuardAuditFlags.CoverageIncomplete);
    }

    [Fact]
    public void Nonfatal_observations_do_not_supply_a_fatal_reason()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        audit.Record(GuardAuditFlags.CoverageIncomplete, "advisory coverage observation");
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "patch denied");

        Assert.Null(audit.FatalReason);
        Assert.True(audit.Snapshot.Healthy);
    }

    [Fact]
    public void First_fatal_reason_survives_later_failures_and_ready()
    {
        var audit = new GuardAudit(true);
        audit.Ready();
        audit.Record(GuardAuditFlags.CoverageIncomplete, "first monitor failure", fatal: true);
        audit.Record(GuardAuditFlags.InventoryDivergence, "later monitor failure", fatal: true);
        audit.Ready();

        Assert.Equal("first monitor failure", audit.FatalReason);
        Assert.False(audit.Snapshot.Healthy);
    }

    [Fact]
    public void Fatal_reason_is_bounded_to_the_diagnostic_limit()
    {
        var audit = new GuardAudit(false);
        string reason = new string('x', 2048) + "discarded suffix";
        audit.Record(GuardAuditFlags.CoverageIncomplete, reason, fatal: true);

        Assert.Equal(new string('x', 2048), audit.FatalReason);
    }

    [Fact]
    public void Repeated_observations_are_deduplicated_and_diagnostics_are_bounded()
    {
        var audit = new GuardAudit(false);
        audit.Ready();
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "repeat");
        long generation = audit.Snapshot.Generation;
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "repeat");
        Assert.Equal(generation, audit.Snapshot.Generation);
        for (int i = 0; i < 2048; i++)
            audit.Record(GuardAuditFlags.BlockedAttemptObserved, "blocked " + i);
        Assert.Equal(128, audit.Diagnostics.Length);
        long bounded = audit.Snapshot.Generation;
        audit.Record(GuardAuditFlags.BlockedAttemptObserved, "beyond budget");
        Assert.Equal(bounded, audit.Snapshot.Generation);
    }
}
