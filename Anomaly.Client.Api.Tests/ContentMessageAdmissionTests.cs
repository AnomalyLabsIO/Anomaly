using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class ContentMessageAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnorderedVocabularyRequiresContentAdmissionInsteadOfAPlayerObject(bool admitted)
    {
        Begin();
        int published = 0;
        void Received(IAnomalyMessage message)
        {
            Assert.IsType<AnnouncerVocabularyMessage>(message);
            published++;
        }
        AnomalyMessaging.MessageReceived += Received;
        try
        {
            var message = new AnnouncerVocabularyMessage();
            Assert.Equal(MessageChannel.ReliableUnordered, message.TransportChannel);
            Receive(message, beforeContentReady: !admitted);
            Assert.Equal(admitted ? 1 : 0, published);
            Assert.Equal(admitted, AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= Received;
            End();
        }
    }

    [Fact]
    public void ExactBootstrapConfirmationStillRoutesBeforeContentAdmission()
    {
        Begin();
        var previous = AnomalyMessaging.ContentBootstrapMessageReceived;
        int delivered = 0;
        AnomalyMessaging.ContentBootstrapMessageReceived = message =>
        {
            Assert.Equal(7ul, Assert.IsType<ContentBootstrapReadyMessage>(message).SessionId);
            delivered++;
        };
        try
        {
            Receive(new ContentBootstrapReadyMessage { SessionId = 7 }, beforeContentReady: true);
            Assert.Equal(1, delivered);
            Assert.True(AnomalyMessaging.TryEncryptClientEnvelope(new byte[] { 1 }, out _));
        }
        finally
        {
            AnomalyMessaging.ContentBootstrapMessageReceived = previous;
            End();
        }
    }

    private static void Receive(IAnomalyMessage message, bool beforeContentReady)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1u);
        writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName));
        message.Serialize(writer);
        writer.Flush();
        AnomalyMessaging.OnReceived(stream.ToArray(), encryptedAndAuthenticated: true,
            localTransport: false, beforeContentReady: beforeContentReady);
    }

    private static void Begin()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        AnomalyReplayTable.Reset();
        AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
    }

    private static void End()
    {
        AnomalyMessaging.ClearTransportSession();
        AnomalyReplayTable.Reset();
    }
}
