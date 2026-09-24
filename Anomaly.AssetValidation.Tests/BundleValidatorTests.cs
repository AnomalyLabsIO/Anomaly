using System.Security.Cryptography;
using System.Text;
using Anomaly.AssetValidation;
using Xunit;

namespace Anomaly.AssetValidation.Tests;

public sealed class BundleValidatorTests {
    internal static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unity6000.bundle"));
    internal static BundleAssetExpectation[] Expectations() => new[] {
        new BundleAssetExpectation("mods/fixture/sprites/checker", "fixture/sprites/checker", 213, 256, 256),
        new BundleAssetExpectation("mods/fixture/audio/tone", "fixture/audio/tone", 83, audioFrames: 12000, sampleRate: 48000, channels: 2),
        new BundleAssetExpectation("mods/fixture/meshes/quad", "fixture/meshes/quad", 43, vertices: 4, triangles: 2),
    };
    internal static BundleValidationRequest Request(byte[] bytes, IEnumerable<BundleAssetExpectation>? expectations = null) => new(Convert.ToHexString(SHA256.HashData(bytes)), expectations ?? Expectations());
    internal static ValidatedBundle Validate(byte[] bytes, IEnumerable<BundleAssetExpectation>? expectations = null) => BundleValidator.Validate(new MemoryStream(bytes), Request(bytes, expectations));

    [Fact]
    public void TrustedUnityFixtureValidatesAllFourKindsAndResources() {
        using var result = Validate(Fixture());
        Assert.Equal(4, result.Assets.Count);
        Assert.Equal(new[] { 28, 43, 83, 213 }, result.Assets.Select(value => value.ClassId).Order().ToArray());
        Assert.Equal(286440, result.ExpandedBytes);
        Assert.Equal(358376, result.TotalDecodedBytes);
        Assert.Contains(result.Assets, item => item.PathId < 0);
        var audio = Assert.Single(result.Assets, item => item.ClassId == 83);
        Assert.Equal(12000, audio.AudioFrames);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
    }

    [Fact]
    public void VerifiedLeaseCannotBeChangedByMutatingOriginalInput() {
        var bytes = Fixture(); var original = bytes.ToArray();
        using var result = Validate(bytes); Array.Clear(bytes);
        using var output = new MemoryStream(); result.WriteVerifiedBytes(output);
        Assert.Equal(original, output.ToArray());
        result.Dispose();
        Assert.Throws<ObjectDisposedException>(() => result.WriteVerifiedBytes(output));
    }

