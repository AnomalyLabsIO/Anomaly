using System;

namespace Anomaly.Client.API.Config;

/// <summary>
/// A typed, key/value config store backed by persistent storage. Each Anomaly
/// mod gets its own <see cref="IConfigSource"/> via
/// <see cref="ClientConfig.For"/>; the store is scoped to that mod's id and
/// isolated from other mods.
/// </summary>
/// <remarks>
/// <para>Values are serialized with Newtonsoft.Json so any type Json.NET can round-trip
/// is supported (primitives, POCOs, collections, enums).</para>
/// <para>The store supports file-watcher-based hot reload: when the backing file
/// changes on disk, <see cref="Reload"/> is called and <see cref="KeyChanged"/>
/// fires for each key whose value differs from the in-memory copy.</para>
/// </remarks>
public interface IConfigSource
{
    /// <summary>The mod id this store belongs to.</summary>
    string ModId { get; }

    /// <summary>
    /// Returns the value stored under <paramref name="key"/>, or
    /// <paramref name="defaultValue"/> if the key is absent or deserialization fails.
    /// </summary>
    T Get<T>(string key, T defaultValue = default);

    /// <summary>Stores a value under <paramref name="key"/> (in-memory only until <see cref="Save"/>).</summary>
    void Set<T>(string key, T value);

    /// <summary>Returns true if <paramref name="key"/> has a stored value.</summary>
    bool Has(string key);

    /// <summary>Removes the value at <paramref name="key"/>, if any.</summary>
    void Remove(string key);

    /// <summary>Persists the in-memory state to the backing file.</summary>
    void Save();

    /// <summary>
    /// Re-reads the backing file and merges changes into the in-memory state,
    /// firing <see cref="KeyChanged"/> for each key whose value differs.
    /// </summary>
    void Reload();

    /// <summary>
    /// Fires with the key that changed as a result of either <see cref="Set"/>,
    /// <see cref="Remove"/>, or <see cref="Reload"/>. Not fired during initial
    /// load.
    /// </summary>
    event Action<string> KeyChanged;
}
