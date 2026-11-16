using System.Collections;
using System.Reflection;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using HarmonyLib;
using Mirror;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ContentAcquisitionIdleTests : IDisposable
{
    private readonly Harmony _harmony = new("anomaly.tests.content.idle");

    public ContentAcquisitionIdleTests()
    {
        ConnectionContentBootstrap.Reset(); AuthenticatedTransportSessions.Reset(); MEC.Timing.Reset();
        IdleMode.Reset(); ReferenceHub.AllHubs.Clear();
        Install("FixedUpdatePatch", ContentAcquisitionIdleGuard.FixedUpdateTarget);
        Install("SetIdleModePatch", ContentAcquisitionIdleGuard.SetIdleModeTarget);
    }

    [Fact]
    public void NoAcquisitionUsesNativeTimersAndIdleTransition()
    {
        ElapseStockTimers();
        new IdleMode().Tick();
        Assert.True(IdleMode.IdleModeActive);
        Assert.Equal(1, IdleMode.TimerConsumptions);
        Assert.Equal(1, IdleMode.TargetFrameRate);
        Assert.Equal(.01f, IdleMode.TimeScale);
    }

    [Fact]
    public void AcquisitionWakesNativeSchedulerAndDefersTimerConsumptionUntilLastCleanup()
    {
        IdleMode.SetIdleMode(true);
        Assert.True(IdleMode.IdleModeActive);
        var connection = Start();
        Assert.False(IdleMode.IdleModeActive);
        Assert.Equal(60, IdleMode.TargetFrameRate);
        Assert.Equal(1f, IdleMode.TimeScale);
        ElapseStockTimers();
        var idle = new IdleMode();
        idle.Tick();
        IdleMode.SetIdleMode(true);
        IdleMode.ForceIdle();
        Assert.False(IdleMode.IdleModeActive);
        Assert.True(IdleMode.IdleTimerReady);
        Assert.True(IdleMode.PreauthTimerReady);
        Assert.Equal(0, IdleMode.TimerConsumptions);
        ConnectionContentBootstrap.Cleanup(connection);
        idle.Tick();
        Assert.True(IdleMode.IdleModeActive);
        Assert.Equal(1, IdleMode.TimerConsumptions);
    }

    [Fact]
    public void OneConnectionCannotReleaseAnotherConnectionsBusyReason()
    {
        var first = Start();
        var second = Start();
        ConnectionContentBootstrap.Cleanup(first);
        IdleMode.ForceIdle();
        Assert.True(ConnectionContentBootstrap.HasPendingAcquisition);
        Assert.False(IdleMode.IdleModeActive);
        ConnectionContentBootstrap.Cleanup(second);
        IdleMode.ForceIdle();
        Assert.False(ConnectionContentBootstrap.HasPendingAcquisition);
        Assert.True(IdleMode.IdleModeActive);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("rejected")]
    [InlineData("revoked")]
    public void NonLiveAdmissionCannotRetainBusyReasonBeforeWatchdogCleanup(string terminal)
    {
        var connection = Start();
        if (terminal == "expired") SetElapsed(connection, 120);
        else if (terminal == "rejected") ConnectionContentBootstrap.MarkRejected(connection);
        else AuthenticatedTransportSessions.QuarantineConnection(connection);
        Assert.False(ConnectionContentBootstrap.HasPendingAcquisition);
        IdleMode.ForceIdle();
        Assert.True(IdleMode.IdleModeActive);
    }

    [Fact]
    public void PendingGenerationReplacementPreservesTheAbsoluteDeadline()
    {
        var connection = Start();
        SetElapsed(connection, 119);
        ConnectionContentBootstrap.OnGenerationStarted();
        Assert.True(ConnectionContentBootstrap.HasPendingAcquisition);
        SetElapsed(connection, 120);
        Assert.False(ConnectionContentBootstrap.HasPendingAcquisition);
        IdleMode.ForceIdle();
        Assert.True(IdleMode.IdleModeActive);
    }

    [Fact]
    public void OtherOwnersPauseAndDisabledSettingsAreNotChangedOrRestoredByAcquisition()
    {
        IdleMode.PauseIdleMode = true;
        var connection = Start();
        ElapseStockTimers();
        ConnectionContentBootstrap.Cleanup(connection);
        new IdleMode().Tick();
        Assert.True(IdleMode.PauseIdleMode);
        Assert.True(IdleMode.IdleTimerReady);
        Assert.Equal(0, IdleMode.TimerConsumptions);
        Assert.False(IdleMode.IdleModeActive);

        IdleMode.PauseIdleMode = false;
        IdleMode.IdleModeEnabled = false;
        connection = Start();
        ConnectionContentBootstrap.Cleanup(connection);
        new IdleMode().Tick();
        Assert.False(IdleMode.IdleModeEnabled);
        Assert.False(IdleMode.IdleModeActive);
    }

    [Fact]
    public void LocalDummyAndUnauthenticatedConnectionsCannotAcquireBusyReason()
    {
        ConnectionContentBootstrap.Begin(new LocalConnectionToClient());
        ConnectionContentBootstrap.Begin(new NetworkManagerUtils.Dummies.DummyNetworkConnection());
        Assert.Throws<InvalidOperationException>(() => ConnectionContentBootstrap.Begin(new NetworkConnectionToClient()));
        Assert.False(ConnectionContentBootstrap.HasPendingAcquisition);
        IdleMode.ForceIdle();
        Assert.True(IdleMode.IdleModeActive);
    }

    [Fact]
    public void ResetReleasesAllOwnedAdmissionsWithoutChangingNativePauseState()
    {
        Start(); Start();
        ConnectionContentBootstrap.Reset();
        Assert.False(ConnectionContentBootstrap.HasPendingAcquisition);
        IdleMode.ForceIdle();
        Assert.True(IdleMode.IdleModeActive);
        Assert.False(IdleMode.PauseIdleMode);
    }

    private void Install(string nestedType, MethodBase target)
    {
        Assert.NotNull(target);
        var prefix = typeof(ContentAcquisitionIdleGuard).GetNestedType(nestedType, BindingFlags.NonPublic)
            .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static NetworkConnectionToClient Start()
    {
        var connection = new NetworkConnectionToClient();
        AuthenticatedTransportSessions.RegisterConnection(connection, new PlayerStorage
        {
            UserId = "idle-fixture@anomaly", AcceptedPeer = new object(),
            ClientToServerKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            ServerToClientKey = Enumerable.Repeat((byte)2, 32).ToArray(),
            SessionId = Enumerable.Repeat((byte)3, 16).ToArray(),
            TranscriptHash = Enumerable.Repeat((byte)4, 32).ToArray(),
        });
        ConnectionContentBootstrap.Begin(connection);
        return connection;
    }

    private static void SetElapsed(NetworkConnectionToClient connection, double elapsed)
    {
        var admissions = (IDictionary)typeof(ConnectionContentBootstrap)
            .GetField("Admissions", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var admission = admissions[connection];
        var gate = (ConnectionContentState)admission.GetType()
            .GetField("Gate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(admission);
        typeof(ConnectionContentState).GetField("_elapsed", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(gate, new Func<double>(() => elapsed));
    }

    private static void ElapseStockTimers() => IdleMode.IdleTimerReady = IdleMode.PreauthTimerReady = true;

    public void Dispose()
    {
        _harmony.UnpatchAll(_harmony.Id);
        ConnectionContentBootstrap.Reset(); AuthenticatedTransportSessions.Reset(); MEC.Timing.Reset();
        IdleMode.Reset(); ReferenceHub.AllHubs.Clear();
    }
}