    [Fact]
    public void CancellationOccursBeforeInputAllocationOrNativeHandoff() {
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); var handedOff = false;
        Assert.Throws<OperationCanceledException>(() => { using var result = BundleValidator.Validate(new MemoryStream(Fixture()), Request(Fixture()), cancel.Token); handedOff = true; });
        Assert.False(handedOff);
    }

    [Fact]
    public void CancellationDuringInputReadDoesNotProduceALease() {
        using var cancel = new CancellationTokenSource(); var bytes = Fixture();
        using var source = new CancelOnReadStream(bytes, cancel);
        Assert.Throws<OperationCanceledException>(() => BundleValidator.Validate(source, Request(bytes), cancel.Token));
    }

    [Fact]
    public void EncodedLimitRejectsBeforeReadingOrAllocatingClaimedLength() {
        using var source = new OversizedStream();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<BundleValidationException>(() => BundleValidator.Validate(source, new BundleValidationRequest(new string('0', 64), Expectations())));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 65536);
    }

    [Fact]
    public void WrongHashAndIncompleteOrWrongIndexFailBeforeHandoff() {
        var bytes = Fixture();
        Reject(bytes, new BundleValidationRequest(new string('0', 64), Expectations()));
        Reject(bytes, Request(bytes, Expectations().Take(2)));
        var wrong = Expectations(); wrong[0] = new BundleAssetExpectation("mods/fixture/sprites/checker", "fixture/sprites/checker", 213, 255, 256);
        Reject(bytes, Request(bytes, wrong));
    }

    [Fact]
    public void RawBlocksAndIndependentRepackingRemainValid() {
        using var result = Validate(new FixtureMutator(Fixture()).Pack());
        Assert.Equal(4, result.Assets.Count);
    }

    [Theory]
    [InlineData("huge-objects")]
    [InlineData("huge-type-nodes")]
    [InlineData("changed-type-hash")]
    [InlineData("managed-field-tag")]
    [InlineData("unsupported-class")]
    [InlineData("script-index")]
    [InlineData("stripped-trees")]
    [InlineData("duplicate-path")]
    [InlineData("object-range")]
    [InlineData("script-table")]
    [InlineData("externals")]
    [InlineData("reference-types")]
    public void HostileMetadataRejectsBeforeAssetsToolsAllocations(string mutation) {
        var fixture = new FixtureMutator(Fixture()); fixture.MutateMetadata(mutation);
        var bytes = fixture.Pack();
        var before = GC.GetAllocatedBytesForCurrentThread(); Reject(bytes);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 8 * 1024 * 1024);
    }

    [Theory]
    [InlineData("texture-dimension")]
    [InlineData("texture-format")]
    [InlineData("texture-mip")]
    [InlineData("texture-resource-size")]
    [InlineData("resource-path")]
    [InlineData("sprite-script-objects")]
    [InlineData("sprite-external-reference")]
    [InlineData("sprite-dangling-reference")]
    [InlineData("sprite-wrong-reference-type")]
    [InlineData("mesh-nan")]
    [InlineData("mesh-index")]
    [InlineData("mesh-channel")]
    [InlineData("mesh-submesh")]
    [InlineData("audio-duration")]
    [InlineData("audio-channels")]
    [InlineData("audio-frames")]
    [InlineData("audio-codec")]
    [InlineData("audio-packet")]
    [InlineData("boolean")]
    [InlineData("preload")]
    [InlineData("hidden-root")]
    public void HostileObjectOrResourceRejectsBeforeNativeHandoff(string mutation) {
        var fixture = new FixtureMutator(Fixture()); fixture.MutateAsset(mutation); Reject(fixture.Pack());
    }

    [Fact]
    public void EveryTruncatedBoundaryAndTrailingDataRejects() {
        var bytes = Fixture();
        foreach (var length in new[] { 1, 7, 8, 12, 32, 48, 64, 100, bytes.Length - 1 }) Reject(bytes.Take(length).ToArray());
        Reject(bytes.Concat(new byte[] { 0 }).ToArray());
    }

    [Theory]
    [InlineData("block-count")]
    [InlineData("block-expansion")]
    [InlineData("block-compression")]
    [InlineData("directory-count")]
    [InlineData("directory-offset")]
    [InlineData("directory-name")]
    public void EnvelopeMetadataLimitsRejectBeforeExpandedAllocation(string mutation) {
        var fixture = new FixtureMutator(Fixture());
        var bytes = fixture.Pack(info => {
            var blocks = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(info.AsSpan(16));
            var directoryCount = 20 + blocks * 10;
            switch (mutation) {
                case "block-count": System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(info.AsSpan(16), int.MaxValue); break;
                case "block-expansion": System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(info.AsSpan(20), int.MaxValue); break;
                case "block-compression": info[29] = 1; break;
                case "directory-count": System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(info.AsSpan(directoryCount), int.MaxValue); break;
                case "directory-offset": info[directoryCount + 11] = 1; break;
                case "directory-name": info[directoryCount + 24] = (byte)'/'; break;
            }
        });
        var before = GC.GetAllocatedBytesForCurrentThread(); Reject(bytes);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 2 * 1024 * 1024);
    }

    [Fact]
    public void OversizedInfoDeclarationRejectsBeforeItsAllocation() {
        var bytes = Fixture(); var reader = new BoundedReader(bytes, 0, bytes.Length, bigEndian: true);
        reader.NullString(7); reader.U32(); reader.NullString(16); reader.NullString(32); reader.U64(); reader.U32();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(reader.Position), uint.MaxValue);
        var before = GC.GetAllocatedBytesForCurrentThread(); Reject(bytes);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 2 * 1024 * 1024);
    }

    [Fact]
    public void FixturesAndEmbeddedSchemasMatchRecordedProvenance() {
        Assert.Equal("BB139F22934C6991C0C654DB10BF90EA8435954CCB1593CB8D2298A67372A29E", Convert.ToHexString(SHA256.HashData(Fixture())));
        var hashes = new Dictionary<int, string> {
            [28] = "099BE08975977F33FC9F2A76040E48356711A907C8C303995355147C1DA0BA18",
            [43] = "96B7DC953ECBEC402597E2B4DEA5045B5A6E5979219FD61F0DD4255FE4F16D75",
            [83] = "B61767E0BC01593949E3ECDCA321C6D0EE8E11504D0F27D564D0041F800BA10D",
            [142] = "57391D757C605BCCB741CC272DE2BA3655466BB0DCB719A3359A5155DFC05E5A",
            [213] = "0340A41BA4D84A5DB45D9F697E27FBA799DA068C3E49BE075F2AFCCDA181B2E5",
        };
        foreach (var item in hashes) Assert.Equal(item.Value, Convert.ToHexString(SHA256.HashData(CanonicalProfile.Types[item.Key].Record)));
    }

    [Fact]
    public void LongerUnityAudioWithSeekTableMatchesStockPhysicalTiming() {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unity6000-g7.bundle"));
        Assert.Equal("650C15A42B6C06837578FFE9D06AC76199C892D2ADBEBAE7A055040D049CCC70", Convert.ToHexString(SHA256.HashData(bytes)));
        using var result = Validate(bytes, new[] { new BundleAssetExpectation("game/cassie/.g7", "game/cassie/.g7", 83, audioFrames: 72000, sampleRate: 44100, channels: 1) });
        Assert.Equal(72000, Assert.Single(result.Assets).AudioFrames);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("frame")]
    [InlineData("offset")]
    [InlineData("nonboundary")]
    public void VorbisSeekTableCannotEscapeFramesOrPacketBoundaries(string mutation) {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unity6000-g7.bundle"));
        var fixture = new FixtureMutator(bytes); fixture.MutateSeek(mutation); bytes = fixture.Pack();
        Reject(bytes, Request(bytes, new[] { new BundleAssetExpectation("game/cassie/.g7", "game/cassie/.g7", 83, audioFrames: 72000, sampleRate: 44100, channels: 1) }));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0 }, 4)]
    [InlineData(new byte[] { 0x10, 1, 2, 0 }, 5)]
    [InlineData(new byte[] { 0xf0, 255 }, 16)]
    [InlineData(new byte[] { 0x50, 1, 2, 3, 4 }, 5)]
    [InlineData(new byte[] { 0x10, 1 }, 5)]
    [InlineData(new byte[] { 0x60, 1, 2, 3, 4, 5, 6 }, 5)]
    public void InvalidLz4RejectsWithoutOverruns(byte[] input, int outputSize) {
        Assert.Throws<BundleValidationException>(() => StrictLz4.Decode(new ByteSlice(input, 0, input.Length), new byte[outputSize], 0, outputSize, default));
    }

    [Fact]
    public void LargeTextureProofPreservesCompressedRatherThanExpandedTextureLimit() {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(root, "artifacts", "assetbundles", "authoring-proof", "anomaly-skin-proof.bundle");
        if (!File.Exists(path)) return; // Optional local authoring integration artifact, not a unit-test dependency.
        var bytes = File.ReadAllBytes(path);
        using var result = Validate(bytes, new[] {
            new BundleAssetExpectation("game/skin/logo", "game/skin/logo", 28, 2048, 2048),
            new BundleAssetExpectation("game/skin/background", "game/skin/background", 28, 3328, 1872),
        });
        Assert.Contains(result.Assets, item => item.DecodedBytes > 16 * 1024 * 1024 && item.CompressedBytes < 16 * 1024 * 1024);
    }

    private static void Reject(byte[] bytes, BundleValidationRequest? request = null) {
        var nativeCalled = false;
        Assert.Throws<BundleValidationException>(() => {
            using var result = BundleValidator.Validate(new MemoryStream(bytes), request ?? Request(bytes));
            nativeCalled = true;
        });
        Assert.False(nativeCalled);
    }
}

