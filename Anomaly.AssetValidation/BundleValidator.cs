using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Anomaly.AssetValidation;

/// <summary>A deliberately narrow validator for the matching Anomaly authoring profile.</summary>
public static class BundleValidator {
    public const string UnityVersion = "6000.0.43f1";
    public const string ProfileId = "unity6000.0.43f1-win64-v1";
    public const int MaximumEncodedBytes = 64 * 1024 * 1024;
    public const int MaximumExpandedBytes = 128 * 1024 * 1024;
    public const long MaximumDecodedBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Copies a seekable input into private memory and validates it without invoking Unity.
    /// Callers serialize validation jobs and account for the separate encoded/expanded memory reservation.
    /// Native Unity/FMOD decoding still requires a current patched engine and is not sandboxed by this API.
    /// </summary>
    public static ValidatedBundle Validate(Stream source, BundleValidationRequest request, CancellationToken cancellationToken = default) {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (request == null) throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        Guard.Require(source.CanRead && source.CanSeek, "Validation requires a bounded seekable input.");
        var length = source.Length - source.Position;
        Guard.Require(length > 0 && length <= MaximumEncodedBytes, "Bundle file size exceeds limit.");
        var bytes = new byte[(int)length];
        for (var offset = 0; offset < bytes.Length;) {
            cancellationToken.ThrowIfCancellationRequested();
            var count = source.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            Guard.Require(count > 0, "Bundle input ended early."); offset += count;
        }
        Guard.Require(source.ReadByte() == -1, "Bundle input changed while reading.");
        cancellationToken.ThrowIfCancellationRequested();
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        Guard.Require(string.Equals(hash, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase), "Bundle SHA-256 mismatch.");
        var envelope = UnityFsEnvelope.Read(bytes, cancellationToken);
        var assets = SerializedMetadata.Read(envelope, cancellationToken);
        var byId = assets.ToDictionary(item => item.PathId);
        var semantics = new AssetSemantics(envelope, cancellationToken);
        long decoded = 0;
        foreach (var asset in assets) {
            cancellationToken.ThrowIfCancellationRequested();
            var pointers = new List<AssetPointer>();
            var value = ObjectVisitor.Read(asset, pointers, cancellationToken);
            semantics.Validate(asset, value);
            foreach (var pointer in pointers) {
                if (pointer.PathId == 0) continue;
                Guard.Require(byId.TryGetValue(pointer.PathId, out var target) && target.ClassId != 142, "Dangling or structural object reference.");
                var type = pointer.Type;
                Guard.Require(type == "PPtr<Object>" || ((type == "PPtr<Texture>" || type == "PPtr<Texture2D>") && target.ClassId == 28) || (type == "PPtr<Sprite>" && target.ClassId == 213), "Object reference type mismatch.");
                asset.References.Add(pointer.PathId);
            }
            Guard.Require(asset.Summary.DecodedBytes <= MaximumDecodedBytes - decoded, "Bundle decoded asset budget exceeded.");
            decoded += asset.Summary.DecodedBytes;
        }
        semantics.VerifyResourceCoverage();
        Guard.Require(semantics.Roots.Count == request.Assets.Count, "Public asset index does not match bundle roots.");
        var reachable = new HashSet<long>(); var rootIds = new HashSet<long>(); var usedPreloads = new bool[semantics.Preloads.Count];
        foreach (var expected in request.Assets) {
            cancellationToken.ThrowIfCancellationRequested();
            Guard.Require(semantics.Roots.TryGetValue(expected.EntryName, out var root) && byId.TryGetValue(root.PathId, out var asset) && rootIds.Add(root.PathId), "Missing or aliased public asset.");
            var measured = byId[root.PathId].Summary;
            Guard.Require(measured.ClassId == expected.ClassId, "Public asset class mismatch.");
            if (expected.ClassId == 28 || expected.ClassId == 213) Guard.Require(measured.Width == expected.Width && measured.Height == expected.Height, "Public asset dimensions mismatch.");
            if (expected.ClassId == 83) Guard.Require(measured.AudioFrames == expected.AudioFrames && measured.SampleRate == expected.SampleRate && measured.Channels == expected.Channels, "Public audio metadata mismatch.");
            if (expected.ClassId == 43) Guard.Require(measured.Vertices == expected.Vertices && measured.Triangles == expected.Triangles, "Public mesh metadata mismatch.");
            if (expected.ClassId == 213 && (expected.Vertices != 0 || expected.Triangles != 0)) Guard.Require(measured.Vertices == expected.Vertices && measured.Triangles == expected.Triangles, "Public sprite geometry mismatch.");
            measured.Address = expected.Address; measured.EntryName = expected.EntryName;
            var closure = Closure(root.PathId, byId, cancellationToken); reachable.UnionWith(closure);
            var preloadSet = new HashSet<long>();
            for (var i = root.PreloadIndex; i < root.PreloadIndex + root.PreloadSize; i++) { usedPreloads[i] = true; Guard.Require(preloadSet.Add(semantics.Preloads[i]), "Duplicate preload dependency."); }
            Guard.Require(preloadSet.SetEquals(closure), "Preload range disagrees with asset reference closure.");
        }
        Guard.Require(usedPreloads.All(used => used) && reachable.Count == assets.Count - 1, "Unindexed assets or preload data.");
        foreach (var asset in assets.Where(item => item.ClassId == 213)) {
            var texture = byId[asset.SpriteTexture];
            Guard.Require(texture.ClassId == 28 && asset.Summary.Width == texture.Summary.Width && asset.Summary.Height == texture.Summary.Height, "Sprite texture dependency mismatch.");
            // Include dependent compressed texture blocks in the sprite's conservative transfer charge.
            var ranges = new List<ByteSlice>(asset.Ranges); ranges.AddRange(texture.Ranges);
            asset.Summary.CompressedBytes = envelope.CompressedCost(ranges);
            Guard.Require(asset.Summary.CompressedBytes <= 16L * 1024 * 1024, "Sprite compressed byte budget exceeded.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new ValidatedBundle(bytes, hash, envelope.Expanded.Length, decoded, assets.Where(item => item.ClassId != 142).Select(item => item.Summary).ToList());
    }

    private static HashSet<long> Closure(long root, Dictionary<long, AssetRecord> assets, CancellationToken cancellation) {
        var result = new HashSet<long>(); var pending = new Stack<long>(); pending.Push(root);
        while (pending.Count != 0) {
            cancellation.ThrowIfCancellationRequested(); var path = pending.Pop(); if (!result.Add(path)) continue;
            foreach (var dependency in assets[path].References) pending.Push(dependency);
        }
        return result;
    }

    private static void ValidateRequest(BundleValidationRequest request) {
        Guard.Require(request.ExpectedSha256.Length == 64 && request.ExpectedSha256.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f' || character >= 'A' && character <= 'F'), "Expected hash is invalid.");
        Guard.Require(request.Assets.Count >= 1 && request.Assets.Count <= 4095, "Public asset count exceeds limit.");
        var addresses = new HashSet<string>(StringComparer.Ordinal); var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in request.Assets) {
            Guard.Require(asset != null && IsEntryName(asset.Address) && IsEntryName(asset.EntryName) && addresses.Add(asset.Address) && entries.Add(asset.EntryName), "Invalid or duplicate expected asset.");
            Guard.Require(asset.ClassId == 28 || asset.ClassId == 43 || asset.ClassId == 83 || asset.ClassId == 213, "Unsupported expected asset class.");
            if (asset.ClassId == 28 || asset.ClassId == 213) Guard.Require(asset.Width > 0 && asset.Width <= 4096 && asset.Height > 0 && asset.Height <= 4096, "Invalid expected dimensions.");
            if (asset.ClassId == 83) Guard.Require(asset.AudioFrames > 0 && asset.SampleRate >= 8000 && asset.SampleRate <= 96000 && asset.Channels >= 1 && asset.Channels <= 2 && asset.AudioFrames <= asset.SampleRate * 30L, "Invalid expected audio metadata.");
            if (asset.ClassId == 43) Guard.Require(asset.Vertices >= 3 && asset.Vertices <= 100000 && asset.Triangles >= 1 && asset.Triangles <= 200000, "Invalid expected mesh metadata.");
        }
    }

    internal static bool IsEntryName(string name) {
        if (string.IsNullOrEmpty(name) || name.Length > 256 || name[0] == '/' || name[name.Length - 1] == '/') return false;
        foreach (var character in name) if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') && !(character >= '0' && character <= '9') && character != '/' && character != '-' && character != '_' && character != '.') return false;
        return name.Split('/').All(part => part.Length != 0 && part != "." && part != "..");
    }
}
