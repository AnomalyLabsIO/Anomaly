using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Persistence;

namespace Anomaly.Client.Api.Persistence;

/// <summary>
/// Entry point for per-mod cross-session persistence on the client. Returns a
/// MelonPreferences-backed <see cref="IPersistenceStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stores are cached per mod id — multiple calls to <see cref="For"/> with
/// the same id return the same instance.
/// </para>
/// <para>
/// New mods should prefer direct MelonPreferences categories for local state.
/// This facade remains for existing Anomaly.Client.Api consumers that want a
/// small typed key/value compatibility layer.
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

        var store = new MelonPreferencesPersistenceStore(modId);
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
