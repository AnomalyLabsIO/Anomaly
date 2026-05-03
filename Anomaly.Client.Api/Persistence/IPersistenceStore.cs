namespace Anomaly.Client.Api.Persistence;

/// <summary>
/// A typed, key/value persistence store for cross-session state backed by
/// MelonPreferences. Each mod gets its own store via
/// <see cref="ClientPersistence.For"/>.
/// </summary>
/// <remarks>
/// <para>
/// Persistence is distinct from <see cref="Anomaly.Client.Api.Config.IConfigSource"/>:
/// config is user-editable tunables with hot-reload semantics, while persistence
/// is implementation-owned state the user generally shouldn't hand-edit.
/// </para>
/// <para>
/// Writes are debounced — rapid successive <see cref="Save"/> calls coalesce
/// into one MelonPreferences write — so callers can freely save on every change.
/// </para>
/// </remarks>
public interface IPersistenceStore
{
    /// <summary>The mod id this store belongs to.</summary>
    string ModId { get; }

    /// <summary>Loads the value at <paramref name="key"/> or returns <paramref name="defaultValue"/>.</summary>
    T Load<T>(string key, T defaultValue = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> and schedules a debounced write.</summary>
    void Save<T>(string key, T value);

    /// <summary>Returns true if a value is stored under <paramref name="key"/>.</summary>
    bool Exists(string key);

    /// <summary>Removes the stored value (if any) and schedules a debounced write.</summary>
    void Delete(string key);

    /// <summary>Forces an immediate synchronous write, bypassing debouncing.</summary>
    void Flush();
}