internal sealed class FixtureMutator {
    private readonly UnityFsEnvelope _envelope;
    private readonly List<AssetRecord> _assets;
    internal FixtureMutator(byte[] bytes) { _envelope = UnityFsEnvelope.Read(bytes, default); _assets = SerializedMetadata.Read(_envelope, default); }
    private void Int(int offset, int value) => BitConverter.GetBytes(value).CopyTo(_envelope.Expanded, offset);
    private void Long(int offset, long value) => BitConverter.GetBytes(value).CopyTo(_envelope.Expanded, offset);
    private Value Asset(int classId) => ObjectVisitor.Read(_assets.Single(item => item.ClassId == classId), new List<AssetPointer>(), default);

    internal void MutateMetadata(string mutation) {
        var reader = new BoundedReader(_envelope.Expanded, 48, _envelope.Entries[0].Length - 48);
        reader.NullString(32); reader.U32(); var treeFlag = reader.Position; reader.Boolean(); var types = reader.I32(); var typeStart = reader.Position;
        for (var i = 0; i < types; i++) { var id = reader.I32(); reader.Position += CanonicalProfile.Types[id].Record.Length - 4; }
        var countOffset = reader.Position; var count = reader.I32(); reader.Align(); var objects = reader.Position;
        switch (mutation) {
            case "huge-objects": Int(countOffset, int.MaxValue); break;
            case "huge-type-nodes": Int(typeStart + 23, int.MaxValue); break;
            case "changed-type-hash": _envelope.Expanded[typeStart + 7] ^= 1; break;
            case "managed-field-tag": _envelope.Expanded[typeStart + 31 + 3] = 4; break;
            case "unsupported-class": Int(typeStart, 114); break;
            case "script-index": _envelope.Expanded[typeStart + 5] = 0; break;
            case "stripped-trees": _envelope.Expanded[treeFlag] = 0; break;
            case "duplicate-path": Long(objects + 24, BitConverter.ToInt64(_envelope.Expanded, objects)); break;
            case "object-range": Long(objects + 8, long.MaxValue); break;
            case "script-table": Int(objects + count * 24, int.MaxValue); break;
            case "externals": Int(objects + count * 24 + 4, 1); break;
            case "reference-types": Int(objects + count * 24 + 8, 1); break;
            default: throw new ArgumentException(mutation);
        }
    }

