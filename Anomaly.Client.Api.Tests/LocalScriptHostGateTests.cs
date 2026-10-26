using Anomaly.Client.Internal.Hosting;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class LocalScriptHostGateTests
{
    [Fact]
    public void A_reciprocal_local_connection_pair_is_verified_before_any_player_or_hub_exists()
    {
        Assert.True(LocalScriptHostGate.IsVerified(false, true, true,
            new IntPtr(11), new IntPtr(22), new IntPtr(22), new IntPtr(11)));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void Dedicated_offline_or_remote_modes_cannot_use_even_matching_pointer_values(
        bool dedicated, bool serverActive, bool clientActiveHost)
    {
        // No address/loopback claim participates in this proof.
        Assert.False(LocalScriptHostGate.IsVerified(dedicated, serverActive, clientActiveHost,
            new IntPtr(11), new IntPtr(22), new IntPtr(22), new IntPtr(11)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_side_of_the_connection_pair_must_be_present(int missing)
    {
        var pointers = new[] { new IntPtr(11), new IntPtr(22), new IntPtr(22), new IntPtr(11) };
        pointers[missing] = IntPtr.Zero;
        Assert.False(LocalScriptHostGate.IsVerified(false, true, true,
            pointers[0], pointers[1], pointers[2], pointers[3]));
    }

    [Theory]
    [InlineData(23, 11)]
    [InlineData(22, 12)]
    [InlineData(23, 12)]
    public void The_server_and_client_connections_must_be_reciprocally_paired(int pairedClient, int reciprocalServer)
    {
        Assert.False(LocalScriptHostGate.IsVerified(false, true, true,
            new IntPtr(11), new IntPtr(22), new IntPtr(pairedClient), new IntPtr(reciprocalServer)));
    }

    [Fact]
    public void A_previous_pair_does_not_cache_authority_for_a_changed_current_connection()
    {
        Assert.True(LocalScriptHostGate.IsVerified(false, true, true,
            new IntPtr(11), new IntPtr(22), new IntPtr(22), new IntPtr(11)));
        Assert.False(LocalScriptHostGate.IsVerified(false, true, true,
            new IntPtr(11), new IntPtr(23), new IntPtr(22), new IntPtr(11)));
        Assert.True(LocalScriptHostGate.IsVerified(false, true, true,
            new IntPtr(12), new IntPtr(23), new IntPtr(23), new IntPtr(12)));
    }
}
