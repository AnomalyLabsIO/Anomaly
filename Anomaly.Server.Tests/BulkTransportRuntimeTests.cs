using System.Reflection;
using HarmonyLib;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking;
using LiteNetLib;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class BulkTransportRuntimeTests : IDisposable
{
    private static DeliveryMethod[] Stock => new[] { DeliveryMethod.ReliableOrdered, DeliveryMethod.Unreliable,
        DeliveryMethod.Sequenced, DeliveryMethod.ReliableSequenced, DeliveryMethod.ReliableUnordered,
        DeliveryMethod.ReliableOrdered };

    public BulkTransportRuntimeTests() => Reset();
    public void Dispose() => Reset();

    private static void Reset()
    {
        BulkTransportRuntime.Disable();
        LiteNetLib4MirrorServer.Peers.Clear();
        LiteNetLib4MirrorTransport.Singleton.channels = Stock;
        Transport.active = LiteNetLib4MirrorTransport.Singleton;
        Transport.active.FlushedChannels.Clear();
        LiteNetLib4MirrorCore.Host = new NetManager { ChannelsCount = 6 };
    }

    [Fact]
    public void Startup_appends_sixteen_owned_windows_preserves_existing_entries_and_is_idempotent()
    {
        BulkTransportRuntime.Initialize();
        Assert.True(BulkTransportRuntime.TryGetProfile(out byte version, out ulong mask));
        Assert.Equal(BulkTransferProtocol.ProfileVersion, version);
        Assert.Equal(0xFFFFUL << 6, mask);
        Assert.Equal(Stock, LiteNetLib4MirrorTransport.Singleton.channels.Take(6).ToArray());
        Assert.Equal(22, LiteNetLib4MirrorCore.Host.ChannelsCount);
        BulkTransportRuntime.Initialize();
        Assert.Equal(22, LiteNetLib4MirrorTransport.Singleton.channels.Length);
        BulkTransportRuntime.Disable();
        BulkTransportRuntime.Initialize();
        Assert.Equal(22, LiteNetLib4MirrorTransport.Singleton.channels.Length);
    }

    [Fact]
    public void Existing_extensions_are_preserved_and_not_claimed_by_the_bulk_pool()
    {
        LiteNetLib4MirrorTransport.Singleton.channels = Stock.Concat(new[]
            { DeliveryMethod.Unreliable, DeliveryMethod.ReliableOrdered }).ToArray();
        BulkTransportRuntime.Initialize();
        Assert.True(BulkTransportRuntime.TryGetProfile(out _, out ulong mask));
        Assert.Equal(0xFFFFUL << 8, mask);
        Assert.False(BulkTransportRuntime.IsBulkPhysicalChannel(6));
        Assert.True(BulkTransportRuntime.IsBulkPhysicalChannel(8));
        Assert.Equal(DeliveryMethod.Unreliable, LiteNetLib4MirrorTransport.Singleton.channels[6]);
    }

    [Fact]
    public void Hot_expansion_with_an_existing_native_peer_requires_restart_without_mutating_the_mapping()
    {
        LiteNetLib4MirrorCore.Host.FirstPeer = new Peer();
        Assert.Throws<InvalidOperationException>(BulkTransportRuntime.Initialize);
        Assert.Equal(Stock, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(6, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.False(BulkTransportRuntime.TryGetProfile(out _, out _));
    }

    [Fact]
    public void A_recreated_host_is_configured_before_peer_creation_and_validated_after_native_options()
    {
        BulkTransportRuntime.Initialize();
        LiteNetLib4MirrorCore.Host = new NetManager();
        Assert.False(BulkTransportRuntime.TryGetProfile(out _, out _));
        InvokePatch("OptionsPatch", "Prefix", true);
        LiteNetLib4MirrorCore.Host.ChannelsCount = (byte)LiteNetLib4MirrorTransport.Singleton.channels.Length;
        InvokePatch("OptionsPatch", "Postfix", true);
        Assert.True(BulkTransportRuntime.TryGetProfile(out _, out _));
        Assert.Equal(22, LiteNetLib4MirrorTransport.Singleton.channels.Length);
    }

    [Fact]
    public void Routing_requires_selected_owned_lanes_and_pauses_on_backlog_without_using_control_lane_zero()
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        var peer = new Peer();
        LiteNetLib4MirrorServer.Peers[1] = peer;
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out int channel, out bool blocked));
        Assert.Equal(9, channel);
        Assert.False(blocked);
        peer.QueuedByChannel[9] = 64;
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out channel, out blocked));
        Assert.Equal(9, channel);
        Assert.True(blocked);
        Assert.False(BulkTransportRuntime.TryGetRoute(connection, 1UL << 5, 0, out _, out _));
        Assert.False(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 1, out _, out _));
        LiteNetLib4MirrorTransport.Singleton.channels[9] = DeliveryMethod.Unreliable;
        Assert.False(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
    }

    [Fact]
    public void Capacity_reads_the_matching_peers_effective_MTU_and_full_RTT_and_fails_for_missing_information()
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        Assert.False(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out _, out _));
        var peer = new Peer { Mtu = 1024, RoundTripTime = 100 };
        LiteNetLib4MirrorServer.Peers[1] = peer;
        Assert.True(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out int mtu, out int rtt));
        Assert.Equal(1024, mtu); Assert.Equal(100, rtt);
        peer.RoundTripTime = 0;
        Assert.True(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out _, out rtt));
        Assert.Equal(0, rtt); // The planner applies its explicit fallback.
        peer.Mtu = 0;
        Assert.False(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out _, out _));
        peer.Mtu = 1024; peer.RoundTripTime = -1;
        Assert.False(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out _, out _));
        peer.RoundTripTime = 100; peer.ConnectionState = ConnectionState.Disconnected;
        Assert.False(BulkTransportRuntime.TryGetLinkCapacity(connection, 1UL << 6, out _, out _));
    }

    [Fact]
    public void Batch_threshold_hook_limits_only_owned_bulk_lanes()
    {
        BulkTransportRuntime.Initialize();
        object[] bulk = { LiteNetLib4MirrorTransport.Singleton, 6, int.MaxValue };
        Assert.False((bool)InvokePatch("BatchThresholdPatch", "Prefix", bulk));
        Assert.Equal(BulkTransferProtocol.BatchThresholdBytes, bulk[2]);
        object[] control = { LiteNetLib4MirrorTransport.Singleton, 0, int.MaxValue };
        Assert.True((bool)InvokePatch("BatchThresholdPatch", "Prefix", control));
        Assert.Equal(int.MaxValue, control[2]);
        Assert.NotNull(BulkTransportRuntime.SetOptionsTarget);
        Assert.NotNull(BulkTransportRuntime.BatchThresholdTarget);
        Assert.NotNull(BulkTransportRuntime.SendToTransportTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_active_transport_does_not_expand_or_advertise_the_singleton(bool missing)
    {
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        Transport.active = missing ? null : new AlternateTransport();
        BulkTransportRuntime.Initialize();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(6, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.False(BulkTransportRuntime.TryGetProfile(out _, out ulong mask));
        Assert.Equal(0UL, mask);
        Assert.False(BulkTransportRuntime.IsBulkPhysicalChannel(6));
    }

    [Fact]
    public void Changed_active_transport_revokes_profile_and_cannot_enqueue_on_the_old_pool()
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        LiteNetLib4MirrorServer.Peers[1] = new Peer();
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
        Transport.active = new AlternateTransport();
        Assert.False(BulkTransportRuntime.TryGetProfile(out _, out ulong mask));
        Assert.Equal(0UL, mask);
        Assert.False(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
        Assert.False(BulkTransportRuntime.IsBulkPhysicalChannel(9));
    }

    [Fact]
    public void Delayed_flush_rejects_changed_active_transport_only_for_tracked_bulk_lanes()
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        LiteNetLib4MirrorServer.Peers[1] = new Peer();
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
        Assert.True((bool)InvokePatch("BulkFlushPatch", "Prefix", connection, 9));
        Transport.active = new AlternateTransport();
        Assert.True((bool)InvokePatch("BulkFlushPatch", "Prefix", connection, 0));
        Assert.True((bool)InvokePatch("BulkFlushPatch", "Prefix", connection, 10));
        Assert.True((bool)InvokePatch("BulkFlushPatch", "Prefix", new NetworkConnectionToClient(), 9));
        Assert.False(connection.Disconnected);
        Assert.False((bool)InvokePatch("BulkFlushPatch", "Prefix", connection, 9));
        Assert.True(connection.Disconnected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delayed_flush_rejects_host_recreation_even_if_the_replacement_has_a_valid_pool(bool prepareReplacement)
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        LiteNetLib4MirrorServer.Peers[1] = new Peer();
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
        LiteNetLib4MirrorCore.Host = new NetManager();
        if (prepareReplacement)
        {
            InvokePatch("OptionsPatch", "Prefix", true);
            LiteNetLib4MirrorCore.Host.ChannelsCount = 22;
            InvokePatch("OptionsPatch", "Postfix", true);
        }
        Assert.Equal(prepareReplacement, BulkTransportRuntime.TryGetProfile(out _, out _));
        Assert.False((bool)InvokePatch("BulkFlushPatch", "Prefix", connection, 9));
        Assert.True(connection.Disconnected);
    }

    [Fact]
    public void Installed_harmony_guard_blocks_the_real_deferred_call_boundary_after_an_active_transport_change()
    {
        BulkTransportRuntime.Initialize();
        var connection = new NetworkConnectionToClient { connectionId = 1 };
        LiteNetLib4MirrorServer.Peers[1] = new Peer();
        Assert.True(BulkTransportRuntime.TryGetRoute(connection, 1UL << 9, 0, out _, out _));
        var patch = typeof(BulkTransportRuntime).GetNestedType("BulkFlushPatch", BindingFlags.NonPublic)
            .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        var harmony = new Harmony("anomaly.tests.bulk.flushguard");
        try
        {
            harmony.Patch(BulkTransportRuntime.SendToTransportTarget, prefix: new HarmonyMethod(patch));
            connection.FlushBatch(default, 9);
            Assert.Equal(new[] { 9 }, Transport.active.FlushedChannels);
            var alternate = new AlternateTransport();
            Transport.active = alternate;
            connection.FlushBatch(default, 9);
            Assert.Empty(alternate.FlushedChannels);
            Assert.True(connection.Disconnected);
            // This same connection's unrelated extension lane remains outside the guard.
            connection.FlushBatch(default, 10);
            Assert.Equal(new[] { 10 }, alternate.FlushedChannels);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private sealed class AlternateTransport : Transport { }

    private static object InvokePatch(string typeName, string method, params object[] args) =>
        typeof(BulkTransportRuntime).GetNestedType(typeName, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
}
