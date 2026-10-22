using System;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class MessagePayloadBudgetTests
{
    [Fact]
    public void Remote_budget_fits_the_actual_encrypted_envelope_at_the_boundary()
    {
        Assert.True(TransportPacketPolicy.TryGetPayloadBudget(1200, 1200, 2000, true, out var budget));
        var payload = new byte[budget.MaxPayloadBytes + TransportPacketPolicy.AnomalyEnvelopeBytes];
        byte[] envelope = AuthTransportCrypto.Encrypt(new byte[32], new byte[16], new byte[32],
            AuthTransportDirection.ClientToServer, payload);

        Assert.Equal(1095, budget.MaxPayloadBytes);
        Assert.Equal(budget.MaxPayloadBytes, budget.MaxUnfragmentedPayloadBytes);
        Assert.True(TransportPacketPolicy.Fits(1200, envelope.Length));
        Assert.False(TransportPacketPolicy.Fits(1200, envelope.Length + 1));
        Assert.Equal(payload.Length + AuthTransportCrypto.EnvelopeOverhead, envelope.Length);
        // The previous nonce+tag-only estimate incorrectly admitted these four bytes.
        int oversizedPlaintextBytes = payload.Length + AuthTransportCrypto.MarkerSize;
        Assert.True(TransportPacketPolicy.Fits(1200, oversizedPlaintextBytes +
            AuthTransportCrypto.NonceSize + AuthTransportCrypto.TagSize));
        Assert.False(TransportPacketPolicy.Fits(1200, oversizedPlaintextBytes + AuthTransportCrypto.EnvelopeOverhead));
    }

    [Fact]
    public void Local_budget_does_not_reserve_authenticated_encryption()
    {
        Assert.True(TransportPacketPolicy.TryGetPayloadBudget(1200, 1200, 2000, false, out var local));
        Assert.True(TransportPacketPolicy.TryGetPayloadBudget(1200, 1200, 2000, true, out var remote));
        Assert.Equal(AuthTransportCrypto.EnvelopeOverhead, local.MaxPayloadBytes - remote.MaxPayloadBytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(1095, 1095)]
    [InlineData(2000, 1095)]
    public void Contract_ceiling_is_intersected_with_transport(int contractLimit, int expected)
    {
        Assert.True(TransportPacketPolicy.TryGetPayloadBudget(1200, 1200, contractLimit, true, out var budget));
        Assert.Equal(expected, budget.MaxPayloadBytes);
    }

    [Fact]
    public void Reliable_budget_distinguishes_single_packet_and_fragmented_capacity()
    {
        int maximum = TransportPacketPolicy.GetMaximumPacketBytes(1196, true);
        Assert.Equal(1190 * ushort.MaxValue, maximum);
        Assert.True(TransportPacketPolicy.TryGetPayloadBudget(maximum, 1196,
            AnomalyChannel.MaxEnvelopeBytes, true, out var budget));
        Assert.Equal(AnomalyChannel.MaxEnvelopeBytes - TransportPacketPolicy.AnomalyEnvelopeBytes, budget.MaxPayloadBytes);
        Assert.Equal(1091, budget.MaxUnfragmentedPayloadBytes);
        Assert.Equal(1196, TransportPacketPolicy.GetMaximumPacketBytes(1196, false));
        Assert.Equal(int.MaxValue, TransportPacketPolicy.GetMaximumPacketBytes(int.MaxValue, true));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1200, 0, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(1200, 1201, 0)]
    [InlineData(1200, 1200, -1)]
    [InlineData(1200, 1200, int.MaxValue)]
    public void Invalid_or_unusable_limits_fail_without_a_budget(int maximum, int single, int contract)
    {
        Assert.False(TransportPacketPolicy.TryGetPayloadBudget(maximum, single, contract, true, out var budget));
        Assert.Equal(0, budget.MaxPayloadBytes);
        Assert.Equal(0, budget.MaxUnfragmentedPayloadBytes);
    }
}
