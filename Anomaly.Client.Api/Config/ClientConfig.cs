using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Config;

namespace Anomaly.Client.Api.Config;

/// <summary>
/// Entry point for per-mod configuration on the client. Returns a
/// MelonPreferences-backed <see cref="IConfigSource"/>.
/// </summary>
/// <remarks>
/// <para>Instances are cached per mod id so multiple calls to
/// <see cref="For"/> with the same id return the same store (and therefore
/// share <see cref="IConfigSource.KeyChanged"/> subscribers).</para>
/// <para>New mods should prefer direct MelonPreferences categories for their
/// user-facing settings. This facade remains for Anomaly.Client.Api consumers
/// that want a small typed key/value compatibility layer.</para>
/// </remarks>
public static class ClientConfig
{
    private static readonly Dictionary<string, IConfigSource> _sources =
        new Dictionary<string, IConfigSource>(StringComparer.Ordinal);

    /// <summary>
    /// Returns (or creates) the config store for <paramref name="modId"/>.
    /// <paramref name="modId"/> must be a syntactically valid dotted-lowercase
    /// namespace outside the reserved <c>"anomaly"</c> prefix.
    /// </summary>
    public static IConfigSource For(string modId)
    {
        if (string.IsNullOrEmpty(modId))
            throw new ArgumentException("Mod id must be non-empty.", nameof(modId));

        if (!ModNamespace.IsValid(modId) || ModNamespace.IsReserved(modId))
        {
            throw new ArgumentException(
                $"ClientConfig.For('{modId}'): id must be a non-reserved namespace (lowercase dotted, e.g. \"mymod\").",
                nameof(modId));
        }

        if (_sources.TryGetValue(modId, out var existing))
            return existing;

        var source = new MelonPreferencesConfigSource(modId);
        _sources[modId] = source;
        return source;
    }

    /// <summary>
    /// Clears cached config source instances. MelonPreferences owns file IO and
    /// watchers; this only resets Anomaly's compatibility facade cache.
    /// </summary>
    internal static void DisposeAll()
    {
        _sources.Clear();
    }
}
