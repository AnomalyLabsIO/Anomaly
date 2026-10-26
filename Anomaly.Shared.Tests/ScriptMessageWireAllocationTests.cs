using System;
using System.IO;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ScriptMessageWireAllocationTests
{
    [Fact]
    public void RepeatedValidScopeChecksAllocateNoScratchHashStrings()
    {
        string hash = new string('a', 64);
        for (int call = 0; call < 128; call++) ScriptMessageWire.ValidateScope(hash, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int call = 0; call < 1024; call++) ScriptMessageWire.ValidateScope(hash, 1);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BinaryIndexHashRetainsEveryHexNibbleWithoutTemporarySubstrings()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var outgoing = ScriptToServerMessage.Create("example:panel", "State", ScriptMessagePayload.FromValues(
            new System.Collections.Generic.Dictionary<string, object>())).Bind(hash, 12);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) outgoing.Serialize(writer);
        bytes.Position = 0;
        using var reader = new BinaryReader(bytes);
        var incoming = new ScriptToServerMessage();
        incoming.Deserialize(reader);
        Assert.Equal(hash, incoming.IndexSha256);
        Assert.Equal(bytes.Length, bytes.Position);
    }
}
