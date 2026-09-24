using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Anomaly.AssetValidation;

internal sealed class AssetSemantics {
    private readonly UnityFsEnvelope _envelope;
    private readonly CancellationToken _cancellation;
    internal readonly Dictionary<string, ContainerRoot> Roots = new Dictionary<string, ContainerRoot>(StringComparer.Ordinal);
    internal readonly List<long> Preloads = new List<long>();

    internal AssetSemantics(UnityFsEnvelope envelope, CancellationToken cancellation) { _envelope = envelope; _cancellation = cancellation; }
    internal void Validate(AssetRecord asset, Value value) {
        Guard.Require(!string.IsNullOrEmpty(value.S("m_Name")), "Missing asset name.");
        switch (asset.ClassId) {
            case 142: Bundle(value); break;
            case 28: Texture(asset, value); break;
            case 43: Mesh(asset, value); break;
            case 213: Sprite(asset, value); break;
            case 83: Audio(asset, value); break;
            default: throw new BundleValidationException("Unsupported asset class.");
        }
        asset.Summary.CompressedBytes = _envelope.CompressedCost(asset.Ranges);
        var maximum = asset.ClassId == 28 || asset.ClassId == 213 ? 16 * 1024 * 1024 : 32 * 1024 * 1024;
        Guard.Require(asset.Summary.CompressedBytes <= maximum && asset.Summary.DecodedBytes <= 64L * 1024 * 1024, "Asset byte budget exceeded.");
    }

    private void Bundle(Value value) {
        Guard.Require(value.S("m_Name") == value.S("m_AssetBundleName") && value.I("m_RuntimeCompatibility") == 1 && value.I("m_ExplicitDataLayout") == 0 && value.I("m_PathFlags") == 4, "Unsupported bundle asset settings.");
        value["m_Dependencies"].Empty(); value["m_SceneHashes"].Empty(); value["m_MainAsset"].Empty(); value["m_IsStreamedSceneAssetBundle"].Empty();
        foreach (var item in value["m_PreloadTable"].Array) { var path = item.I("m_PathID"); Guard.Require(path != 0, "Null preload reference."); Preloads.Add(path); }
        foreach (var item in value["m_Container"].Array) {
            var name = item.S("first"); var info = item["second"]; var index = info.I("preloadIndex"); var size = info.I("preloadSize");
            Guard.Require(BundleValidator.IsEntryName(name) && !Roots.ContainsKey(name) && Roots.Count < 4095, "Invalid or duplicate asset entry name.");
            Guard.Require(index >= 0 && size >= 1 && index <= Preloads.Count && size <= Preloads.Count - index, "Invalid preload range.");
            var path = info["asset"].I("m_PathID"); Guard.Require(path != 0, "Null root asset.");
            Roots.Add(name, new ContainerRoot(path, (int)index, (int)size));
        }
        Guard.Require(Roots.Count > 0, "Empty bundle.");
    }

    private void Texture(AssetRecord asset, Value value) {
        var width = value.I("m_Width"); var height = value.I("m_Height");
        Guard.Require(width > 0 && width <= 4096 && height > 0 && height <= 4096 && width * height <= 16 * 1024 * 1024, "Texture dimensions exceed limit.");
        var bytes = width * height * 4;
        Guard.Require(value.I("m_TextureFormat") == 4 && value.I("m_MipCount") == 1 && value.I("m_MipsStripped") == 0 && value.I("m_CompleteImageSize") == bytes && value.I("m_ImageCount") == 1 && value.I("m_TextureDimension") == 2, "Unsupported texture format or image size.");
        value["m_PlatformBlob"].Empty(); value["m_IsPreProcessed"].Empty(); value["m_StreamingMipmaps"].Empty(); value["m_StreamingMipmapsPriority"].Empty(); value["m_MipmapLimitGroupName"].Empty();
        Guard.Require(value.I("m_ColorSpace") >= 0 && value.I("m_ColorSpace") <= 1 && value.I("m_LightmapFormat") == 0, "Unsupported texture color settings.");
        var settings = value["m_TextureSettings"];
        Guard.Require(settings.I("m_FilterMode") >= 0 && settings.I("m_FilterMode") <= 2 && settings.I("m_Aniso") >= 0 && settings.I("m_Aniso") <= 16 && Math.Abs(settings.F("m_MipBias")) <= 3, "Unsupported texture sampling settings.");
        foreach (var name in new[] { "m_WrapU", "m_WrapV", "m_WrapW" }) Guard.Require(settings.I(name) >= 0 && settings.I(name) <= 3, "Unsupported texture wrap mode.");
        var inline = value["image data"].Bytes; var stream = value["m_StreamData"];
        if (inline.Count != 0) { Guard.Require(inline.Count == bytes, "Texture payload size mismatch."); stream.Empty(); }
        else { var range = Resource(asset, stream.S("path"), stream.I("offset"), stream.I("size"), ".resS"); Guard.Require(range.Count == bytes, "Texture resource size mismatch."); }
        asset.Summary.Width = (int)width; asset.Summary.Height = (int)height; asset.Summary.DecodedBytes = bytes;
    }

