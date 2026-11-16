using Anomaly.Client.Api.Networking;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Networking;
using Anomaly.Client.Patches.Mirror;
using Anomaly.Shared.Networking;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLiteNetLib;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using Xunit;

namespace Anomaly.Client.Patches.Tests;

public sealed class BulkTransportRuntimeTests : IDisposable
{
    private static DeliveryMethod[] Stock => new[] { DeliveryMethod.ReliableOrdered, DeliveryMethod.Unreliable,
        DeliveryMethod.Sequenced, DeliveryMethod.ReliableSequenced, DeliveryMethod.ReliableUnordered,
        DeliveryMethod.ReliableOrdered };

    public BulkTransportRuntimeTests()
    {
        LiteNetLib4MirrorTransport.Singleton = new LiteNetLib4MirrorTransport { channels = Table(Stock) };
        Transport.active = LiteNetLib4MirrorTransport.Singleton;
        LiteNetLib4MirrorCore.Host = new NetManager();
        LiteNetLib4MirrorCore.OptionsCalls = 0;
        BulkTransportRuntime.Unavailable(new InvalidOperationException("test reset"));
        AnomalyMessaging.Failures.Clear();
        AnomalyMessaging.HasSession = true;
    }

    public void Dispose()
    {
        BulkTransportRuntime.Unavailable(new InvalidOperationException("test cleanup"));
        LiteNetLib4MirrorCore.Host = null;
        LiteNetLib4MirrorTransport.Singleton = null;
        Transport.active = null;
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Constructor_and_serialized_stock_tables_preserve_control_lanes_and_append_after_reserved_five(int existingCount)
    {
        var transport = LiteNetLib4MirrorTransport.Singleton;
        transport.channels = Table(Stock.Take(existingCount));
        BulkTransportOptionsPatch.Prefix();

        Assert.Equal(22, transport.channels.Length);
        Assert.Equal(Stock, transport.channels.Take(6));
        Assert.All(transport.channels.Skip(6), method => Assert.Equal(DeliveryMethod.ReliableUnordered, method));
        Assert.Equal(1, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));

        LiteNetLib4MirrorCore.SetOptions(false);
        BulkTransportOptionsPatch.Postfix();
        Assert.Equal(22, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(BulkTransferProtocol.ProfileVersion, FileTransferClient.ProfileVersion);
        Assert.Equal(0xFFFFUL << 6, FileTransferClient.LaneMask);
        Assert.True(BulkTransportRuntime.IsOwnedChannel(6));
    }

    [Fact]
    public void Existing_third_party_extensions_are_preserved_and_not_claimed()
    {
        var existing = Stock.Concat(new[] { DeliveryMethod.Unreliable, DeliveryMethod.ReliableOrdered }).ToArray();
        LiteNetLib4MirrorTransport.Singleton.channels = Table(existing);
        RunOptions();
        Assert.Equal(existing, LiteNetLib4MirrorTransport.Singleton.channels.Take(existing.Length));
        Assert.Equal(0xFFFFUL << 8, FileTransferClient.LaneMask);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        Assert.False(BulkTransportRuntime.IsOwnedChannel(7));
        Assert.True(BulkTransportRuntime.IsOwnedChannel(8));
    }

    [Fact]
    public void Recreated_host_reuses_intact_pool_before_stock_options_sets_its_count()
    {
        RunOptions();
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        LiteNetLib4MirrorCore.Host = new NetManager();
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));

        BulkTransportOptionsPatch.Prefix();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(1, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        LiteNetLib4MirrorCore.SetOptions(false);
        BulkTransportOptionsPatch.Postfix();

        Assert.Equal(22, table.Length);
        Assert.True(BulkTransportRuntime.IsOwnedChannel(6));
        RunOptions();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
    }

