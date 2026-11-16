using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class FileControlContractTests
{
    [Fact]
    public void FileTransactionControlsUseTheRegisteredOrderedStreamWhileChunksKeepTheirBulkDomain()
    {
        AnomalyMessageRegistry.RegisterDefaults();
        IAnomalyMessage[] controls = {
            new FileTransferCompleteMessage(), new FileRequestMessage(), new FileAckMessage(),
            new FileCreditMessage(), new FileTransferStatusMessage(), new ContentCatalogMessage(),
            new ContentCatalogDecisionMessage(), new ContentCatalogAckMessage(), new ContentBootstrapReadyMessage(),
        };
        foreach (var message in controls)
        {
            Assert.Equal(MessageChannel.ReliableOrdered, message.TransportChannel);
            Assert.True(AnomalyMessageRegistry.TryGetChannel(AnomalyMessageRegistry.GetHash(message.MessageName), out var registered));
            Assert.Equal(MessageChannel.ReliableOrdered, registered);
        }
        Assert.Equal(MessageChannel.ReliableUnordered, new FileChunkMessage().TransportChannel);
    }
}