    private void Mesh(AssetRecord asset, Value value) {
        foreach (var name in new[] { "m_Shapes", "m_BindPose", "m_BoneNameHashes", "m_RootBoneNameHash", "m_BonesAABB", "m_VariableBoneCountWeights", "m_MeshCompression", "m_CompressedMesh", "m_BakedConvexCollisionMesh", "m_BakedTriangleCollisionMesh", "m_StreamData", "m_MeshUsageFlags" }) value[name].Empty();
        Guard.Require(value.I("m_CookingOptions") == 30 && value.F("m_MeshMetrics[0]") >= 0 && value.F("m_MeshMetrics[0]") <= 1e9 && value.F("m_MeshMetrics[1]") >= 0 && value.F("m_MeshMetrics[1]") <= 1e9, "Unsupported mesh settings.");
        Bounds(value["m_LocalAABB"]);
        Geometry(asset, value["m_VertexData"], value["m_IndexBuffer"].ByteArray, value["m_SubMeshes"].Array, value.I("m_IndexFormat"));
    }

    private void Sprite(AssetRecord asset, Value value) {
        foreach (var name in new[] { "m_Offset", "m_Border", "m_Extrude", "m_IsPolygon", "m_RenderDataKey", "m_AtlasTags", "m_SpriteAtlas", "m_PhysicsShape", "m_Bones", "m_ScriptableObjects" }) value[name].Empty();
        Guard.Require(value.F("m_PixelsToUnits") >= 0.01 && value.F("m_PixelsToUnits") <= 100000 && value["m_Pivot"].F("x") >= 0 && value["m_Pivot"].F("x") <= 1 && value["m_Pivot"].F("y") >= 0 && value["m_Pivot"].F("y") <= 1, "Unsupported sprite units or pivot.");
        var rect = value["m_Rect"]; var width = rect.F("width"); var height = rect.F("height");
        Guard.Require(width >= 1 && width <= 4096 && height >= 1 && height <= 4096 && width == Math.Floor(width) && height == Math.Floor(height) && rect.F("x") == 0 && rect.F("y") == 0, "Unsupported sprite rectangle.");
        var data = value["m_RD"];
        foreach (var name in new[] { "alphaTexture", "secondaryTextures", "m_Bindpose", "textureRectOffset", "settingsRaw" }) data[name].Empty();
        Guard.Require(data.F("downscaleMultiplier") == 1 && data["atlasRectOffset"].F("x") == -1 && data["atlasRectOffset"].F("y") == -1, "Unsupported sprite packing.");
        var textureRect = data["textureRect"];
        Guard.Require(textureRect.F("x") == 0 && textureRect.F("y") == 0 && textureRect.F("width") == width && textureRect.F("height") == height, "Sprite texture rectangle mismatch.");
        foreach (var name in new[] { "x", "y", "z", "w" }) Guard.Require(Math.Abs(data["uvTransform"].F(name)) <= 100000, "Sprite UV transform exceeds limit.");
        asset.SpriteTexture = data["texture"].I("m_PathID"); Guard.Require(asset.SpriteTexture != 0, "Missing sprite texture.");
        Geometry(asset, data["m_VertexData"], data["m_IndexBuffer"].ByteArray, data["m_SubMeshes"].Array, 0);
        asset.Summary.Width = (int)width; asset.Summary.Height = (int)height;
    }

