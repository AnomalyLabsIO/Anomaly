using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Anomaly.Shared.Assets;
using Anomaly.Server.Utils;

namespace Anomaly.Server.FileTransfer;

internal static class ServerAssetManager
{
    private static readonly object Sync = new object();
    private static AssetPackSnapshot _current = AssetPackSnapshot.Empty();
    private static string _directory;
    private static int _epoch;
    private static bool _scanning, _rescan, _ready;
    internal static bool IsReady { get { lock (Sync) return _ready; } }
    public static void Init(string directory) { Reset(); lock (Sync) _directory = directory; Rescan(); }
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
                    previous = _current; _current = candidate; candidate = null; _ready = true;
                }
                previous.Dispose();
                OffThreadLogger.Info($"Asset packs: published {fileCount} immutable files ({totalBytes} bytes), worker indexing/copy {elapsed.ElapsedMilliseconds} ms; bundles were not decompressed.");
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                lock (Sync) { if (epoch == _epoch) _ready = true; }
                OffThreadLogger.Warn("Asset packs: retaining previous generation after scan failure: " + exception.Message);
            }
        }
    }
    internal static AssetPackSnapshot AcquireSnapshot() { lock (Sync) return _current.Retain(); }
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
