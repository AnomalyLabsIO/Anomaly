using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class ClrEntryStubPreserverTests
{
    [Theory]
    [InlineData(0xE9, 0x5F)]
    [InlineData(0xE8, 0x5E)]
    public void Live_and_cold_fixup_precodes_are_recognized(int opcode, int tag) =>
        Assert.True(ClrEntryStubPreserver.IsFixupPrecode(new byte[] { (byte)opcode, 0x93, 0x0E, 0x16, 0, (byte)tag, 0, 0 }));

    [Theory]
    [InlineData(0xE9, 0x90)]
    [InlineData(0xFF, 0x5F)]
    [InlineData(0xE8, 0x5F)]
    public void Ordinary_native_entries_are_not_treated_as_clr_precodes(int opcode, int tag) =>
        Assert.False(ClrEntryStubPreserver.IsFixupPrecode(new byte[] { (byte)opcode, 0, 0, 0, 0, (byte)tag, 0, 0 }));

    [Fact]
    public void Captured_apply_and_change_source_descriptors_resolve_their_distinct_native_slots()
    {
        Assert.Equal(8, ClrEntryStubPreserver.NativeCodeSlotOffset(0x2020));
        Assert.Equal(16, ClrEntryStubPreserver.NativeCodeSlotOffset(0x2028));
    }

    [Theory]
    [InlineData(0x2000)]
    [InlineData(0x2030)]
    [InlineData(0x2025)]
    public void Unsupported_descriptor_layouts_are_rejected(int flags) =>
        Assert.Equal(-1, ClrEntryStubPreserver.NativeCodeSlotOffset((ushort)flags));

    [Fact]
    public void Incomplete_entry_bytes_are_not_treated_as_a_clr_stub() =>
        Assert.False(ClrEntryStubPreserver.IsFixupPrecode(new byte[] { 0xE9, 0, 0, 0, 0, 0x5F }));
}