    private void Geometry(AssetRecord asset, Value vertices, ByteSlice indices, List<Value> submeshes, long indexFormat) {
        var count = vertices.I("m_VertexCount"); var channels = vertices["m_Channels"].Array; var data = vertices["m_DataSize"].Bytes;
        Guard.Require(count >= 3 && count <= 100000 && channels.Count == 14 && (indexFormat == 0 || indexFormat == 1), "Unsupported vertex or index format.");
        var indexBytes = indexFormat == 0 ? 2 : 4;
        Guard.Require(indices.Count > 0 && indices.Count % (3 * indexBytes) == 0 && indices.Count / indexBytes <= 600000 && submeshes.Count > 0 && submeshes.Count <= 32, "Mesh index budget exceeded.");
        var strides = new int[4]; var offsets = new int[4];
        for (var i = 0; i < channels.Count; i++) {
            var channel = channels[i]; var stream = channel.I("stream"); var offset = channel.I("offset"); var format = channel.I("format"); var dimension = channel.I("dimension");
            if (dimension == 0) { channel.Empty(); continue; }
            Guard.Require(i < 12 && stream >= 0 && stream < 4 && format == 0 && dimension >= 1 && dimension <= 4 && offset == strides[(int)stream], "Unsupported or overlapping mesh channel.");
            Guard.Require(i == 0 ? dimension == 3 : i == 1 ? dimension == 3 : i == 2 || i == 3 ? dimension == 4 : dimension == 2, "Unsupported channel dimensions.");
            strides[(int)stream] += (int)dimension * 4;
        }
        Guard.Require(channels[0].I("dimension") == 3 && channels[12].I("dimension") == 0 && channels[13].I("dimension") == 0, "Mesh position or skin channels invalid.");
        long dataSize = 0;
        for (var i = 0; i < 4; i++) {
            if (strides[i] == 0) continue;
            var aligned = (dataSize + 15) & ~15L;
            Guard.Require(aligned <= data.Count, "Vertex stream offset exceeds buffer.");
            var padding = new BoundedReader(data.Bytes, data.Offset + (int)dataSize, (int)(aligned - dataSize)); padding.Zero(padding.Remaining);
            dataSize = aligned; offsets[i] = (int)dataSize; dataSize += count * strides[i];
            Guard.Require(dataSize <= data.Count, "Vertex stream exceeds buffer.");
        }
        Guard.Require(dataSize == data.Count, "Vertex buffer size mismatch.");
        for (var i = 0; i < channels.Count; i++) {
            var channel = channels[i]; var dimension = (int)channel.I("dimension"); if (dimension == 0) continue;
            var stream = (int)channel.I("stream"); var channelOffset = (int)channel.I("offset");
            var cursor = new BoundedReader(data.Bytes, data.Offset, data.Count);
            for (var vertex = 0; vertex < count; vertex++) {
                if ((vertex & 1023) == 0) _cancellation.ThrowIfCancellationRequested();
                cursor.Position = data.Offset + offsets[stream] + vertex * strides[stream] + channelOffset;
                for (var component = 0; component < dimension; component++) {
                    var number = cursor.Number32(); var maximum = i == 0 ? 10000 : i <= 3 ? 1.01 : 1000000;
                    Guard.Require(Math.Abs(number) <= maximum && (i != 3 || number >= 0), "Vertex component exceeds limit.");
                }
            }
        }
        var covered = 0;
        foreach (var mesh in submeshes) {
            var first = mesh.I("firstByte"); var length = mesh.I("indexCount"); var firstVertex = mesh.I("firstVertex"); var vertexCount = mesh.I("vertexCount");
            Guard.Require(first == covered && length > 0 && length % 3 == 0 && length <= (indices.Count - covered) / indexBytes && mesh.I("topology") == 0 && mesh.I("baseVertex") == 0 && firstVertex >= 0 && vertexCount > 0 && firstVertex <= count && vertexCount <= count - firstVertex, "Invalid submesh range.");
            Bounds(mesh["localAABB"]);
            var cursor = new BoundedReader(indices.Bytes, indices.Offset + covered, (int)length * indexBytes, _cancellation);
            for (var i = 0; i < length; i++) { var index = indexBytes == 2 ? cursor.U16() : cursor.U32(); Guard.Require(index >= firstVertex && index < firstVertex + vertexCount, "Mesh index exceeds vertex range."); }
            covered += (int)length * indexBytes;
        }
        Guard.Require(covered == indices.Count, "Unindexed mesh indices.");
        asset.Summary.Vertices = (int)count; asset.Summary.Triangles = indices.Count / indexBytes / 3;
        asset.Summary.DecodedBytes = (long)data.Count + indices.Count;
    }

