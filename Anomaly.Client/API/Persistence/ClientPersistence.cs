using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.Internal;
using Anomaly.Client.Internal.Persistence;
using MelonLoader.Utils;

namespace Anomaly.Client.API.Persistence;

/// <summary>
/// Entry point for per-mod cross-session persistence on the client. Returns
/// a JSON-file-backed <see cref="IPersistenceStore"/> rooted at
/// <c>&lt;UserData&gt;/Anomaly/&lt;modId&gt;/data.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Stores are cached per mod id — multiple calls to <see cref="For"/> with
/// the same id return the same instance.
/// </para>
/// <para>
/// On the server side, plugins should use LabAPI's config or their own file I/O.
/// This facade exists because the client has no equivalent.
/// </para>
/// </remarks>
public static class ClientPersistence
{
    private static readonly Dictionary<string, IPersistenceStore> _stores =
        new Dictionary<string, IPersistenceStore>(StringComparer.Ordinal);

    /// <summary>
    /// Returns (or creates) the persistence store for <paramref name="modId"/>.
    /// <paramref name="modId"/> must be a syntactically valid dotted-lowercase
    /// namespace; the reserved <c>"anomaly"</c> prefix is accepted for the
    /// framework's own stores.
    /// </summary>
    public static IPersistenceStore For(string modId)
    {
        if (string.IsNullOrEmpty(modId))
            throw new ArgumentException("Mod id must be non-empty.", nameof(modId));

        if (!ModNamespace.IsReserved(modId) && !ModNamespace.IsValid(modId))
        {
            throw new ArgumentException(
                $"ClientPersistence.For('{modId}'): id is not a valid namespace (lowercase dotted, e.g. \"mymod\").",
                nameof(modId));
        }

        if (_stores.TryGetValue(modId, out var existing))
            return existing;

        string path = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly", modId, "data.json");
        var store = new JsonPersistenceStore(modId, path);
        _stores[modId] = store;
        return store;
    }

    /// <summary>
    /// Flushes every open store synchronously. Call on shutdown paths so
    /// debounced writes don't get lost.
    /// </summary>
    public static void FlushAll()
    {
        foreach (var store in _stores.Values)
        {
            try { store.Flush(); }
            catch (Exception) { /* individual failures logged by the store */ }
        }
    }
}
