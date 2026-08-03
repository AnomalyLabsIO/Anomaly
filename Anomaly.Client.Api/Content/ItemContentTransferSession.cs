using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// A consent-bound, disk-backed receiver for one passive item catalog. Assets
/// must be explicitly requested before chunks are accepted. Chunks are
/// sequential so a partial file has an unambiguous, resumable offset.
/// </summary>
public sealed class ItemContentTransferSession : IDisposable
{
    private readonly ItemCatalog _catalog;
    private readonly string _cacheRoot;
    private readonly Dictionary<Guid, ActiveRequest> _requests = new();
    private readonly Dictionary<string, Guid> _assetRequests = new(StringComparer.Ordinal);
    private bool _disposed;

    public ItemContentTransferSession(
        ItemCatalog catalog,
        string authenticatedServerFingerprint,
        ItemCatalogConsentRecord consent,
        string cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot))
            throw new ArgumentException("A cache root is required.", nameof(cacheRoot));
        ItemCatalogGuard.ValidateAuthenticatedCatalog(catalog, authenticatedServerFingerprint, consent);

        _catalog = catalog;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(_cacheRoot);
    }

    public ItemContentRequest RequestAsset(string assetKey)
    {
        ThrowIfDisposed();
        var asset = FindManifestAsset(assetKey);
        if (_assetRequests.TryGetValue(assetKey, out var existingId))
            return ToPublicRequest(_requests[existingId]);

        var cacheKey = GetCacheKey(asset);
        var finalPath = GetFullCachePath(cacheKey);
        if (IsCompleteFile(finalPath, asset))
        {
            return new ItemContentRequest
            {
                RequestId = Guid.Empty,
                AssetKey = asset.Key,
                ResumeOffset = asset.SizeBytes,
                ExpectedSize = asset.SizeBytes,
                IsAlreadyAvailable = true,
            };
        }

        if (File.Exists(finalPath)) File.Delete(finalPath);
        var partialPath = finalPath + ".part";
        var resumeOffset = GetSafeResumeOffset(partialPath, asset.SizeBytes);
        var request = new ActiveRequest(Guid.NewGuid(), asset, partialPath, resumeOffset);
        _requests.Add(request.RequestId, request);
        _assetRequests.Add(asset.Key, request.RequestId);
        return ToPublicRequest(request);
    }

    public long AcceptChunk(Guid requestId, string assetKey, long offset, ReadOnlyMemory<byte> data)
    {
        ThrowIfDisposed();
        if (!_requests.TryGetValue(requestId, out var request) ||
            !string.Equals(request.Asset.Key, assetKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Chunk does not match an active manifest asset request.");
        if (data.Length <= 0 || data.Length > ItemContentLimits.MaxTransferChunkBytes)
            throw new InvalidOperationException("Chunk size is outside the public-v1 limit.");
        if (offset != request.Offset)
            throw new InvalidOperationException("Chunk offset does not match the requested resume position.");
        if (offset < 0 || offset > request.Asset.SizeBytes ||
            data.Length > request.Asset.SizeBytes - offset)
            throw new InvalidOperationException("Chunk exceeds the manifest-declared asset size.");

        Directory.CreateDirectory(Path.GetDirectoryName(request.PartialPath)!);
        using (var stream = new FileStream(
                   request.PartialPath,
                   FileMode.OpenOrCreate,
                   FileAccess.Write,
                   FileShare.None,
                   ItemContentLimits.MaxTransferChunkBytes,
                   FileOptions.WriteThrough))
        {
            if (stream.Length != request.Offset)
                throw new IOException("Partial content changed outside the active request.");
            stream.Position = request.Offset;
            stream.Write(data.Span);
            stream.Flush(true);
        }

        request.Offset += data.Length;
        return request.Offset;
    }

    public ItemContentCompletionStatus CompleteAsset(Guid requestId, string assetKey)
    {
        ThrowIfDisposed();
        if (!_requests.TryGetValue(requestId, out var request) ||
            !string.Equals(request.Asset.Key, assetKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Completion does not match an active manifest asset request.");
        if (request.Offset != request.Asset.SizeBytes)
            return ItemContentCompletionStatus.Incomplete;

        if (!File.Exists(request.PartialPath) ||
            !FileHashMatches(request.PartialPath, request.Asset.Sha256, request.Asset.SizeBytes))
        {
            DeleteIfExists(request.PartialPath);
            RemoveRequest(request);
            return ItemContentCompletionStatus.HashMismatch;
        }

        var finalPath = GetFullCachePath(GetCacheKey(request.Asset));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(finalPath))
        {
            if (!IsCompleteFile(finalPath, request.Asset))
                File.Delete(finalPath);
            else
            {
                File.Delete(request.PartialPath);
                RemoveRequest(request);
                return ItemContentCompletionStatus.Complete;
            }
        }

        File.Move(request.PartialPath, finalPath);
        RemoveRequest(request);
        return ItemContentCompletionStatus.Complete;
    }

    public bool IsAssetAvailable(string assetKey)
    {
        ThrowIfDisposed();
        var asset = FindManifestAsset(assetKey);
        return IsCompleteFile(GetFullCachePath(GetCacheKey(asset)), asset);
    }

    public ItemContentCacheKey GetCacheKey(string assetKey)
    {
        ThrowIfDisposed();
        return GetCacheKey(FindManifestAsset(assetKey));
    }

    public string GetCachePath(string assetKey)
    {
        ThrowIfDisposed();
        return GetFullCachePath(GetCacheKey(FindManifestAsset(assetKey)));
    }

    public void Dispose()
    {
        _disposed = true;
        _requests.Clear();
        _assetRequests.Clear();
    }

    private PassiveAssetReference FindManifestAsset(string assetKey)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
            throw new ArgumentException("Asset key is required.", nameof(assetKey));
        var asset = _catalog.Assets.SingleOrDefault(value =>
            string.Equals(value.Key, assetKey, StringComparison.Ordinal));
        return asset ?? throw new InvalidOperationException("Asset is not declared by the approved catalog manifest.");
    }

    private ItemContentCacheKey GetCacheKey(PassiveAssetReference asset) => ItemContentCacheKey.Create(
        _catalog.ServerFingerprint,
        _catalog.CatalogSha256,
        asset.Sha256);

    private string GetFullCachePath(ItemContentCacheKey key)
    {
        var path = Path.GetFullPath(Path.Combine(_cacheRoot, key.GetRelativePath()));
        var rootPrefix = _cacheRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? _cacheRoot
            : _cacheRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Content cache path escaped its configured root.");
        return path;
    }

    private static long GetSafeResumeOffset(string partialPath, long expectedSize)
    {
        if (!File.Exists(partialPath)) return 0;
        var length = new FileInfo(partialPath).Length;
        if (length < 0 || length > expectedSize)
        {
            File.Delete(partialPath);
            return 0;
        }
        return length;
    }

    private static bool IsCompleteFile(string path, PassiveAssetReference asset) =>
        FileHashMatches(path, asset.Sha256, asset.SizeBytes);

    private static bool FileHashMatches(string path, string expectedSha256, long expectedSize)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length != expectedSize) return false;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();
        var actual = sha256.ComputeHash(stream);
        ItemCatalogCanonicalizer.TryDecodeSha256(expectedSha256, out var expected);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static ItemContentRequest ToPublicRequest(ActiveRequest request) => new()
    {
        RequestId = request.RequestId,
        AssetKey = request.Asset.Key,
        ResumeOffset = request.Offset,
        ExpectedSize = request.Asset.SizeBytes,
    };

    private void RemoveRequest(ActiveRequest request)
    {
        _requests.Remove(request.RequestId);
        _assetRequests.Remove(request.Asset.Key);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ItemContentTransferSession));
    }

    private sealed class ActiveRequest
    {
        public Guid RequestId { get; }
        public PassiveAssetReference Asset { get; }
        public string PartialPath { get; }
        public long Offset { get; set; }

        public ActiveRequest(Guid requestId, PassiveAssetReference asset, string partialPath, long offset)
        {
            RequestId = requestId;
            Asset = asset;
            PartialPath = partialPath;
            Offset = offset;
        }
    }
}
