using Anomaly.Client.Api.Features;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class CapabilityRegistrationTests
{
    [Fact]
    public void Capacity_rejection_preserves_existing_registrations_and_disposal_frees_a_slot()
    {
        var registrations = new List<IDisposable>();
        string prefix = "tests:" + Guid.NewGuid().ToString("N");
        try
        {
            for (int index = 0; index < CapabilitySnapshotMessage.MaxCapabilities; index++)
                registrations.Add(ClientCapabilities.Register(new CapabilityDescriptor(prefix + index, 1, 0)));

            string rejectedId = prefix + "overflow";
            Assert.Throws<InvalidOperationException>(() =>
                ClientCapabilities.Register(new CapabilityDescriptor(rejectedId, 1, 0)));
            Assert.False(ClientCapabilities.TryGet(rejectedId, out _));
            for (int index = 0; index < CapabilitySnapshotMessage.MaxCapabilities; index++)
                Assert.True(ClientCapabilities.TryGet(prefix + index, out _));

            registrations[0].Dispose();
            Assert.False(ClientCapabilities.TryGet(prefix + "0", out _));
            registrations.Add(ClientCapabilities.Register(new CapabilityDescriptor(rejectedId, 1, 0)));
            Assert.True(ClientCapabilities.TryGet(rejectedId, out _));
        }
        finally
        {
            foreach (IDisposable registration in registrations)
                registration.Dispose();
        }
    }
}
