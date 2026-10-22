using Anomaly.Client.Api.Internal.Players;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class Scp079ObservationStateTests
{
    [Fact]
    public void ZeroValuesBecomeKnownOnlyAfterMatchingLifeReceipt()
    {
        var state = new Scp079ObservationState(7);
        Assert.Null(state.AuxiliaryPower);
        Assert.Null(state.Experience);
        Assert.Null(state.Tier);

        Assert.True(state.TryObserveAuxiliaryPower(7, 0f, out var previousPower));
        Assert.Null(previousPower);
        Assert.Equal(0f, state.AuxiliaryPower);

        Assert.True(state.TryObserveExperience(7, 0, 1, out var previousExperience, out var previousTier));
        Assert.Null(previousExperience);
        Assert.Null(previousTier);
        Assert.Equal(0, state.Experience);
        Assert.Equal(1, state.Tier);
    }

    [Fact]
    public void SpectatorContextChangeRequiresFreshPowerButPreservesGlobalExperience()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveAuxiliaryPower(7, 42f, out _);
        state.TryObserveExperience(7, 100, 2, out _, out _);

        state.InvalidateAuxiliaryPower();

        Assert.Null(state.AuxiliaryPower);
        Assert.Equal(100, state.Experience);
        Assert.Equal(2, state.Tier);
        Assert.True(state.TryObserveAuxiliaryPower(7, 42f, out var previousPower));
        Assert.Null(previousPower);
    }

    [Fact]
    public void ReusedNativeRoleCannotOverwriteAnEarlierLifeObservation()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveAuxiliaryPower(7, 42f, out _);
        state.TryObserveExperience(7, 100, 2, out _, out _);

        Assert.False(state.TryObserveAuxiliaryPower(8, 100f, out _));
        Assert.False(state.TryObserveExperience(8, 0, 1, out _, out _));
        Assert.Equal(42f, state.AuxiliaryPower);
        Assert.Equal(100, state.Experience);
        Assert.Equal(2, state.Tier);
    }

    [Fact]
    public void RemovedRoleRejectsLateReceiptsAndAReplacementStartsUnknown()
    {
        var previous = new Scp079ObservationState(7);
        previous.TryObserveAuxiliaryPower(7, 42f, out _);
        previous.TryObserveExperience(7, 100, 2, out _, out _);
        previous.Invalidate();

        Assert.True(previous.IsInvalidated);
        Assert.False(previous.TryObserveAuxiliaryPower(7, 50f, out _));
        Assert.False(previous.TryObserveExperience(7, 150, 2, out _, out _));
        Assert.Null(previous.AuxiliaryPower);
        Assert.Null(previous.Experience);
        Assert.Null(previous.Tier);

        var replacement = new Scp079ObservationState(8);
        Assert.Null(replacement.AuxiliaryPower);
        Assert.Null(replacement.Experience);
        Assert.True(replacement.TryObserveExperience(8, 0, 1, out var previousExperience, out _));
        Assert.Null(previousExperience);
    }

    [Fact]
    public void RepeatedReceiptsPreservePreviousValuesForChangeDetection()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveAuxiliaryPower(7, 42f, out _);
        state.TryObserveExperience(7, 100, 2, out _, out _);

        Assert.True(state.TryObserveAuxiliaryPower(7, 42f, out var previousPower));
        Assert.Equal(state.AuxiliaryPower, previousPower);
        Assert.True(state.TryObserveExperience(7, 100, 2, out var previousExperience, out var previousTier));
        Assert.Equal(state.Experience, previousExperience);
        Assert.Equal(state.Tier, previousTier);

        Assert.True(state.TryObserveExperience(7, 20, 1, out previousExperience, out previousTier));
        Assert.Equal(100, previousExperience);
        Assert.Equal(2, previousTier);
        Assert.Equal(20, state.Experience);
        Assert.Equal(1, state.Tier);
    }

    [Fact]
    public void DisconnectDuringExperienceCallbackSuppressesOldTierNotification()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveExperience(7, 100, 2, out _, out _);
        var events = new List<string>();

        state.DispatchExperienceChanges(() => true, () =>
        {
            events.Add("experience");
            state.Invalidate();
        }, () => events.Add("tier"));

        Assert.Equal(new[] { "experience" }, events);
    }

    [Fact]
    public void NewReceiptDuringExperienceCallbackCannotPublishOldTierAfterNewTier()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveExperience(7, 100, 2, out _, out _);
        var events = new List<string>();

        state.DispatchExperienceChanges(() => true, () =>
        {
            events.Add("experience-2");
            state.TryObserveExperience(7, 200, 3, out _, out _);
            state.DispatchExperienceChanges(() => true,
                () => events.Add("experience-3"), () => events.Add("tier-3"));
        }, () => events.Add("tier-2"));

        Assert.Equal(new[] { "experience-2", "experience-3", "tier-3" }, events);
    }

    [Fact]
    public void NativeRoleReuseDuringExperienceCallbackSuppressesOldTierNotification()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveExperience(7, 100, 2, out _, out _);
        bool sameLife = true;
        bool tierFired = false;

        state.DispatchExperienceChanges(() => sameLife, () => sameLife = false, () => tierFired = true);

        Assert.False(tierFired);
    }

    [Fact]
    public void DuplicateReceiptDuringExperienceCallbackStillPublishesPendingTier()
    {
        var state = new Scp079ObservationState(7);
        state.TryObserveExperience(7, 100, 2, out _, out _);
        bool tierFired = false;

        state.DispatchExperienceChanges(() => true,
            () => state.TryObserveExperience(7, 100, 2, out _, out _),
            () => tierFired = true);

        Assert.True(tierFired);
    }
}
