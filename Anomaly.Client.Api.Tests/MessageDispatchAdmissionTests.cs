using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class MessageDispatchAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Revoked_session_stops_later_internal_handlers_and_publication(bool revoke)
    {
        string name = "tests:dispatch:" + Guid.NewGuid().ToString("N");
        AnomalyMessageRegistry.Register(name, () => new ServerNotice(name));
        AnomalyReplayTable.Reset();
        AnomalyMessaging.ConfigureTransportSession(
            new byte[32], new byte[32],
            new byte[AuthTransportCrypto.SessionIdSize],
            new byte[AuthTransportCrypto.TranscriptHashSize]);

        int firstCalls = 0;
        int laterCalls = 0;
        int publicCalls = 0;
        void First(IAnomalyMessage _)
        {
            firstCalls++;
            if (revoke)
                AnomalyMessaging.ClearTransportSession();
        }
        void Later(IAnomalyMessage _) => laterCalls++;
        void Published(AnomalyMessageReceivedEventArgs _) => publicCalls++;

        AnomalyMessaging.MessageReceived += First;
        AnomalyMessaging.MessageReceived += Later;
        NetworkMessageEvents.AnomalyMessageReceived += Published;
        try
        {
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload);
            writer.Write(1u);
            writer.Write(AnomalyMessageRegistry.GetHash(name));
            AnomalyMessaging.OnReceived(payload.ToArray(), encryptedAndAuthenticated: true, localTransport: false);

            Assert.Equal(1, firstCalls);
            Assert.Equal(revoke ? 0 : 1, laterCalls);
            Assert.Equal(revoke ? 0 : 1, publicCalls);
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= First;
            AnomalyMessaging.MessageReceived -= Later;
            NetworkMessageEvents.AnomalyMessageReceived -= Published;
            AnomalyMessaging.ClearTransportSession();
            AnomalyReplayTable.Reset();
        }
    }

    private sealed class ServerNotice : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        internal ServerNotice(string name) => MessageName = name;
        public string MessageName { get; }
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
}
