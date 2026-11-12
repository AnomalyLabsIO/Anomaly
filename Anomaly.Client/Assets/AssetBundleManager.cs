using System.Security.Cryptography;
using Anomaly.AssetValidation;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using MelonLoader;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Anomaly.Client.Assets;

/// <summary>Owns validated pack bytes, native bundle lifetimes and atomically published address layers.</summary>
internal static class AssetBundleManager
{
    private const int MaximumPacks = BundleCopyBudget.MaximumCopies;
    private const int MaximumIndexBytes = AssetPackIndexCodec.MaxIndexBytes;
    private const long MaximumLiveDecodedBytes = BundleCopyBudget.MaximumBytes;
    private const long MaximumCachedBytes = BundleCopyBudget.MaximumBytes;
    private static readonly object CacheLock = new();
    private static readonly SemaphoreSlim RemoteValidation = new(1, 1);
    private static readonly Dictionary<string, CachedCopy> Cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LoadedPack> NativePacks = new(StringComparer.Ordinal);
    private static readonly List<LoadedPack> FailedNativeCleanup = new();
    private static Layer _local = new();
    private static Layer _remote = new();
    private static Task<PreparedSet> _work;
    private static CancellationTokenSource _cancellation;
    private static PendingOffer _pending;
    private static PendingOffer _running;
    private static long _generation;
    private static bool _initialized;
    private static bool _remoteActive;
    private static bool _cleanupFailed;
    private static string _verifiedRoot;
    private static PreparedPackWarmup<PreparedPack> _warmup;
    private static Dictionary<string, PackOffer> _warmOffers;
    private static string _warmIdentity;

    private sealed class Layer
    {
        internal readonly Dictionary<string, UnityObject> Assets = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, AssetKind> Kinds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, DecorativeVisualDefinition> Visuals = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> VisualTriangles = new(StringComparer.Ordinal);
        internal readonly List<LoadedPack> Packs = new();
    }

    private sealed class CachedCopy
    {
        internal string Key, Path;
        internal FileStream Lock;
        internal long Bytes, DecodedBytes;
        internal int Leases;
    }

    private sealed class PreparedPack : IDisposable
    {
        internal AssetPackIndex Index;
        internal CachedCopy Copy;
        public void Dispose()
        {
            lock (CacheLock)
            {
                if (Copy == null) return;
                Copy.Leases--;
                Copy = null;
            }
        }
    }

    private sealed class PreparedSet : IDisposable
    {
        internal readonly List<PreparedPack> Packs = new();
        public void Dispose() { foreach (var pack in Packs) pack.Dispose(); Packs.Clear(); }
    }

    private sealed class LoadedPack
    {
        internal PreparedPack Prepared;
        internal NativeAssetBundle.Handle Handle;
        internal readonly Dictionary<string, UnityObject> Assets = new(StringComparer.Ordinal);
        internal int Owners;
    }

    private sealed class PackOffer
    {
        internal string PackId, IndexPath, BundlePath, IndexHash, BundleHash;
        internal long IndexSize, BundleSize, DecodedBytes;
    }

    private sealed class PendingOffer
    {
        internal long Generation;
        internal List<PackOffer> Packs;
        internal Action<bool, string> Completed;
        internal PreparedPackWarmup<PreparedPack> Warmup;
    }

    internal static void Initialize()
    {
        if (_initialized) return;
        _verifiedRoot = Path.Combine(Paths.AnomalyPath, "AssetBundleCache", "verified");
        string root;
        try
        {
            Directory.CreateDirectory(_verifiedRoot);
            CleanPriorVerifiedCopies();
            root = Paths.ContentPacksPath;
            Directory.CreateDirectory(root);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("AssetBundles: pack storage unavailable; using stock assets. " + exception.Message);
            return;
        }
        AnomalyResources.SetResolver(Resolve);
        _initialized = true;
        try
        {
            // Parsing and decompression never run on the engine thread. Startup waits
            // for the bounded job so the first preloader can use validated local skins.
            using var prepared = Task.Run(() => PrepareLocal(ContentPackDirectoryReader.ReadPacks(root))).GetAwaiter().GetResult();
            _local = Materialize(prepared, remote: false);
        }
        catch (Exception exception)
        {
            lock (CacheLock) EvictUnused(all: true);
            MelonLogger.Warning("AssetBundles: local pack set rejected; using stock assets. " + exception.Message);
        }
        MelonLogger.Msg($"AssetBundles: loaded {_local.Packs.Count} local pack(s), {_local.Assets.Count} assets.");
    }

