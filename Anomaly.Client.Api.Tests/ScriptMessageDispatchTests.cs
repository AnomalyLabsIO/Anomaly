using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class ScriptMessageDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Script_carriers_require_the_connection_bridge_before_publication(bool allowed)
    {
        AnomalyMessageRegistry.RegisterDefaults();
        AnomalyReplayTable.Reset();
        AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32],
            new byte[AuthTransportCrypto.SessionIdSize], new byte[AuthTransportCrypto.TranscriptHashSize]);
        var previous = AnomalyMessaging.ScriptMessageReceived;
        int routed = 0, published = 0;
        AnomalyMessaging.ScriptMessageReceived = _ => { routed++; return allowed; };
        void Published(IAnomalyMessage _) => published++;
        AnomalyMessaging.MessageReceived += Published;
        try
        {
            var data = ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["Enabled"] = true });
            var message = ScriptToClientMessage.Create("example:pack", "State", data).Bind(new string('a', 64), 7);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(1u);
            writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName));
            message.Serialize(writer);
            writer.Flush();
            AnomalyMessaging.OnReceived(stream.ToArray(), encryptedAndAuthenticated: true, localTransport: false);
            Assert.Equal(1, routed);
            Assert.Equal(allowed ? 1 : 0, published);
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= Published;
            AnomalyMessaging.ScriptMessageReceived = previous;
            AnomalyMessaging.ClearTransportSession();
            AnomalyReplayTable.Reset();
        }
    }
}