    [Fact]
    public void Temporary_unavailability_does_not_forget_an_intact_owned_pool()
    {
        RunOptions();
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        BulkTransportRuntime.Unavailable(new InvalidOperationException("host stopped"));
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        LiteNetLib4MirrorCore.Host = new NetManager();
        RunOptions();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(0xFFFFUL << 6, FileTransferClient.LaneMask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unprepared_live_peer_rejects_options_without_mutating_table_or_peer_capacity(bool previousPool)
    {
        if (previousPool) RunOptions();
        LiteNetLib4MirrorCore.Host = new NetManager { FirstPeer = new NetPeer(), ChannelsCount = 6 };
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        int calls = LiteNetLib4MirrorCore.OptionsCalls;

        Assert.Throws<InvalidOperationException>(RunOptions);
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(6, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(calls, LiteNetLib4MirrorCore.OptionsCalls);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(AnomalyMessaging.HasSession);
        Assert.Contains("Reconnect", Assert.Single(AnomalyMessaging.Failures));
    }

    [Fact]
    public void An_already_prepared_live_host_can_reapply_unchanged_options()
    {
        RunOptions();
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        LiteNetLib4MirrorCore.Host.FirstPeer = new NetPeer();
        RunOptions();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(0xFFFFUL << 6, FileTransferClient.LaneMask);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void Any_mapping_tamper_revokes_routing_even_outside_owned_lanes(int channel)
    {
        LiteNetLib4MirrorTransport.Singleton.channels = Table(Stock.Concat(new[] { DeliveryMethod.Unreliable, DeliveryMethod.Sequenced }));
        RunOptions();
        LiteNetLib4MirrorCore.Host.FirstPeer = new NetPeer();
        LiteNetLib4MirrorTransport.Singleton.channels[channel] = DeliveryMethod.ReliableSequenced;
        Assert.False(BulkTransportRuntime.IsOwnedChannel(8));
        Assert.False(AnomalyMessaging.IsBulkPhysicalChannel(8));
        Assert.Throws<InvalidOperationException>(RunOptions);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
    }

    [Fact]
    public void Identical_table_replacement_preserves_pool_but_a_different_mapping_invalidates_it()
    {
        RunOptions();
        var replacement = Table(LiteNetLib4MirrorTransport.Singleton.channels);
        LiteNetLib4MirrorTransport.Singleton.channels = replacement;
        Assert.True(BulkTransportRuntime.IsOwnedChannel(6));
        RunOptions();
        Assert.Same(replacement, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(22, replacement.Length);

        LiteNetLib4MirrorTransport.Singleton.channels = Table(Stock);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        RunOptions();
        Assert.Equal(22, LiteNetLib4MirrorTransport.Singleton.channels.Length);
        Assert.True(BulkTransportRuntime.IsOwnedChannel(6));
    }

    [Fact]
    public void Host_count_or_identity_change_revokes_routing_until_options_complete()
    {
        RunOptions();
        LiteNetLib4MirrorCore.Host.ChannelsCount = 6;
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        LiteNetLib4MirrorCore.Host = new NetManager { ChannelsCount = 22 };
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        RunOptions();
        Assert.True(BulkTransportRuntime.IsOwnedChannel(6));
    }

    [Fact]
    public void Postfix_rejects_native_capacity_mismatch_and_revokes_profile()
    {
        BulkTransportOptionsPatch.Prefix();
        Assert.Throws<InvalidOperationException>(BulkTransportOptionsPatch.Postfix);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        Assert.False(AnomalyMessaging.HasSession);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Postfix_rejects_host_or_mapping_changes_between_preparation_and_native_completion(bool replaceHost)
    {
        BulkTransportOptionsPatch.Prefix();
        LiteNetLib4MirrorCore.SetOptions(false);
        if (replaceHost)
            LiteNetLib4MirrorCore.Host = new NetManager { ChannelsCount = 22 };
        else
            LiteNetLib4MirrorTransport.Singleton.channels[5] = DeliveryMethod.Unreliable;

        Assert.Throws<InvalidOperationException>(BulkTransportOptionsPatch.Postfix);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        Assert.False(AnomalyMessaging.HasSession);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    [InlineData(65)]
    public void Invalid_fresh_table_sizes_are_rejected_without_mutation(int count)
    {
        var table = Table(Stock.Concat(Enumerable.Repeat(DeliveryMethod.ReliableOrdered, 65)).Take(count));
        LiteNetLib4MirrorTransport.Singleton.channels = table;
        Assert.Throws<InvalidOperationException>(RunOptions);
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(0, LiteNetLib4MirrorCore.OptionsCalls);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Stock_delivery_methods_must_match_the_game_contract(int index)
    {
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        table[index] = table[index] == DeliveryMethod.Unreliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
        Assert.Throws<InvalidOperationException>(RunOptions);
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(0, LiteNetLib4MirrorCore.OptionsCalls);
    }

    [Fact]
    public void Unknown_extension_delivery_method_is_rejected()
    {
        LiteNetLib4MirrorTransport.Singleton.channels = Table(Stock.Concat(new[] { (DeliveryMethod)255 }));
        Assert.Throws<InvalidOperationException>(RunOptions);
        Assert.Equal(7, LiteNetLib4MirrorTransport.Singleton.channels.Length);
    }

    [Theory]
    [InlineData(48, 16)]
    [InlineData(60, 4)]
    [InlineData(63, 1)]
    public void Remaining_capacity_can_reach_exactly_sixty_four_and_be_reused(int first, int count)
    {
        LiteNetLib4MirrorTransport.Singleton.channels = Table(Stock.Concat(Enumerable.Repeat(DeliveryMethod.ReliableOrdered, first - 6)));
        RunOptions();
        Assert.Equal(64, LiteNetLib4MirrorTransport.Singleton.channels.Length);
        Assert.Equal(64, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(count, BulkTransferProtocol.DomainCount(FileTransferClient.LaneMask));
        Assert.True(BulkTransportRuntime.IsOwnedChannel(63));
        Assert.False(BulkTransportRuntime.IsOwnedChannel(64));
        Assert.False(BulkTransportRuntime.IsOwnedChannel(-1));
        LiteNetLib4MirrorCore.Host = new NetManager();
        RunOptions();
        Assert.Equal(64, LiteNetLib4MirrorTransport.Singleton.channels.Length);
    }

    [Fact]
    public void Batch_threshold_changes_only_owned_lanes_on_the_prepared_transport()
    {
        RunOptions();
        int result = int.MaxValue;
        Assert.False(BulkBatchThresholdPatch.Prefix(LiteNetLib4MirrorTransport.Singleton, 6, ref result));
        Assert.Equal(BulkTransferProtocol.BatchThresholdBytes, result);
        foreach (int channel in new[] { -1, 0, 4, 5, 22, 64 })
        {
            result = int.MaxValue;
            Assert.True(BulkBatchThresholdPatch.Prefix(LiteNetLib4MirrorTransport.Singleton, channel, ref result));
            Assert.Equal(int.MaxValue, result);
        }
        result = int.MaxValue;
        Assert.True(BulkBatchThresholdPatch.Prefix(new Transport(), 6, ref result));
        Assert.Equal(int.MaxValue, result);
        LiteNetLib4MirrorCore.Host = new NetManager();
        Assert.True(BulkBatchThresholdPatch.Prefix(LiteNetLib4MirrorTransport.Singleton, 6, ref result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unsupported_active_transport_does_not_change_or_advertise_the_singleton_pool(bool missing)
    {
        var table = LiteNetLib4MirrorTransport.Singleton.channels;
        Transport.active = missing ? null : new Transport();
        RunOptions();
        Assert.Same(table, LiteNetLib4MirrorTransport.Singleton.channels);
        Assert.Equal(6, LiteNetLib4MirrorCore.Host.ChannelsCount);
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(FileTransferClient.TransportAvailable());
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        Assert.True(AnomalyMessaging.HasSession);
    }

    [Fact]
    public void Active_transport_change_revokes_cached_profile_validation_and_batch_ownership()
    {
        RunOptions();
        Assert.True(FileTransferClient.TransportAvailable());
        Transport.active = new Transport();
        Assert.False(FileTransferClient.TransportAvailable());
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
        int result = int.MaxValue;
        Assert.True(BulkBatchThresholdPatch.Prefix(LiteNetLib4MirrorTransport.Singleton, 6, ref result));
        Assert.Equal(int.MaxValue, result);
        RunOptions();
        Assert.Equal(0UL, FileTransferClient.LaneMask);
    }

    [Fact]
    public void Active_transport_change_during_options_keeps_the_pool_unadvertised()
    {
        BulkTransportOptionsPatch.Prefix();
        LiteNetLib4MirrorCore.SetOptions(false);
        Transport.active = new Transport();
        BulkTransportOptionsPatch.Postfix();
        Assert.Equal(0UL, FileTransferClient.LaneMask);
        Assert.False(FileTransferClient.TransportAvailable());
        Assert.False(BulkTransportRuntime.IsOwnedChannel(6));
    }

    private static Il2CppStructArray<DeliveryMethod> Table(IEnumerable<DeliveryMethod> values) => new(values.ToArray());
    private static void RunOptions()
    {
        BulkTransportOptionsPatch.Prefix();
        LiteNetLib4MirrorCore.SetOptions(false);
        BulkTransportOptionsPatch.Postfix();
    }
}
