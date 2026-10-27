using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Anomaly.Shared.Assets;
using Anomaly.Server.Utils;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Content;

namespace Anomaly.Server.FileTransfer;

internal static class ServerAssetManager
{
    // Publication, embedded registration and native definition freeze share this gate.
    internal static readonly object Sync = new object();
    private static AssetPackSnapshot _current = AssetPackSnapshot.Empty();
    private static string _directory;
    private static int _epoch;
    private static bool _scanning, _rescan, _ready;
    internal static bool IsReady { get { lock (Sync) return _ready && _current.ContentRegistrationRevision == ContentPacks.Revision; } }
    public static void Init(string directory)
    { Reset(); lock (Sync) _directory = directory; Rescan(); }
    public static void Reset()
    {
        AssetPackSnapshot previous;
        lock (Sync) { _epoch++; _directory = null; _ready = _rescan = _scanning = false; previous = _current; _current = AssetPackSnapshot.Empty(); }
        previous.Dispose();
    }
    public static void Rescan()
    {
        lock (Sync)
        {
            _rescan = true;
            if (_scanning) return;
            _scanning = true;
            int epoch = _epoch;
            Task.Run(() => ScanWorker(epoch));
        }
    }
    internal static void InvalidateContentRegistrations()
    {
        lock (Sync) _ready = false;
        Rescan();
    }
    private static void ScanWorker(int epoch)
    {
        while (true)
        {
            string directory;
            lock (Sync)
            {
                if (epoch != _epoch) return;
                if (!_rescan) { _scanning = false; return; }
                _rescan = false; directory = _directory;
            }
            AssetPackSnapshot candidate = null;
            var elapsed = Stopwatch.StartNew();
            try
            {
                candidate = AssetPackSnapshot.Build(directory);
                int fileCount = candidate.Files.Count;
                long totalBytes = candidate.Files.Values.Sum(file => (long)file.Size);
                AssetPackSnapshot previous;
                lock (Sync)
                {
                    if (epoch != _epoch) { candidate.Dispose(); return; }
                    if (candidate.ContentRegistrationRevision != ContentPacks.Revision)
                    {
                        candidate.Dispose(); candidate = null; _rescan = true;
                        continue;
                    }
                    ContentRegistry.ValidateSnapshotDefinitions(candidate);
                    previous = _current; _current = candidate; candidate = null; _ready = true;
                }
                previous.Dispose();
                OffThreadLogger.Info($"Asset packs: published {fileCount} immutable files ({totalBytes} bytes), worker indexing/copy {elapsed.ElapsedMilliseconds} ms; bundles were not decompressed.");
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                OffThreadLogger.Warn("Asset packs: retaining previous generation after scan failure: " + exception.Message);
            }
        }
    }
    internal static AssetPackSnapshot AcquireSnapshot() { lock (Sync) return _current.Retain(); }
    internal static void ValidateFreezeSnapshot(AssetPackSnapshot snapshot)
    {
        if (snapshot.ContentRegistrationRevision != ContentPacks.Revision)
            throw new InvalidOperationException("Content registrations changed before the offer could freeze.");
        // A retained first-offer snapshot must still match the publishable definitions.
        if (_ready && !ContentRegistry.SameDefinitions(_current.Items, snapshot.Items))
            throw new InvalidOperationException("Content definitions changed before the first offer could freeze.");
    }
    internal static bool TryAcquireOfferSnapshot(out AssetPackSnapshot snapshot)
    {
        lock (Sync)
        {
            snapshot = null;
            if (!_ready || _current.ContentRegistrationRevision != ContentPacks.Revision) return false;
            ContentRegistry.ValidateSnapshotDefinitions(_current);
            snapshot = _current.Retain();
            return true;
        }
    }
    internal static bool TryGetAsset(AssetAddress address, out AssetPackSnapshot.AssetRecord record) { lock (Sync) return _current.TryGetAsset(address, out record); }
    internal static byte[] ComputeCurrentManifestSha256() { using (var snapshot = AcquireSnapshot()) return snapshot.BuildManifest(1).ManifestSha256; }
    internal static string ToHex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    internal struct AssetInfo
    {
        public string FullPath, RelativePath;
        public byte[] Hash;
        public uint Size;
        public FileContentKind Kind;
    }
}
