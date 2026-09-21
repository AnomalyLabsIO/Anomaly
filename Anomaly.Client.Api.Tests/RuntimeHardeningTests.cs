using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Events;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class RuntimeHardeningTests
{
    [Fact]
    public void RemoteAudioRequiresOggContentSignature()
    {
        Assert.True(AudioClipFactory.HasOggHeader("OggS"u8.ToArray()));
        Assert.False(AudioClipFactory.HasOggHeader("RIFF"u8.ToArray()));
        Assert.False(AudioClipFactory.HasOggHeader(new byte[] { (byte)'O', (byte)'g' }));
    }

    [Fact]
    public void Remote_resources_fail_closed_and_require_exact_hash_and_kind()
    {
        byte[] approved = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        byte[] other = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        AnomalyResources.SetRemoteAssetAuthorization(null);
        try
        {
            Assert.False(AnomalyResources.IsRemoteAssetAuthorized(approved, AssetKind.Texture2D));

            AnomalyResources.SetRemoteAssetAuthorization((hash, kind) =>
                hash.SequenceEqual(approved) && kind == AssetKind.Texture2D);

            Assert.True(AnomalyResources.IsRemoteAssetAuthorized((byte[])approved.Clone(), AssetKind.Texture2D));
            Assert.False(AnomalyResources.IsRemoteAssetAuthorized(approved, AssetKind.AudioClip));
            Assert.False(AnomalyResources.IsRemoteAssetAuthorized(other, AssetKind.Texture2D));
            Assert.False(AnomalyResources.IsRemoteAssetAuthorized(new byte[31], AssetKind.Texture2D));
        }
        finally
        {
            AnomalyResources.SetRemoteAssetAuthorization(null);
        }
    }

    [Fact]
    public void Replay_table_separates_and_resets_raw_and_decoded_limiters()
    {
        AnomalyReplayTable.Reset();
        var initialRaw = AnomalyReplayTable.RawInboundRateLimiter;
        var initialDecoded = AnomalyReplayTable.InboundRateLimiter;
        Assert.NotSame(initialRaw, initialDecoded);
        Assert.Equal(1, initialRaw.RegisterProtocolViolation().DroppedInWindow);
        Assert.Equal(0, initialDecoded.TryAccept(1, 0).DroppedInWindow);

        AnomalyReplayTable.Reset();

        Assert.NotSame(initialRaw, AnomalyReplayTable.RawInboundRateLimiter);
        Assert.NotSame(initialDecoded, AnomalyReplayTable.InboundRateLimiter);
        Assert.NotSame(
            AnomalyReplayTable.RawInboundRateLimiter,
            AnomalyReplayTable.InboundRateLimiter);
        Assert.Equal(
            1,
            AnomalyReplayTable.RawInboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
    }

    [Fact]
    public void Receive_dispatch_coalescer_suppresses_only_expected_same_frame_copies()
    {
        var coalescer = new InboundEnvelopeDispatchCoalescer();
        var limiter = new Anomaly.Shared.Networking.AnomalyInboundRateLimiter();
        byte[] first = BuildEnvelopePrefix(0x10);
        byte[] second = BuildEnvelopePrefix(0x20);
        int processed = 0;

        for (int invocation = 0; invocation < 3; invocation++)
        {
            var disposition = coalescer.Observe(100, 1, (byte[])first.Clone());
            if (disposition == InboundEnvelopeDispatchCoalescer.DispatchDisposition.Process)
            {
                processed++;
                Assert.True(limiter.TryAccept(1, first.Length).Allowed);
            }
            else
            {
                Assert.Equal(
                    InboundEnvelopeDispatchCoalescer.DispatchDisposition.IgnoreExpectedDuplicate,
                    disposition);
            }
        }

        Assert.Equal(1, processed);
        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.Process,
            coalescer.Observe(100, 1, second));
        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.ChargeExcessDuplicate,
            coalescer.Observe(100, 1, first));

        Anomaly.Shared.Networking.AnomalyRateLimitResult fourthCopy =
            limiter.RegisterProtocolViolation();
        Assert.Equal(1, fourthCopy.DroppedInWindow);

        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.Process,
            coalescer.Observe(101, 1, first));
    }

    [Fact]
    public void Receive_dispatch_coalescer_reset_starts_a_new_connection_scope()
    {
        var coalescer = new InboundEnvelopeDispatchCoalescer();
        byte[] envelope = BuildEnvelopePrefix(0x33);

        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.Process,
            coalescer.Observe(7, 1, envelope));
        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.IgnoreExpectedDuplicate,
            coalescer.Observe(7, 1, envelope));

        coalescer.Reset();

        Assert.Equal(
            InboundEnvelopeDispatchCoalescer.DispatchDisposition.Process,
            coalescer.Observe(7, 1, envelope));
    }

    private static byte[] BuildEnvelopePrefix(byte nonceSeed)
    {
        var envelope = new byte[64];
        envelope[0] = (byte)'A';
        envelope[1] = (byte)'N';
        envelope[2] = (byte)'2';
        envelope[3] = (byte)'E';
        for (int index = 4; index < 16; index++)
            envelope[index] = unchecked((byte)(nonceSeed + index));
        return envelope;
    }

    [Fact]
    public void HintEventArgumentsSupportTextDurationAndCancellationMutation()
    {
        var args = new HintDisplayingEventArgs("original", 3f)
        {
            Text = "replacement",
            DurationSeconds = 1.5f,
            IsAllowed = false,
        };

        Assert.Equal("replacement", args.Text);
        Assert.Equal(1.5f, args.DurationSeconds);
        Assert.False(args.IsAllowed);
    }

    [Fact]
    public void SchedulerTickUpdatesMultipleCooldownsWithoutInvalidatingEnumeration()
    {
        Scheduler.ClearAll();
        try
        {
            Scheduler.StartCooldown("example.first", TimeSpan.FromSeconds(2));
            Scheduler.StartCooldown("example.second", TimeSpan.FromSeconds(3));

            Scheduler.Tick(0.5);

            Assert.Equal(1.5, Scheduler.GetCooldownRemaining("example.first").TotalSeconds, 6);
            Assert.Equal(2.5, Scheduler.GetCooldownRemaining("example.second").TotalSeconds, 6);
        }
        finally
        {
            Scheduler.ClearAll();
        }
    }

    [Fact]
    public void SchedulerOwnerCleanupDuringCallbackDoesNotCorruptTick()
    {
        Scheduler.ClearAll();
        int callbacks = 0;
        try
        {
            Scheduler.In(TimeSpan.Zero, () =>
            {
                callbacks++;
                Scheduler.ClearOwner("example.module");
            }, "example.module");
            Scheduler.In(TimeSpan.Zero, () => callbacks++, "example.module");

            Scheduler.Tick(0.1);

            Assert.Equal(1, callbacks);
        }
        finally
        {
            Scheduler.ClearAll();
        }
    }

    [Fact]
    public void InputSubscribersAreIsolated()
    {
        var observed = new List<string>();
        Action<string> throwing = _ => throw new InvalidOperationException("expected test fault");
        Action<string> succeeding = observed.Add;
        InputRegistry.Pressed += throwing;
        InputRegistry.Pressed += succeeding;
        try
        {
            InputRegistry.OnPressed("anomaly.test");
        }
        finally
        {
            InputRegistry.Pressed -= throwing;
            InputRegistry.Pressed -= succeeding;
        }

        Assert.Equal(new[] { "anomaly.test" }, observed);
    }

    [Fact]
    public void ClientEventSubscribersAreIsolated()
    {
        int observed = 0;
        Anomaly.Client.Api.Events.AnomalyEventHandler<ClientReadyEventArgs> throwing =
            _ => throw new InvalidOperationException("expected test fault");
        Anomaly.Client.Api.Events.AnomalyEventHandler<ClientReadyEventArgs> succeeding =
            _ => observed++;
        ClientEvents.Ready += throwing;
        ClientEvents.Ready += succeeding;
        try
        {
            ClientEvents.OnReady(new ClientReadyEventArgs("TestScene"));
        }
        finally
        {
            ClientEvents.Ready -= throwing;
            ClientEvents.Ready -= succeeding;
        }

        Assert.Equal(1, observed);
    }

    [Fact]
    public void ActionSubscribersAreIsolated()
    {
        int observedArgument = 0;
        int observedSignal = 0;
        Action<int> argumentHandlers = _ => throw new InvalidOperationException("expected test fault");
        argumentHandlers += value => observedArgument = value;
        Action signalHandlers = () => throw new InvalidOperationException("expected test fault");
        signalHandlers += () => observedSignal++;

        EventInvoker.Invoke(argumentHandlers, 42, "test argument event");
        EventInvoker.Invoke(signalHandlers, "test signal event");

        Assert.Equal(42, observedArgument);
        Assert.Equal(1, observedSignal);
    }

    [Fact]
    public void ComponentInitializationRollsBackAndRetriesIdempotently()
    {
        var initializer = new TransactionalInitializer();
        int first = 0;
        int second = 0;

        Assert.Throws<InvalidOperationException>(() => initializer.Initialize(
            new InitializationStep(() => first++, () => first--),
            new InitializationStep(
                () =>
                {
                    second++;
                    throw new InvalidOperationException("injected failure");
                },
                () => second--)));

        Assert.False(initializer.IsInitialized);
        Assert.Equal(0, first);
        Assert.Equal(0, second);

        initializer.Initialize(
            new InitializationStep(() => first++, () => first--),
            new InitializationStep(() => second++, () => second--));
        initializer.Initialize(
            new InitializationStep(() => first++, () => first--));

        Assert.True(initializer.IsInitialized);
        Assert.Equal(1, first);
        Assert.Equal(1, second);

        initializer.Reset();
        Assert.False(initializer.IsInitialized);
        Assert.Equal(0, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public void Isolated_cleanup_runs_security_resets_after_predecessor_and_logger_failures()
    {
        var observed = new List<string>();

        IsolatedCleanup.Run(
            (_, _) => throw new InvalidOperationException("injected logger failure"),
            new CleanupStep("unity", () => throw new InvalidOperationException("injected cleanup failure")),
            new CleanupStep("file-transfer", () => observed.Add("file-transfer")),
            new CleanupStep("replay", () => observed.Add("replay")),
            new CleanupStep("enrollment", () => observed.Add("enrollment")));

        Assert.Equal(new[] { "file-transfer", "replay", "enrollment" }, observed);
    }

    [Fact]
    public void Prompt_leases_reject_stale_submit_and_cancel_after_owner_or_connection_changes()
    {
        var gate = new PromptLeaseGate();
        PromptLease oldServer = gate.Begin("auth");
        PromptLease newServer = gate.Begin("content");

        Assert.False(gate.TryConsume(oldServer));
        Assert.True(gate.IsCurrent(newServer));
        Assert.False(gate.Invalidate("auth"));
        Assert.True(gate.Invalidate("content"));
        Assert.False(gate.TryConsume(newServer));

        PromptLease reconnect = gate.Begin("auth");
        gate.InvalidateAll();
        Assert.False(gate.TryConsume(reconnect));
    }

    [Fact]
    public void Connection_attempts_only_preserve_state_for_the_expected_single_use_reconnect()
    {
        var gate = new ConnectionAttemptGate();
        int firstAttempt = gate.BeginUserAttempt();

        Assert.True(gate.TryScheduleReconnect("127.0.0.1:7777", firstAttempt));
        Assert.False(gate.TryConsumeExpectedReconnect("127.0.0.1:7778"));
        Assert.False(gate.TryConsumeExpectedReconnect("127.0.0.1:7777"));

        int secondAttempt = gate.BeginUserAttempt();
        Assert.NotEqual(firstAttempt, secondAttempt);
        Assert.True(gate.TryScheduleReconnect("server.example:7777", secondAttempt));
        Assert.True(gate.TryConsumeExpectedReconnect("SERVER.EXAMPLE:7777"));
        Assert.False(gate.TryConsumeExpectedReconnect("server.example:7777"));

        gate.Invalidate();
        Assert.False(gate.TryScheduleReconnect("server.example:7777", secondAttempt));
    }

    [Fact]
    public void Writer_attempts_deduplicate_only_the_same_native_writer_and_endpoint()
    {
        var gate = new ConnectionAttemptGate();

        Assert.Equal(
            ConnectionWriterDisposition.NewUserAttempt,
            gate.BeginWriterAttempt("server.example:7777", 101));
        int firstGeneration = gate.Generation;
        Assert.Equal(
            ConnectionWriterDisposition.Duplicate,
            gate.BeginWriterAttempt("SERVER.EXAMPLE:7777", 101));
        Assert.Equal(firstGeneration, gate.Generation);

        Assert.Equal(
            ConnectionWriterDisposition.NewUserAttempt,
            gate.BeginWriterAttempt("other.example:7777", 202));
        Assert.NotEqual(firstGeneration, gate.Generation);

        int reconnectGeneration = gate.Generation;
        Assert.True(gate.TryScheduleReconnect("other.example:7777", reconnectGeneration));
        gate.CompleteWriterAttempt();
        Assert.Equal(
            ConnectionWriterDisposition.ExpectedReconnect,
            gate.BeginWriterAttempt("OTHER.EXAMPLE:7777", 303));
        Assert.Equal(reconnectGeneration, gate.Generation);

        // A production authentication-body failure invalidates the attempt;
        // retrying the same endpoint/native writer must not remain latched as a duplicate.
        gate.Invalidate();
        Assert.Equal(
            ConnectionWriterDisposition.NewUserAttempt,
            gate.BeginWriterAttempt("other.example:7777", 303));
    }

    [Fact]
    public void Frame_invocation_gate_is_instance_aware_and_resettable()
    {
        var gate = new FrameInvocationGate();

        Assert.True(gate.ShouldFire(101, 9, 42));
        Assert.False(gate.ShouldFire(101, 9, 42));
        Assert.True(gate.ShouldFire(202, 9, 42));
        Assert.True(gate.ShouldFire(101, 10, 42));
        Assert.True(gate.ShouldFire(101, 9, 43));

        gate.Reset();
        Assert.True(gate.ShouldFire(101, 9, 43));
    }

    [Fact]
    public void Remote_state_capacity_is_bounded_per_scope_and_globally_and_releases_capacity()
    {
        var guard = new RemoteStateCapacityGuard<uint, string>(3, 2);

        Assert.True(guard.TryAdd(1, "a"));
        Assert.True(guard.TryAdd(1, "a"));
        Assert.True(guard.TryAdd(1, "b"));
        Assert.False(guard.TryAdd(1, "c"));
        Assert.True(guard.TryAdd(2, "c"));
        Assert.False(guard.TryAdd(2, "d"));
        Assert.Equal(3, guard.Count);

        Assert.True(guard.Remove(1, "a"));
        Assert.True(guard.TryAdd(2, "d"));
        Assert.Equal(2, guard.RemoveScope(2));
        Assert.Equal(1, guard.Count);

        guard.Clear();
        Assert.Equal(0, guard.Count);
        Assert.True(guard.TryAdd(3, "fresh"));
    }

    [Fact]
    public void SchedulerClearOwnerDoesNotCancelOtherOwners()
    {
        int first = 0;
        int second = 0;
        Scheduler.ClearAll();
        try
        {
            Scheduler.In(TimeSpan.Zero, () => first++, "first.mod");
            Scheduler.In(TimeSpan.Zero, () => second++, "second.mod");
            Scheduler.ClearOwner("first.mod");
            Scheduler.Tick(0.01);

            Assert.Equal(0, first);
            Assert.Equal(1, second);
        }
        finally
        {
            Scheduler.ClearAll();
        }
    }

    [Fact]
    public void LifecycleGateDeduplicatesAndAllowsReconnectAndSceneReady()
    {
        var gate = new ClientLifecycleGate();

        Assert.True(gate.TryStart(10));
        Assert.False(gate.TryStart(10));
        Assert.True(gate.TryConnect(10));
        Assert.False(gate.TryConnect(10));
        Assert.True(gate.TryReady(10));
        Assert.False(gate.TryReady(10));

        gate.BeginSceneTransition();
        Assert.True(gate.TryReady(10));
        Assert.True(gate.TryDisconnect(10));
        Assert.False(gate.TryDisconnect(10));

        Assert.True(gate.TryStart(10));
        Assert.True(gate.TryConnect(10));
        Assert.True(gate.TryReady(10));
    }

    [Fact]
    public void LifecycleGateRejectsLateSignalsFromOldInstance()
    {
        var gate = new ClientLifecycleGate();
        Assert.True(gate.TryStart(10));
        Assert.True(gate.TryStart(20));
        Assert.False(gate.TryConnect(10));
        Assert.False(gate.TryDisconnect(10));
        Assert.True(gate.TryConnect(20));
    }

    [Fact]
    public void LifecycleGateResetStartsFreshInstanceScope()
    {
        var gate = new ClientLifecycleGate();
        Assert.True(gate.TryStart(10));
        Assert.True(gate.TryStart(20));
        Assert.False(gate.TryConnect(10));

        gate.Reset();

        Assert.True(gate.TryStart(10));
        Assert.True(gate.TryConnect(10));
        Assert.True(gate.TryReady(10));
    }

    [Fact]
    public void RoundStateGatePublishesFirstSnapshotAndSuppressesOnlyConnectionDuplicates()
    {
        var gate = new RoundStateConnectionGate();

        Assert.True(gate.TryAccept(RoundPhase.WaitingForPlayers, out RoundPhase firstPrevious));
        Assert.Equal(RoundPhase.WaitingForPlayers, firstPrevious);
        Assert.Equal(RoundPhase.WaitingForPlayers, gate.CurrentPhase);
        Assert.False(gate.TryAccept(RoundPhase.WaitingForPlayers, out _));

        Assert.True(gate.TryAccept(RoundPhase.InProgress, out RoundPhase transitionPrevious));
        Assert.Equal(RoundPhase.WaitingForPlayers, transitionPrevious);
        Assert.Equal(RoundPhase.InProgress, gate.CurrentPhase);
        Assert.False(gate.TryAccept(RoundPhase.InProgress, out _));
    }

    [Fact]
    public void RoundStateGateResetClearsStalePhaseAndAllowsSamePhaseOnReconnect()
    {
        var gate = new RoundStateConnectionGate();
        Assert.True(gate.TryAccept(RoundPhase.InProgress, out _));

        gate.Reset();

        Assert.Equal(RoundPhase.WaitingForPlayers, gate.CurrentPhase);
        Assert.True(gate.TryAccept(RoundPhase.WaitingForPlayers, out RoundPhase previous));
        Assert.Equal(RoundPhase.WaitingForPlayers, previous);
        Assert.False(gate.TryAccept(RoundPhase.WaitingForPlayers, out _));
    }
}
