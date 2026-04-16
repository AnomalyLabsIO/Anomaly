using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Client.API.Mods;
using Anomaly.Client.Internal.Config;
using MelonLoader.Utils;

namespace Anomaly.Client.API.Config;

/// <summary>
/// Entry point for per-mod configuration on the client. Returns a JSON-file
/// backed <see cref="IConfigSource"/> rooted at
/// <c>&lt;UserData&gt;/Anomaly/&lt;modId&gt;/config.json</c>.
/// </summary>
/// <remarks>
/// <para>Instances are cached per mod id so multiple calls to
/// <see cref="For"/> with the same id return the same store (and therefore
/// share <see cref="IConfigSource.KeyChanged"/> subscribers).</para>
/// <para>On the server side, plugins should use LabAPI's <c>Plugin&lt;TConfig&gt;</c>
/// directly — this facade exists only because the client has no equivalent.</para>
/// </remarks>
public static class ClientConfig
{
    private static readonly Dictionary<string, IConfigSource> _sources =
        new Dictionary<string, IConfigSource>(StringComparer.Ordinal);

    /// <summary>
    /// Returns (or creates) the config store for <paramref name="modId"/>.
    /// The id must belong to a registered mod or be the reserved "anomaly"
    /// core prefix.
    /// </summary>
    public static IConfigSource For(string modId)
    {
        if (string.IsNullOrEmpty(modId))
            throw new ArgumentException("Mod id must be non-empty.", nameof(modId));

        if (modId != "anomaly" &&
            !modId.StartsWith("anomaly.", StringComparison.Ordinal) &&
            !AnomalyClientMods.IsKeyOwnedByRegisteredMod(modId))
        {
            throw new ArgumentException(
                $"ClientConfig.For('{modId}'): mod id is not registered (see AnomalyClientMods.Register).",
                nameof(modId));
        }

        if (_sources.TryGetValue(modId, out var existing))
            return existing;

        string path = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly", modId, "config.json");
        var source = new JsonConfigSource(modId, path);
        _sources[modId] = source;
        return source;
    }

    /// <summary>
    /// Disposes every cached config source, releasing the underlying
    /// <see cref="System.IO.FileSystemWatcher"/>s and scheduled reload tasks.
    /// Call on shutdown paths (e.g. <c>OnApplicationQuit</c>) to leave a clean
    /// process exit. Mirrors <see cref="Persistence.ClientPersistence.FlushAll"/>.
    /// </summary>
    public static void DisposeAll()
    {
        foreach (var source in _sources.Values)
        {
            if (source is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception) { /* per-source failures are non-fatal on shutdown */ }
            }
        }
        _sources.Clear();
    }
}