    internal void MutateAsset(string mutation) {
        var texture = Asset(28); var sprite = Asset(213); var mesh = Asset(43); var audio = Asset(83); var bundle = Asset(142);
        var resource = _envelope.Entries.Single(item => item.Name.EndsWith(".resource", StringComparison.Ordinal));
        switch (mutation) {
            case "texture-dimension": Int(texture["m_Width"].Offset, int.MaxValue); break;
            case "texture-format": Int(texture["m_TextureFormat"].Offset, 12); break;
            case "texture-mip": Int(texture["m_MipCount"].Offset, 16); break;
            case "texture-resource-size": Int(texture["m_StreamData"]["size"].Offset, int.MaxValue); break;
            case "resource-path": _envelope.Expanded[texture["m_StreamData"]["path"].Offset + 4] = (byte)'x'; break;
            case "sprite-script-objects": Int(sprite["m_ScriptableObjects"]["Array"].Offset, int.MaxValue); break;
            case "sprite-external-reference": Int(sprite["m_RD"]["texture"]["m_FileID"].Offset, 1); break;
            case "sprite-dangling-reference": Long(sprite["m_RD"]["texture"]["m_PathID"].Offset, 123456); break;
            case "sprite-wrong-reference-type": Long(sprite["m_RD"]["texture"]["m_PathID"].Offset, _assets.Single(item => item.ClassId == 83).PathId); break;
            case "mesh-nan": Int(mesh["m_VertexData"]["m_DataSize"].Bytes.Offset, 0x7fc00000); break;
            case "mesh-index": _envelope.Expanded[mesh["m_IndexBuffer"].ByteArray.Offset] = 10; break;
            case "mesh-channel": _envelope.Expanded[mesh["m_VertexData"]["m_Channels"].Array[0]["dimension"].Offset] = 255; break;
            case "mesh-submesh": Int(mesh["m_SubMeshes"].Array[0]["indexCount"].Offset, int.MaxValue); break;
            case "audio-duration": Int(audio["m_Length"].Offset, BitConverter.SingleToInt32Bits(29)); break;
            case "audio-channels": Int(audio["m_Channels"].Offset, 1); break;
            case "audio-frames": Long(resource.Offset + 60, unchecked((long)0xffffffff00000033)); break;
            case "audio-codec": Int(resource.Offset + 24, 2); break;
            case "audio-packet": _envelope.Expanded[resource.Offset + 96] = 255; _envelope.Expanded[resource.Offset + 97] = 255; break;
            case "boolean": _envelope.Expanded[texture["m_IsReadable"].Offset] = 2; break;
            case "preload": Int(bundle["m_Container"].Array[0]["second"]["preloadSize"].Offset, 2); break;
            case "hidden-root": _envelope.Expanded[bundle["m_Container"].Array[0]["first"].Offset + 4] = (byte)'x'; break;
            default: throw new ArgumentException(mutation);
        }
    }

