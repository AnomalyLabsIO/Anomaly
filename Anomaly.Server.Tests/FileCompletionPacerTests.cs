using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class FileCompletionPacerTests
{
    [Fact]
    public void AllManifestAttemptsRemainWithinControlAndAckAdmissionWithoutResettingThePacer()
    {
        double now = 0;
        var pacer = new FileCompletionPacer(() => now);
        var sentAt = new List<double>();
        int maximumAttempts = FileManifestMessage.MaxFiles * (BulkTransferProtocol.MaxRetries + 1);
        while (sentAt.Count < maximumAttempts)
        {
            while (sentAt.Count < maximumAttempts && pacer.TryTake()) sentAt.Add(now);
            Assert.False(pacer.TryTake());
            now += 1d / 1024;
        }
        Assert.Equal(768, sentAt.Count);
        Assert.InRange(now, 5.9, 6.1);
        foreach (double start in sentAt)
        {
            int completions = sentAt.Count(time => time >= start && time < start + 1);
            Assert.InRange(completions, 1, FileCompletionPacer.MessagesPerSecond + FileCompletionPacer.Burst);
            // Each completion permits one file ACK. Credits are coalesced at
            // 25ms, and bootstrap/status traffic has room in the same ordinary
            // aggregate. Never increase general ingress limits for tiny files.
            Assert.True(completions + 40 + 16 < AnomalyInboundRateLimiter.AggregateMessagesPerSecond);
            Assert.True(completions + 40 + 16 < AnomalyInboundRateLimiter.AggregateMessageBurst);
        }
    }

    [Fact]
    public void LongWaitRefillsOnlyTheFourFileBurstAndClockRegressionCannotMintTokens()
    {
        double now = 0;
        var pacer = new FileCompletionPacer(() => now);
        for (int index = 0; index < 4; index++) Assert.True(pacer.TryTake());
        Assert.False(pacer.TryTake());
        now = 1000;
        for (int index = 0; index < 4; index++) Assert.True(pacer.TryTake());
        Assert.False(pacer.TryTake());
        now = 999;
        Assert.False(pacer.TryTake());
        now = 1000;
        Assert.False(pacer.TryTake());
    }
}
