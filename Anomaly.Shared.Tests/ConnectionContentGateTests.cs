using System;
using System.IO;
using System.Linq;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ConnectionContentGateTests
{
    private static byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();

    [Fact]
    public void Script_ownership_requires_the_same_offer_even_when_transport_and_hashes_are_reused()
    {
        var gate = new ConnectionContentGate();
        gate.Begin(10);
        gate.BindOffer(1, Hash(1), Hash(2));
        Assert.True(gate.OwnsOffer(1, Hash(1), Hash(2)));
        Assert.False(gate.OwnsOffer(1, Hash(3), Hash(2)));
        gate.Begin(11, preserveDeadline: true);
        gate.BindOffer(2, Hash(1), Hash(2));
        Assert.False(gate.OwnsOffer(1, Hash(1), Hash(2)));
        Assert.True(gate.OwnsOffer(2, Hash(1), Hash(2)));
        gate.Fail();
        Assert.False(gate.OwnsOffer(2, Hash(1), Hash(2)));
    }

    [Fact]
    public void Every_phase_and_the_exact_server_confirmation_are_required()
    {
        var gate = new ConnectionContentGate();
        gate.Begin(10);
        gate.BindOffer(1, Hash(1), Hash(2));
        Assert.False(gate.IsReady);
        Assert.Throws<InvalidOperationException>(() => gate.Loaded(1, Hash(1), Hash(2), true));
        Assert.Throws<InvalidOperationException>(() => gate.Confirm(1, Hash(1), Hash(2)));
        gate.Consent(1, Hash(1), Hash(2));
        Assert.Throws<InvalidOperationException>(() => gate.Loaded(1, Hash(1), Hash(2), false));
        Assert.False(gate.IsReady);
        gate.Loaded(1, Hash(1), Hash(2), true);
        Assert.False(gate.IsReady);
        gate.Confirm(1, Hash(1), Hash(2));
        Assert.True(gate.IsReady);
        gate.Fail();
        Assert.False(gate.IsReady);
        Assert.Throws<InvalidOperationException>(() => gate.Confirm(1, Hash(1), Hash(2)));
    }

    [Fact]
    public void Duplicate_offers_do_not_extend_the_absolute_consent_and_loading_deadline()
    {
        var gate = new ConnectionContentGate();
        gate.Begin(10);
        gate.BindOffer(1, Hash(1), Hash(2));
        gate.BindOffer(1, Hash(1), Hash(2));
        gate.Consent(1, Hash(1), Hash(2));
        gate.Begin(100, preserveDeadline: true);
        gate.BindOffer(2, Hash(1), Hash(2));
        Assert.False(gate.HasTimedOut(129.999));
        Assert.True(gate.HasTimedOut(130));
        Assert.True(gate.HasTimedOut(double.NaN));
    }

    [Fact]
    public void Reconnect_reset_rejects_previous_session_even_for_identical_source_hashes()
    {
        var gate = new ConnectionContentGate();
        gate.Begin(0);
        var catalog = Hash(1);
        gate.BindOffer(1, catalog, Hash(2));
        catalog[0] = 9; // Offer state owns its hash bytes.
        gate.Consent(1, Hash(1), Hash(2));
        gate.Reset();
        gate.Begin(1);
        gate.BindOffer(2, Hash(1), Hash(2));
        Assert.Throws<InvalidDataException>(() => gate.Confirm(1, Hash(1), Hash(2)));
        Assert.Throws<InvalidDataException>(() => gate.BindOffer(2, Hash(9), Hash(2)));
        Assert.False(gate.IsReady);
    }

    [Fact]
    public void Bootstrap_contract_has_bounded_exact_fields_and_closed_directional_allowlists()
    {
        var message = new ContentBootstrapReadyMessage { SessionId = 9, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        message.Serialize(writer);
        Assert.Equal(72, stream.Length);
        stream.Position = 0;
        var copy = new ContentBootstrapReadyMessage();
        copy.Deserialize(new BinaryReader(stream));
        Assert.Equal(9UL, copy.SessionId);
        Assert.Equal(message.CatalogSha256, copy.CatalogSha256);
        Assert.True(ContentBootstrapPolicy.IsServerMessage(message));
        Assert.False(ContentBootstrapPolicy.IsClientMessage(message));
        Assert.True(ContentBootstrapPolicy.IsClientMessage(new FileRequestMessage()));
        Assert.False(ContentBootstrapPolicy.IsServerMessage(new FileRequestMessage()));
        Assert.False(ContentBootstrapPolicy.IsServerMessage(new DerivedCatalog()));
        Assert.False(ContentBootstrapPolicy.IsServerMessage(null));
        Assert.Throws<InvalidDataException>(() => new ContentBootstrapReadyMessage().Serialize(writer));
        Assert.Throws<EndOfStreamException>(() => new ContentBootstrapReadyMessage().Deserialize(new BinaryReader(new MemoryStream(new byte[71]))));
    }

    private sealed class DerivedCatalog : ContentCatalogMessage { }
}
