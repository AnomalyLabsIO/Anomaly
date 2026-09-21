using System.Text;
using Anomaly.Client.Api.Features;
using MelonLoader;
using Newtonsoft.Json;

namespace Anomaly.Client.Content;

/// <summary>
/// Keeps approval provenance separate from the shared content-addressed blob cache.
/// A corrupt store is preserved and disables approval rather than silently widening trust.
/// </summary>
internal static class ContentConsentStore
{
    private sealed class Document
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Record> Records { get; set; } = new();
    }

    private sealed class Record
    {
        public string ServerFingerprint { get; set; }
        public string CatalogSha256 { get; set; }
        public string ManifestSha256 { get; set; }
        public DateTime ApprovedUtc { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly string StorePath = Path.Combine(Paths.AnomalyPath, "content-consent.json");
    private static Document _document = new();
    private static bool _available;

    internal static void Init()
    {
        lock (Sync)
        {
            if (!File.Exists(StorePath))
            {
                _document = new Document();
                _available = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(StorePath, Encoding.UTF8);
                var parsed = JsonConvert.DeserializeObject<Document>(json);
                if (parsed == null || parsed.SchemaVersion != 1 || parsed.Records == null)
                    throw new InvalidDataException("Unsupported or incomplete content-consent document.");

                foreach (Record record in parsed.Records)
                {
                    if (!IsFingerprint(record.ServerFingerprint) ||
                        !IsSha256(record.CatalogSha256) ||
                        !IsSha256(record.ManifestSha256))
                        throw new InvalidDataException("Content-consent document contained an invalid record.");
                }

                _document = parsed;
                _available = true;
            }
            catch (Exception exception)
            {
                _document = new Document();
                _available = false;
                MelonLogger.Error(
                    $"Content consent is unavailable; '{StorePath}' was preserved and remote content will fail closed: {exception.Message}");
            }
        }
    }

    internal static bool IsApproved(string serverFingerprint, byte[] catalogSha256, byte[] manifestSha256)
    {
        string catalog = ToHex(catalogSha256);
        string manifest = ToHex(manifestSha256);
        lock (Sync)
        {
            return _available && _document.Records.Any(record =>
                string.Equals(record.ServerFingerprint, serverFingerprint, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.CatalogSha256, catalog, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.ManifestSha256, manifest, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static bool TryRemember(string serverFingerprint, byte[] catalogSha256, byte[] manifestSha256)
    {
        string catalog = ToHex(catalogSha256);
        string manifest = ToHex(manifestSha256);
        if (!IsFingerprint(serverFingerprint) || !IsSha256(catalog) || !IsSha256(manifest))
            return false;

        lock (Sync)
        {
            if (!_available)
                return false;
            if (_document.Records.Any(record =>
                    string.Equals(record.ServerFingerprint, serverFingerprint, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.CatalogSha256, catalog, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.ManifestSha256, manifest, StringComparison.OrdinalIgnoreCase)))
                return true;

            _document.Records.Add(new Record
            {
                ServerFingerprint = serverFingerprint,
                CatalogSha256 = catalog,
                ManifestSha256 = manifest,
                ApprovedUtc = DateTime.UtcNow,
            });

            try
            {
                SaveAtomic();
                return true;
            }
            catch (Exception exception)
            {
                _document.Records.RemoveAt(_document.Records.Count - 1);
                MelonLogger.Error($"Content consent could not be saved atomically: {exception.Message}");
                return false;
            }
        }
    }

    private static void SaveAtomic()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
        string temporaryPath = StorePath + ".tmp";
        string backupPath = StorePath + ".bak";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_document, Formatting.Indented));
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        if (File.Exists(StorePath))
            File.Replace(temporaryPath, StorePath, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(temporaryPath, StorePath);
    }

    private static bool IsFingerprint(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128;

    private static bool IsSha256(string value) =>
        value != null && value.Length == 64 && value.All(IsHex);

    private static bool IsHex(char value) =>
        (value >= '0' && value <= '9') ||
        (value >= 'a' && value <= 'f') ||
        (value >= 'A' && value <= 'F');

    private static string ToHex(byte[] hash) =>
        hash == null ? string.Empty : Convert.ToHexString(hash).ToLowerInvariant();
}
