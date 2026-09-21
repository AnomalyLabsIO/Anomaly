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
    public void Missing_native_host_drops_observation_without_reusing_it_later()
    {
        var gate = new TransportDisconnectGate();
        gate.Begin(1);
        gate.Observe(1, 100, 100, true, "Timeout");
        Assert.False(gate.TryConsume(1, 0, out _));
        Assert.False(gate.TryConsume(1, 100, out _));
    }
}
