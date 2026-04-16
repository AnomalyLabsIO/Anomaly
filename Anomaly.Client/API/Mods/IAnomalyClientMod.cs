using System;

namespace Anomaly.Client.API.Mods;

/// <summary>
/// Identifies a client-side mod built on the Anomaly framework. Registering a mod
/// with <see cref="AnomalyClientMods"/> scopes all subsystem registrations
/// (input bindings, config, commands, persistence, UI prefabs) under a dotted
/// namespace prefix.
/// </summary>
/// <remarks>
/// Implementations are typically either:
/// <list type="bullet">
///   <item>A MelonMod class that also implements this interface and registers itself
///     explicitly via <see cref="AnomalyClientMods.Register"/> during
///     <c>OnInitializeMelon</c>; or</item>
///   <item>A standalone class decorated with <see cref="AnomalyClientModAttribute"/>
///     and a parameterless constructor, auto-discovered during Anomaly bootstrap.</item>
/// </list>
/// </remarks>
public interface IAnomalyClientMod
{
    /// <summary>
    /// Lowercase dotted namespace (e.g. "acme.wargames"). Used as the prefix for
    /// every other registration this mod makes. Must be stable across versions.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name shown in diagnostics and UI.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Mod version. Informational only — no compatibility checks are made.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Called once after the mod is registered and the Anomaly core is ready.
    /// Use this to wire up event subscriptions and register subsystem resources.
    /// </summary>
    void OnEnable();

    /// <summary>
    /// Called once when the game is shutting down. Use this to release resources.
    /// Anomaly does not currently support hot-unloading.
    /// </summary>
    void OnDisable();
}
