using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Client.Api.Tests;

[Collection("Registration lifecycle")]
public sealed class BulkSessionAdmissionTests
{
    [Fact]
    public void Retired_or_unknown_transactions_do_not_advance_replay_or_accrue_replay_violations()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        AnomalyReplayTable.Reset();
        AnomalyMessaging.ConfigureTransportSession(new byte[32], new byte[32], new byte[16], new byte[32]);
        var priorExpected = AnomalyMessaging.BulkSessionExpected;
        var priorBulk = AnomalyMessaging.BulkReceiveExpected;
        var priorHandler = AnomalyMessaging.ContentBootstrapMessageReceived;
        var received = new List<FileChunkMessage>();
        AnomalyMessaging.BulkSessionExpected = session => session == 2;
        AnomalyMessaging.BulkReceiveExpected = () => false;
        AnomalyMessaging.ContentBootstrapMessageReceived = message => received.Add((FileChunkMessage)message);
        try
        {
            for (uint counter = 1000; counter < 1400; counter++) Receive(1, counter);
            Receive(999, 9000);
            Assert.Empty(received);
            Receive(2, 2);
            Receive(2, 1);
            Assert.Equal(new uint[] { 2, 1 }, received.Select(message => message.FrameCounter));
            Assert.Equal(1, AnomalyReplayTable.InboundRateLimiter.RegisterProtocolViolation().DroppedInWindow);
        }
        finally
        {
            AnomalyMessaging.BulkSessionExpected = priorExpected;
            AnomalyMessaging.BulkReceiveExpected = priorBulk;
            AnomalyMessaging.ContentBootstrapMessageReceived = priorHandler;
            AnomalyMessaging.ClearTransportSession();
            AnomalyReplayTable.Reset();
        }
    }

    private static void Receive(ulong sessionId, uint counter)
    {
        var message = new FileChunkMessage { SessionId = sessionId, Hash = new byte[32], TotalChunks = 1,
            Data = new byte[] { 1 } };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(counter); writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName));
        message.Serialize(writer); writer.Flush();
        AnomalyMessaging.OnReceived(stream.ToArray(), encryptedAndAuthenticated: true, localTransport: false,
            beforeContentReady: true);
    }
}
