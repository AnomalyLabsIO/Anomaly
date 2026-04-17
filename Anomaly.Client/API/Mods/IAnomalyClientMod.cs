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
    /// <remarks>
    /// Hot-unload is not supported. Anything allocated here lives for the rest of
    /// the game session — you cannot rely on <see cref="OnDisable"/> being called
    /// to free it mid-session.
    /// </remarks>
    void OnEnable();

    /// <summary>
    /// Called once during application shutdown (<c>OnApplicationQuit</c>) in reverse
    /// registration order. Use it to flush buffers, close files, and release OS
    /// handles — the game is about to exit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Hot-unload is not supported.</strong> <see cref="OnDisable"/> is invoked
    /// only on game shutdown. There is no runtime path that disables a mod while the
    /// game is still running, so event subscriptions, <c>MonoBehaviour</c> instances,
    /// Harmony patches, Unity objects, and cached references registered during
    /// <see cref="OnEnable"/> remain live for the session.
    /// </para>
    /// <para>
    /// Do not design mods that expect to be toggled on/off mid-session. If you need
    /// a disabled state, expose a feature flag your own code reads; do not rely on
    /// the framework tearing your mod down.
    /// </para>
    /// </remarks>
    void OnDisable();
}
