using Anomaly.Client.Api.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class TransportDisconnectGateTests
{
    [Theory]
    [InlineData("ConnectionFailed")]
    [InlineData("Timeout")]
    [InlineData("HostUnreachable")]
    [InlineData("NetworkUnreachable")]
    public void Current_host_failure_is_consumed_once(string reason)
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, true, reason);
        Assert.True(gate.TryConsume(1, 100, out var observed));
        Assert.Equal(reason, observed);
        Assert.False(gate.TryConsume(1, 100, out _));
    }

    [Fact]
    public void Local_stop_suppresses_a_queued_failure_and_later_callbacks()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, true, "Timeout");
        gate.Cancel(1);
        Assert.False(gate.TryConsume(1, 100, out _));
        gate.Observe(1, 100, 100, true, "Timeout");
        Assert.False(gate.TryConsume(1, 100, out _));
        gate.Begin(2);
        gate.Observe(2, 200, 200, true, "ConnectionFailed");
        Assert.True(gate.TryConsume(2, 200, out _));
    }

    [Fact]
    public void New_attempt_and_replaced_host_cannot_receive_stale_failure()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, true, "Timeout");
        gate.Begin(2);
        gate.Observe(1, 200, 100, true, "Timeout");
        Assert.False(gate.TryConsume(2, 200, out _));
        gate.Observe(2, 200, 100, true, "Timeout");
        Assert.False(gate.TryConsume(2, 200, out _));
        gate.Observe(2, 200, 200, true, "Timeout");
        Assert.False(gate.TryConsume(2, 300, out _));
    }

    [Fact]
    public void Handshake_rejection_and_deliberate_close_do_not_become_failures()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, false, "ConnectionRejected");
        Assert.False(gate.TryConsume(1, 100, out _));
        gate.Cancel(1);
        gate.Begin(1); // Anomaly's expected second phase uses the same generation.
        gate.Observe(1, 200, 200, false, "DisconnectPeerCalled");
        Assert.False(gate.TryConsume(1, 200, out _));
        gate.Observe(1, 200, 200, true, "Timeout");
        Assert.True(gate.TryConsume(1, 200, out _));
    }

    [Fact]
    public void Missing_native_host_cannot_drain_a_valid_notice()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, true, "Timeout");
        Assert.False(gate.TryConsume(1, 0, out _));
        Assert.True(gate.TryConsume(1, 100, out _));
        Assert.False(gate.TryConsume(1, 100, out _));
    }

    [Fact]
    public void Remote_server_close_hands_detached_reason_to_main_thread_once()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        var frame = StockDisconnectReasonTests.Frame("Available upload capacity is exhausted.");
        gate.ObserveServerClose(1, 100, 100, frame);
        Array.Clear(frame); // The pooled/native source may be recycled after the callback.
        Assert.True(gate.TryConsumeNotice(1, 100, out var notice));
        Assert.Equal(TransportDisconnectGate.Kind.ServerClosed, notice.Outcome);
        Assert.Equal("Available upload capacity is exhausted.", notice.Reason);
        gate.ObserveServerClose(1, 100, 100, StockDisconnectReasonTests.Frame("Duplicate callback"));
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
    }

    [Fact]
    public void Malformed_server_payload_never_becomes_network_timeout_or_reuses_a_reason()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.ObserveServerClose(1, 100, 100, new byte[] { 5, 0, 37, 37, 37, 37 });
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
        gate.ObserveServerClose(1, 100, 100, new byte[1025]);
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
        gate.Observe(1, 100, 100, true, "Timeout");
        Assert.True(gate.TryConsumeNotice(1, 100, out var failure));
        Assert.Equal(TransportDisconnectGate.Kind.NetworkFailure, failure.Outcome);
        Assert.Equal("Timeout", failure.Reason);
    }

    [Fact]
    public void Explicit_local_stop_cancels_remote_reasons_including_later_native_callback()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        var frame = StockDisconnectReasonTests.Frame("Server closed.");
        gate.ObserveServerClose(1, 100, 100, frame);
        gate.Cancel(1);
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
        gate.ObserveServerClose(1, 100, 100, frame);
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
    }

    [Fact]
    public void Retired_attempt_and_host_cannot_publish_or_drain_current_server_notice()
    {
        var gate = new TransportDisconnectGate();
        var old = StockDisconnectReasonTests.Frame("Old server.");
        var current = StockDisconnectReasonTests.Frame("Current server.");
        gate.Begin(1);
        gate.ObserveServerClose(1, 100, 100, old);
        gate.Begin(2);
        gate.ObserveServerClose(1, 200, 100, old);
        gate.ObserveServerClose(2, 200, 100, old);
        Assert.False(gate.TryConsumeNotice(2, 200, out _));
        gate.ObserveServerClose(2, 200, 200, current);
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
        Assert.True(gate.TryConsumeNotice(2, 200, out var notice));
        Assert.Equal("Current server.", notice.Reason);
    }

    [Fact]
    public void New_native_host_in_same_handshake_generation_cannot_inherit_previous_host_notice()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.ObserveServerClose(1, 100, 100, StockDisconnectReasonTests.Frame("Old host."));
        Assert.False(gate.TryConsumeNotice(1, 200, out _));
        gate.ObserveServerClose(1, 200, 200, StockDisconnectReasonTests.Frame("New host."));
        Assert.True(gate.TryConsumeNotice(1, 200, out var notice));
        Assert.Equal("New host.", notice.Reason);
    }

    [Fact]
    public void Same_generation_old_host_consumer_cannot_drain_replacement_host_notice()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.ObserveServerClose(1, 100, 100, StockDisconnectReasonTests.Frame("Retired host."));
        gate.ObserveServerClose(1, 200, 200, StockDisconnectReasonTests.Frame("Live host."));
        Assert.False(gate.TryConsumeNotice(1, 100, out _));
        Assert.True(gate.TryConsumeNotice(1, 200, out var notice));
        Assert.Equal("Live host.", notice.Reason);
    }
}
