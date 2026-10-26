using Anomaly.Client.Api.Networking;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class TransportRekeyTests
{
    [Fact]
    public void Failed_connection_cleanup_leaves_neither_previous_nor_replacement_crypto_usable()
    {
        var previous = AnomalyMessaging.TransportSessionRevoked;
        AnomalyMessaging.TransportSessionRevoked = null;
        try
        {
            AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
            Assert.True(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
            long previousGeneration = AnomalyMessaging.TransportGeneration;
            long observedGeneration = previousGeneration;
            AnomalyMessaging.TransportSessionRevoked = () =>
            {
                observedGeneration = AnomalyMessaging.TransportGeneration;
                throw new InvalidOperationException("injected cleanup failure");
            };
            Assert.Throws<InvalidOperationException>(() => AnomalyMessaging.ConfigureTransportSession(
                new byte[32], new byte[32], new byte[16], new byte[32]));
            Assert.False(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
            Assert.True(observedGeneration > previousGeneration);
            Assert.Equal(observedGeneration, AnomalyMessaging.TransportGeneration);
        }
        finally
        {
            AnomalyMessaging.TransportSessionRevoked = null;
            AnomalyMessaging.ClearTransportSession();
            AnomalyMessaging.TransportSessionRevoked = previous;
        }
    }

    [Fact]
    public void Every_clear_and_replacement_advances_the_local_crypto_lifetime()
    {
        var previous = AnomalyMessaging.TransportSessionRevoked;
        AnomalyMessaging.TransportSessionRevoked = null;
        try
        {
            long initial = AnomalyMessaging.TransportGeneration;
            AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
            long first = AnomalyMessaging.TransportGeneration;
            Assert.True(first > initial);
            AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
            long replacement = AnomalyMessaging.TransportGeneration;
            Assert.True(replacement > first);
            Assert.True(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
            AnomalyMessaging.ClearTransportSession();
            Assert.True(AnomalyMessaging.TransportGeneration > replacement);
            Assert.False(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
        }
        finally
        {
            AnomalyMessaging.TransportSessionRevoked = null;
            AnomalyMessaging.ClearTransportSession();
            AnomalyMessaging.TransportSessionRevoked = previous;
        }
    }

    [Fact]
    public void Clear_returns_the_erased_lifetime_even_when_an_observer_rekeys_reentrantly()
    {
        var previous = AnomalyMessaging.TransportSessionRevoked;
        AnomalyMessaging.TransportSessionRevoked = null;
        try
        {
            AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
            AnomalyMessaging.TransportSessionRevoked = () =>
            {
                AnomalyMessaging.TransportSessionRevoked = null;
                AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
            };
            long erasedGeneration = AnomalyMessaging.ClearTransportSession();
            Assert.True(AnomalyMessaging.TransportGeneration > erasedGeneration);
            Assert.True(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
        }
        finally
        {
            AnomalyMessaging.TransportSessionRevoked = null;
            AnomalyMessaging.ClearTransportSession();
            AnomalyMessaging.TransportSessionRevoked = previous;
        }
    }
}
