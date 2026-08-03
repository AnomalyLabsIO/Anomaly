using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Persists active and rollback pointers as one atomically replaced journal.
/// Catalog bytes remain in the provenance cache and are never partially
/// activated by this store.
/// </summary>
public sealed class FileItemCatalogActivationStore : IItemCatalogActivationStore
{
    private readonly string _statePath;

    public FileItemCatalogActivationStore(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("An activation state path is required.", nameof(statePath));
        _statePath = Path.GetFullPath(statePath);
    }

    public ItemCatalogActivationSnapshot Load()
    {
        if (!File.Exists(_statePath)) return new ItemCatalogActivationSnapshot();
        var json = File.ReadAllText(_statePath, Encoding.UTF8);
        var snapshot = JsonConvert.DeserializeObject<ItemCatalogActivationSnapshot>(json) ??
                       new ItemCatalogActivationSnapshot();
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public void SaveAtomically(ItemCatalogActivationSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        ValidateSnapshot(snapshot);
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_statePath))
            {
                File.Replace(temporaryPath, _statePath, null, true);
            }
            else
            {
                File.Move(temporaryPath, _statePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateSnapshot(ItemCatalogActivationSnapshot snapshot)
    {
        ValidateState(snapshot.Active);
        ValidateState(snapshot.Previous);
    }

    private static void ValidateState(ItemCatalogActivationState state)
    {
        if (state == null) return;
        if (state.SchemaVersion != 1 || state.Catalog == null)
            throw new InvalidDataException("Unsupported or incomplete catalog activation state.");

        var errors = ItemCatalogValidator.Validate(state.Catalog);
        if (errors.Count != 0 ||
            !string.Equals(state.ServerFingerprint, state.Catalog.ServerFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(state.CatalogSha256, state.Catalog.CatalogSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog activation state failed integrity validation.");

        var expected = new HashSet<ItemContentCacheKey>();
        foreach (var asset in state.Catalog.Assets)
            expected.Add(ItemContentCacheKey.Create(
                state.ServerFingerprint,
                state.CatalogSha256,
                asset.Sha256));
        var actual = new HashSet<ItemContentCacheKey>();
        foreach (var key in state.Assets ?? new List<ItemContentCacheKey>())
            actual.Add(key);
        if (!expected.SetEquals(actual))
            throw new InvalidDataException("Catalog activation provenance does not match its manifest.");
    }
}