    private static void CleanPriorVerifiedCopies()
    {
        string marker = Path.Combine(_verifiedRoot, ".anomaly-owned-cache-v1");
        const string ownership = "Anomaly.AssetBundleManager verified copies v1";
        if (!File.Exists(marker))
        {
            using var writer = new StreamWriter(new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            writer.Write(ownership);
            return; // Never claim or remove pre-existing unmarked files.
        }
        if (File.ReadAllText(marker) != ownership) throw new InvalidDataException("Verified cache ownership marker is invalid.");
        int inspected = 0;
        foreach (string path in Directory.EnumerateFiles(_verifiedRoot, "*.bundle", SearchOption.TopDirectoryOnly))
        {
            if (++inspected > 4096)
                throw new InvalidDataException("Verified cache cleanup exceeded its bounded startup scan.");
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                // Other instances keep their live copies locked against this
                // exclusive open and against deletion; only abandoned copies go.
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static UnityObject Resolve(AssetAddress address, AssetKind kind)
    {
        if (!address.IsValid) return null;
        if (_remoteActive && !address.IsSkin && TryResolve(_remote, address.Value, kind, out var remote)) return remote;
        return TryResolve(_local, address.Value, kind, out var local) ? local : null;
    }

    internal static bool TryGetLocalTexture(string address, out Texture2D texture)
    {
        texture = TryResolve(_local, address, AssetKind.Texture2D, out var asset) ? asset.TryCast<Texture2D>() : null;
        return texture != null;
    }

    internal static bool IsRemoteAsset(AssetAddress address, AssetKind kind) =>
        _remoteActive && address.IsValid && !address.IsSkin && TryResolve(_remote, address.Value, kind, out _);

    internal static UnityObject ResolveRemote(AssetAddress address, AssetKind kind) =>
        IsRemoteAsset(address, kind) ? _remote.Assets[address.Value] : null;

    internal static bool TryGetRemoteVisual(AssetAddress address, out DecorativeVisualDefinition visual)
    {
        visual = null;
        return _remoteActive && _remote.Visuals.TryGetValue(address.Value, out visual);
    }

    internal static long GetRemoteVisualTriangles(AssetAddress address) =>
        _remoteActive && _remote.VisualTriangles.TryGetValue(address.Value, out long triangles) ? triangles : -1;

    private static bool TryResolve(Layer layer, string address, AssetKind kind, out UnityObject asset)
    {
        asset = null;
        return layer.Kinds.TryGetValue(address, out var actual) && actual == kind &&
            layer.Assets.TryGetValue(address, out asset) && asset != null;
    }

    internal static void ActivateRemote(ContentCatalog catalog, Action<bool, string> completed)
    {
        if (!_initialized || _cleanupFailed) throw new InvalidOperationException("The asset bundle runtime requires a clean restart.");
        bool retainWarmup = _warmup != null && _warmIdentity == OfferIdentity(catalog);
        CancelPending(retainWarmup);
        var packs = RemoteOffers(catalog);
        _pending = new PendingOffer { Generation = _generation, Packs = packs, Completed = completed,
            Warmup = retainWarmup ? _warmup : null };
        StartPending();
    }

    // Admission comes from the exact approved offer and verified-file callbacks.
    // This stage never invokes Unity or publishes a remote address layer.
    internal static void BeginRemotePrewarm(ContentCatalog catalog)
    {
        PreflightRemoteBudget(catalog);
        _warmup?.Dispose();
        _warmup = null;
        _warmOffers = null;
        _warmIdentity = null;
        if (catalog.Packs.Count == 0) return;
        if (!_initialized || _cleanupFailed) throw new InvalidOperationException("The asset bundle runtime requires a clean restart.");
        var offers = new Dictionary<string, PackOffer>(StringComparer.Ordinal);
        foreach (var offer in RemoteOffers(catalog)) offers.Add(offer.PackId, offer);
        _warmOffers = offers;
        _warmIdentity = OfferIdentity(catalog);
        _warmup = new PreparedPackWarmup<PreparedPack>();
    }

    internal static void PreflightRemoteBudget(ContentCatalog catalog)
    {
        // Native/local ownership is changed only on the game thread. Read its
        // immutable copy identities without waiting on a worker's cache-copy I/O.
        var local = new List<KeyValuePair<string, long>>();
        foreach (var pack in _local.Packs)
        {
            var copy = pack.Prepared.Copy;
            local.Add(new KeyValuePair<string, long>(copy.Key, copy.Bytes));
        }
        var offered = new List<KeyValuePair<string, long>>();
        var localDecoded = new List<BundleCopyBudget.DecodedDemand>();
        foreach (var pack in NativePacks.Values)
            localDecoded.Add(new BundleCopyBudget.DecodedDemand(pack.Prepared.Index.BundleSha256,
                pack.Prepared.Copy.Key, pack.Prepared.Copy.DecodedBytes));
        var offeredDecoded = new List<BundleCopyBudget.DecodedDemand>();
        foreach (var pack in catalog.Packs)
        {
            string bundleHash = FileCache.ToHex(pack.BundleHash);
            string copyKey = CacheKey(bundleHash, FileCache.ToHex(pack.IndexHash));
            offered.Add(new KeyValuePair<string, long>(
                copyKey, pack.BundleSize));
            offeredDecoded.Add(new BundleCopyBudget.DecodedDemand(bundleHash, copyKey, pack.DecodedBytes));
        }
        BundleCopyBudget.Validate(local, offered);
        BundleCopyBudget.ValidateDecoded(localDecoded, offeredDecoded);
    }

    internal static void PrewarmRemotePack(string packId)
    {
        if (_warmup == null || _warmOffers == null || !_warmOffers.TryGetValue(packId, out var offer))
            throw new InvalidOperationException("No approved bundle prewarm owns this pack.");
        _warmup.Schedule(packId, token => PrepareRemotePack(offer, token));
    }

    private static string OfferIdentity(ContentCatalog catalog) =>
        FileCache.ToHex(catalog.CatalogSha256) + ":" + FileCache.ToHex(catalog.ManifestSha256);

    private static List<PackOffer> RemoteOffers(ContentCatalog catalog)
    {
        var packs = catalog.Packs.Select(pack => new PackOffer {
            PackId = pack.PackId, IndexPath = FileCache.GetFilePath(pack.IndexHash),
            BundlePath = FileCache.GetFilePath(pack.BundleHash), IndexHash = FileCache.ToHex(pack.IndexHash),
            BundleHash = FileCache.ToHex(pack.BundleHash), IndexSize = pack.IndexSize, BundleSize = pack.BundleSize,
            DecodedBytes = pack.DecodedBytes,
        }).ToList();
        if (packs.Count > MaximumPacks) throw new InvalidDataException("Too many remote asset packs.");
        return packs;
    }

    private static void StartPending()
    {
        if (_work != null || _pending == null) return;
        _running = _pending;
        _pending = null;
        _cancellation = new CancellationTokenSource();
        var request = _running;
        var token = _cancellation.Token;
        // Always enter the delegate so cancelled pending work still retires its warm leases.
        _work = Task.Run(() => PrepareRemote(request.Packs, token, request.Warmup));
    }

    internal static void Update()
    {
        if (_work == null || !_work.IsCompleted) return;
        var work = _work;
        var request = _running;
        _work = null;
        _running = null;
        _cancellation.Dispose();
        _cancellation = null;
        try
        {
            using var prepared = work.GetAwaiter().GetResult();
            if (request.Generation != _generation) return;
            if (_remote.Packs.Count != 0) throw new InvalidOperationException("Previous remote packs are still active.");
            var staged = Materialize(prepared, remote: true);
            _remote = staged; // One publication after every pack and native asset succeeded.
            _remoteActive = true;
            Anomaly.Client.Api.Features.Announcer.RefreshBundleWords();
            request.Completed(true, null);
        }
        catch (Exception exception)
        {
            if (request.Generation == _generation)
                request.Completed(false, exception.Message);
        }
        finally { StartPending(); }
    }

    private static void CancelPending(bool retainWarmup = false)
    {
        checked { _generation++; }
        _pending = null;
        _cancellation?.Cancel();
        if (!retainWarmup)
        {
            _warmup?.Dispose();
            _warmup = null;
            _warmOffers = null;
            _warmIdentity = null;
        }
    }

    // Call only after restoring all consumers of remote assets.
    internal static void ClearRemote()
    {
        RevokeRemote();
        var previous = _remote;
        _remote = new Layer();
        try { Release(previous); }
        catch { _cleanupFailed = true; throw; }
    }

    internal static void RevokeRemote()
    {
        _remoteActive = false;
        CancelPending();
        Anomaly.Client.Api.Features.Announcer.RefreshBundleWords();
    }

    internal static void StopRemoteAudio()
        => StopAudio(_remote);

    private static void StopAudio(Layer layer)
    {
        var clips = new HashSet<int>(layer.Assets.Where(pair => layer.Kinds[pair.Key] == AssetKind.AudioClip && pair.Value != null)
            .Select(pair => pair.Value.GetInstanceID()));
        if (clips.Count == 0) return;
        foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (source == null || source.clip == null || !clips.Contains(source.clip.GetInstanceID())) continue;
            source.Stop();
            source.clip = null;
        }
    }

    internal static void Shutdown()
    {
        ClearRemote();
        Anomaly.Client.Patches.Audio.CassieLineSourceSchedulePatch.RestoreStockAll();
        Anomaly.Client.Api.Features.Announcer.ClearBundleWords();
        StopAudio(_local);
        var previous = _local;
        _local = new Layer();
        Release(previous);
        AnomalyResources.SetResolver(null);
        _initialized = false;
        // A cancelled worker may still hold a lease; it disposes its partial
        // result before completion. Process exit closes remaining read locks.
        lock (CacheLock) EvictUnused(all: true);
    }

    private static PreparedSet PrepareLocal(IEnumerable<LocalContentPack> packs)
    {
        var set = new PreparedSet();
        try
        {
            foreach (var pack in packs)
            {
                try
                {
                    var contents = pack.Contents;
                    if (contents.AssetIndex == null) continue;
                    string bundle = pack.Resolve(contents.BundlePath);
                    set.Packs.Add(PreparePack(contents.AssetIndex, Hash(contents.AssetIndexBytes), bundle, CancellationToken.None));
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException($"Local pack '{pack.Contents.Descriptor.PackId}' rejected the complete local layer: {exception.Message}", exception);
                }
            }
            return set;
        }
        catch { set.Dispose(); throw; }
    }

    private static PreparedSet PrepareRemote(IEnumerable<PackOffer> offers, CancellationToken token,
        PreparedPackWarmup<PreparedPack> warmup)
    {
        var set = new PreparedSet();
        try
        {
            foreach (var offer in offers)
            {
                token.ThrowIfCancellationRequested();
                set.Packs.Add(warmup?.Take(offer.PackId, token) ?? PrepareRemotePack(offer, token));
            }
            token.ThrowIfCancellationRequested();
            return set;
        }
        catch { set.Dispose(); throw; }
        finally { warmup?.Dispose(); }
    }

    private static PreparedPack PrepareRemotePack(PackOffer offer, CancellationToken token)
    {
        bool entered = false;
        try
        {
            // A cancelled generation may still be unwinding a validator. Keep
            // aggregate decoding/copy memory bounded across offer replacements.
            RemoteValidation.Wait(token);
            entered = true;
            byte[] bytes = ReadIndex(offer.IndexPath, offer.IndexHash, offer.IndexSize);
            var index = AssetPackIndexCodec.Parse(bytes, remote: true);
            if (index.PackId != offer.PackId || index.BundleSha256 != offer.BundleHash || index.BundleBytes != offer.BundleSize ||
                index.DecodedBytes != offer.DecodedBytes)
                throw new InvalidDataException("The pack index disagreed with its exact authenticated offer.");
            return PreparePack(index, offer.IndexHash, offer.BundlePath, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        { throw new InvalidDataException($"Pack '{offer.PackId}' validation failed: {exception.Message}", exception); }
        finally { if (entered) RemoteValidation.Release(); }
    }

    private static PreparedPack PreparePack(AssetPackIndex index, string indexHash, string bundlePath, CancellationToken token)
    {
        string key = CacheKey(index.BundleSha256, indexHash);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                BundleCopyBudget.ValidateMeasurement(index.DecodedBytes, cached.DecodedBytes);
                cached.Leases++;
                return new PreparedPack { Index = index, Copy = cached };
            }
        }
        var expectations = index.Assets.Select(asset => new BundleAssetExpectation(asset.Address, asset.EntryName,
            asset.AssetKind switch { AssetKind.Texture2D => 28, AssetKind.Sprite => 213, AssetKind.AudioClip => 83, AssetKind.Mesh => 43, _ => 0 },
            asset.Width, asset.Height, asset.SampleFrames, asset.SampleRate, asset.Channels, asset.VertexCount, asset.TriangleCount));
        using var input = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != index.BundleBytes) throw new InvalidDataException("Bundle length disagreed with the index.");
        using var verified = BundleValidator.Validate(input, new BundleValidationRequest(index.BundleSha256, expectations), token);
        BundleCopyBudget.ValidateMeasurement(index.DecodedBytes, verified.TotalDecodedBytes);
        lock (CacheLock)
        {
            token.ThrowIfCancellationRequested();
            // A retired offer and its replacement can finish validation concurrently.
            // Reuse the winning protected copy rather than adding the same key twice.
            if (Cache.TryGetValue(key, out var existing))
            {
                BundleCopyBudget.ValidateMeasurement(index.DecodedBytes, existing.DecodedBytes);
                existing.Leases++;
                return new PreparedPack { Index = index, Copy = existing };
            }
            EvictUnused(all: false);
            if (Cache.Count >= MaximumPacks || Cache.Values.Sum(copy => copy.Bytes) + verified.BundleLength > MaximumCachedBytes)
                throw new InvalidDataException("The validated bundle cache budget is exhausted.");
            string path = Path.Combine(_verifiedRoot, Guid.NewGuid().ToString("N") + ".bundle");
            FileStream protectedFile = null;
            try
            {
                using (var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    verified.WriteVerifiedBytes(writer, token);
                    writer.Flush(true);
                }
                protectedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                // Recheck under the permanent read lock: no modification during
                // the writer-to-reader transition can reach the native loader.
                using (var sha = SHA256.Create())
                    if (protectedFile.Length != verified.BundleLength ||
                        Convert.ToHexString(sha.ComputeHash(protectedFile)).ToLowerInvariant() != verified.Sha256)
                        throw new InvalidDataException("Validated bundle copy changed before its read lock was acquired.");
                protectedFile.Position = 0;
                token.ThrowIfCancellationRequested();
                var copy = new CachedCopy { Key = key, Path = path, Lock = protectedFile,
                    Bytes = verified.BundleLength, DecodedBytes = verified.TotalDecodedBytes, Leases = 1 };
                Cache.Add(key, copy);
                return new PreparedPack { Index = index, Copy = copy };
            }
            catch
            {
                protectedFile?.Dispose();
                try { File.Delete(path); } catch { }
                throw;
            }
        }
    }

    private static string CacheKey(string bundleHash, string indexHash)
    {
        var validator = typeof(BundleValidator).Assembly;
        return BundleValidator.ProfileId + ":" + validator.GetName().Version + ":" +
            validator.ManifestModule.ModuleVersionId.ToString("N") + ":" + AssetPackIndex.SupportedGameProfile +
            ":" + bundleHash + ":" + indexHash;
    }

    private static void EvictUnused(bool all)
    {
        foreach (var pair in Cache.ToArray())
        {
            if (pair.Value.Leases != 0) continue;
            if (!all && Cache.Count < MaximumPacks && Cache.Values.Sum(copy => copy.Bytes) < MaximumCachedBytes / 2) break;
            pair.Value.Lock.Dispose();
            try { File.Delete(pair.Value.Path); } catch { }
            Cache.Remove(pair.Key);
        }
    }

    private static byte[] ReadIndex(string path, string expectedHash, long expectedSize)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length <= 0 || input.Length > MaximumIndexBytes || (expectedSize != 0 && input.Length != expectedSize))
            throw new InvalidDataException("Pack index length exceeded its bounded offer.");
        var bytes = new byte[checked((int)input.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = input.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        if (expectedHash != null && Hash(bytes) != expectedHash) throw new InvalidDataException("Pack index SHA256 mismatch.");
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Layer Materialize(PreparedSet prepared, bool remote)
    {
        var layer = new Layer();
        try
        {
            PreflightLayer(prepared);
            var packIds = new HashSet<string>(StringComparer.Ordinal);
            long activeBytes = NativePacks.Values.Sum(pack => pack.Prepared.Copy.DecodedBytes);
            foreach (var candidate in prepared.Packs.ToArray())
            {
                if (!packIds.Add(candidate.Index.PackId)) throw new InvalidDataException("Duplicate pack id in one layer.");
                LoadedPack pack;
                string hash = candidate.Index.BundleSha256;
                if (NativePacks.TryGetValue(hash, out pack))
                {
                    if (pack.Prepared.Copy.Key != candidate.Copy.Key) throw new InvalidDataException("One bundle was offered with inconsistent indexes.");
                    candidate.Dispose();
                    prepared.Packs.Remove(candidate);
                }
                else
                {
                    activeBytes = checked(activeBytes + candidate.Copy.DecodedBytes);
                    if (activeBytes > MaximumLiveDecodedBytes) throw new InvalidDataException("Combined native asset budget exceeded.");
                    pack = new LoadedPack { Prepared = candidate };
                    try
                    {
                        pack.Handle = NativeAssetBundle.LoadFromFile(candidate.Copy.Path)
                            ?? throw new InvalidDataException("Unity rejected a validated bundle.");
                        foreach (var entry in candidate.Index.Assets)
                            pack.Assets.Add(entry.Address, LoadAsset(pack.Handle, entry));
                        NativePacks.Add(hash, pack);
                        prepared.Packs.Remove(candidate);
                    }
                    catch
                    {
                        try { if (pack.Handle != null) NativeAssetBundle.Unload(pack.Handle, true); }
                        catch
                        {
                            _cleanupFailed = true;
                            FailedNativeCleanup.Add(pack);
                            prepared.Packs.Remove(candidate); // Retain the immutable file until process exit.
                        }
                        throw;
                    }
                }
                pack.Owners++;
                layer.Packs.Add(pack);
                foreach (var entry in pack.Prepared.Index.Assets)
                {
                    var address = AssetAddress.Parse(entry.Address);
                    if (remote && address.IsSkin) throw new InvalidDataException("Server packs cannot replace local UI skins.");
                    layer.Assets.Add(entry.Address, pack.Assets[entry.Address]);
                    layer.Kinds.Add(entry.Address, entry.AssetKind);
                }
                foreach (var visual in pack.Prepared.Index.Visuals)
                {
                    layer.Visuals.Add(visual.Address, visual);
                    layer.VisualTriangles.Add(visual.Address, AssetPackIndexCodec.GetTriangleCount(pack.Prepared.Index, visual));
                }
            }
            return layer;
        }
        catch { Release(layer); throw; }
    }

    private static void PreflightLayer(PreparedSet prepared)
    {
        var packs = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(NativePacks.Keys, StringComparer.Ordinal);
        long decoded = NativePacks.Values.Sum(pack => pack.Prepared.Copy.DecodedBytes);
        foreach (var pack in prepared.Packs)
        {
            if (!packs.Add(pack.Index.PackId)) throw new InvalidDataException("Duplicate pack id in one layer.");
            foreach (var asset in pack.Index.Assets)
                if (!addresses.Add(asset.Address)) throw new InvalidDataException("Duplicate asset address in one layer: " + asset.Address);
            foreach (var visual in pack.Index.Visuals)
                if (!addresses.Add(visual.Address)) throw new InvalidDataException("Duplicate visual address in one layer: " + visual.Address);
            if (NativePacks.TryGetValue(pack.Index.BundleSha256, out var loaded) && loaded.Prepared.Copy.Key != pack.Copy.Key)
                throw new InvalidDataException("One bundle was offered with inconsistent indexes.");
            if (hashes.Add(pack.Index.BundleSha256)) decoded = checked(decoded + pack.Copy.DecodedBytes);
            if (decoded > MaximumLiveDecodedBytes) throw new InvalidDataException("Combined native asset budget exceeded.");
        }
    }

    private static UnityObject LoadAsset(NativeAssetBundle.Handle handle, PackAssetEntry entry)
    {
        UnityObject asset = entry.AssetKind switch {
            AssetKind.Texture2D => NativeAssetBundle.LoadAsset<Texture2D>(handle, entry.EntryName),
            AssetKind.Sprite => NativeAssetBundle.LoadAsset<Sprite>(handle, entry.EntryName),
            AssetKind.AudioClip => NativeAssetBundle.LoadAsset<AudioClip>(handle, entry.EntryName),
            AssetKind.Mesh => NativeAssetBundle.LoadAsset<Mesh>(handle, entry.EntryName),
            _ => throw new InvalidDataException("Unsupported native asset kind."),
        };
        if (asset == null) throw new InvalidDataException("A validated named asset could not be loaded: " + entry.Address);
        if (entry.AssetKind == AssetKind.Texture2D)
        {
            var texture = asset.Cast<Texture2D>();
            if (texture.width != entry.Width || texture.height != entry.Height) throw new InvalidDataException("Native texture dimensions disagreed.");
        }
        if (entry.AssetKind == AssetKind.AudioClip)
        {
            var clip = asset.Cast<AudioClip>();
            if (clip.samples != entry.SampleFrames || clip.frequency != entry.SampleRate || clip.channels != entry.Channels)
                throw new InvalidDataException("Native audio metadata disagreed.");
        }
        if (entry.AssetKind == AssetKind.Sprite)
        {
            var sprite = asset.Cast<Sprite>();
            if (sprite.texture == null || sprite.texture.width != entry.Width || sprite.texture.height != entry.Height ||
                sprite.rect.width != entry.Width || sprite.rect.height != entry.Height)
                throw new InvalidDataException("Native sprite dimensions disagreed.");
        }
        if (entry.AssetKind == AssetKind.Mesh)
        {
            var mesh = asset.Cast<Mesh>();
            if (mesh.vertexCount != entry.VertexCount || mesh.triangles.Length != entry.TriangleCount * 3)
                throw new InvalidDataException("Native mesh topology disagreed.");
        }
        return asset;
    }

    private static void Release(Layer layer)
    {
        layer.Assets.Clear(); layer.Kinds.Clear(); layer.Visuals.Clear(); layer.VisualTriangles.Clear();
        foreach (var pack in layer.Packs)
        {
            if (--pack.Owners != 0) continue;
            // Keep the immutable file lease if Unity refuses unloading, so no
            // still-live native bundle can observe a deleted/replaced input.
            try { NativeAssetBundle.Unload(pack.Handle, true); }
            catch { _cleanupFailed = true; throw; }
            NativePacks.Remove(pack.Prepared.Index.BundleSha256);
            pack.Prepared.Dispose();
        }
        layer.Packs.Clear();
    }
}