    internal void MutateSeek(string mutation) {
        var resource = _envelope.Entries.Single(item => item.Name.EndsWith(".resource", StringComparison.Ordinal));
        switch (mutation) {
            case "size": Int(resource.Offset + 76, int.MaxValue); break;
            case "frame": Int(resource.Offset + 80, 72001); break;
            case "offset": Int(resource.Offset + 84, int.MaxValue); break;
            case "nonboundary": Int(resource.Offset + 84, 2013); break;
        }
    }

    internal byte[] Pack(Action<byte[]>? metadataMutation = null) {
        var expanded = _envelope.Expanded; var blockCount = (expanded.Length + 131071) / 131072;
        using var infoStream = new MemoryStream(); using var info = new BinaryWriter(infoStream, Encoding.UTF8, true);
        info.Write(new byte[16]); U32(info, (uint)blockCount);
        for (var i = 0; i < blockCount; i++) { var length = Math.Min(131072, expanded.Length - i * 131072); U32(info, (uint)length); U32(info, (uint)length); U16(info, 0); }
        U32(info, (uint)_envelope.Entries.Count);
        foreach (var entry in _envelope.Entries) { U64(info, (ulong)entry.Offset); U64(info, (ulong)entry.Length); U32(info, entry == _envelope.Entries[0] ? 4u : 0u); Z(info, entry.Name); }
        var rawInfo = infoStream.ToArray(); metadataMutation?.Invoke(rawInfo); var compressedInfo = Literals(rawInfo);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Z(writer, "UnityFS"); U32(writer, 8); Z(writer, "5.x.x"); Z(writer, BundleValidator.UnityVersion);
        var lengthOffset = stream.Position; U64(writer, 0); U32(writer, (uint)compressedInfo.Length); U32(writer, (uint)rawInfo.Length); U32(writer, 0x243); Align(writer);
        writer.Write(compressedInfo); Align(writer); writer.Write(expanded); var lengthTotal = stream.Length;
        stream.Position = lengthOffset; U64(writer, (ulong)lengthTotal); return stream.ToArray();
    }
    private static byte[] Literals(byte[] source) {
        using var stream = new MemoryStream(); stream.WriteByte((byte)(Math.Min(15, source.Length) << 4));
        if (source.Length >= 15) { var remaining = source.Length - 15; while (remaining >= 255) { stream.WriteByte(255); remaining -= 255; } stream.WriteByte((byte)remaining); }
        stream.Write(source); return stream.ToArray();
    }
    private static void Align(BinaryWriter writer) { while ((writer.BaseStream.Position & 15) != 0) writer.Write((byte)0); }
    private static void Z(BinaryWriter writer, string value) { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
    private static void U16(BinaryWriter writer, ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    private static void U32(BinaryWriter writer, uint value) { writer.Write((byte)(value >> 24)); writer.Write((byte)(value >> 16)); writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    private static void U64(BinaryWriter writer, ulong value) { U32(writer, (uint)(value >> 32)); U32(writer, (uint)value); }
}

internal sealed class CancelOnReadStream : MemoryStream {
    private readonly CancellationTokenSource _cancel;
    internal CancelOnReadStream(byte[] bytes, CancellationTokenSource cancel) : base(bytes) { _cancel = cancel; }
    public override int Read(byte[] buffer, int offset, int count) { var result = base.Read(buffer, offset, count); _cancel.Cancel(); return result; }
}

internal sealed class OversizedStream : MemoryStream {
    public override long Length => (long)BundleValidator.MaximumEncodedBytes + 1;
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Oversized input must not be read.");
}