    private static void Bounds(Value bounds) {
        foreach (var axis in new[] { "x", "y", "z" }) Guard.Require(Math.Abs(bounds["m_Center"].F(axis)) <= 1000000 && bounds["m_Extent"].F(axis) >= 0 && bounds["m_Extent"].F(axis) <= 1000000, "Mesh bounds exceed limit.");
    }

    private void Audio(AssetRecord asset, Value value) {
        var channels = value.I("m_Channels"); var rate = value.I("m_Frequency"); var length = value.F("m_Length");
        Guard.Require(channels >= 1 && channels <= 2 && rate >= 8000 && rate <= 96000 && length > 0 && length <= 30 && value.I("m_BitsPerSample") == 16, "Audio decode budget exceeded.");
        Guard.Require(value.I("m_LoadType") == 1 && value.I("m_CompressionFormat") == 1 && value.I("m_SubsoundIndex") == 0, "Unsupported audio compression or load mode.");
        value["m_IsTrackerFormat"].Empty(); value["m_Ambisonic"].Empty(); value["m_LoadInBackground"].Empty();
        var source = value["m_Resource"];
        var resource = Resource(asset, source.S("m_Source"), source.I("m_Offset"), source.I("m_Size"), ".resource");
        var frames = Fsb5.Validate(resource, (int)channels, (int)rate, _cancellation);
        Guard.Require(frames > 0 && frames <= rate * 30 && Math.Abs(length - (double)frames / rate) <= Math.Max(0.000002, 0.5 / rate), "Audio duration disagrees with sample frames.");
        asset.Summary.AudioFrames = frames; asset.Summary.Channels = (int)channels; asset.Summary.SampleRate = (int)rate; asset.Summary.DecodedBytes = frames * channels * 4;
    }

    private ByteSlice Resource(AssetRecord asset, string path, long offset, long size, string suffix) {
        var name = _envelope.Entries[0].Name + suffix;
        Guard.Require(path == "archive:/" + _envelope.Entries[0].Name + "/" + name, "Resource path is not internal to this bundle.");
        var entry = _envelope.Entries.FirstOrDefault(item => item.Name == name);
        Guard.Require(entry != null && offset >= 0 && size > 0 && offset <= entry.Length && size <= entry.Length - offset && (offset & 15) == 0, "Resource range exceeds bundle entry.");
        var range = new ByteSlice(_envelope.Expanded, entry.Offset + (int)offset, (int)size);
        entry.Used.Add(range); asset.Ranges.Add(range); return range;
    }

    internal void VerifyResourceCoverage() {
        foreach (var entry in _envelope.Entries.Skip(1)) {
            var covered = entry.Offset;
            foreach (var range in entry.Used.OrderBy(item => item.Offset)) {
                var aligned = entry.Offset + ((covered - entry.Offset + 15) & ~15);
                Guard.Require(range.Offset == covered || range.Offset == aligned, "Unreferenced or overlapping resource data.");
                var gap = new BoundedReader(_envelope.Expanded, covered, range.Offset - covered, _cancellation); gap.Zero(gap.Remaining);
                covered = range.Offset + range.Count;
            }
            Guard.Require(covered == entry.Offset + entry.Length, "Unreferenced resource entry.");
        }
    }
}

internal readonly struct ContainerRoot {
    internal ContainerRoot(long pathId, int preloadIndex, int preloadSize) { PathId = pathId; PreloadIndex = preloadIndex; PreloadSize = preloadSize; }
    internal readonly long PathId;
    internal readonly int PreloadIndex;
    internal readonly int PreloadSize;
}
