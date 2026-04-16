using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;

namespace Anomaly.Client.API.Mods;

/// <summary>
/// Static registry of client-side Anomaly mods. Other Anomaly subsystems
/// (input, config, commands, persistence, UI) consult this registry to enforce
/// mod-id namespace prefixes on their registrations.
/// </summary>
/// <remarks>
/// <para>Mods may register in two ways:</para>
/// <list type="number">
///   <item>Explicitly, by calling <see cref="Register"/> from their MelonMod's
///     <c>OnInitializeMelon</c>.</item>
///   <item>Implicitly, by decorating an <see cref="IAnomalyClientMod"/>
///     implementation with <see cref="AnomalyClientModAttribute"/>. The Anomaly
///     bootstrap calls <see cref="Discover"/> to pick these up.</item>
/// </list>
/// <para>Both paths end up calling <see cref="Register"/> internally, so
/// <see cref="IAnomalyClientMod.OnEnable"/> fires exactly once per mod.</para>
/// </remarks>
public static class AnomalyClientMods
{
    // Lowercase dotted segments, each alphanumeric or underscore, 1-40 segments,
    // each segment 1-32 chars. Prevents collisions and weird wire keys.
    private static readonly Regex IdPattern = new Regex(
        @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, IAnomalyClientMod> _mods =
        new Dictionary<string, IAnomalyClientMod>(StringComparer.Ordinal);

    private static bool _discovered;

    /// <summary>
    /// All currently registered mods, in registration order.
    /// </summary>
    public static IReadOnlyCollection<IAnomalyClientMod> All =>
        new ReadOnlyCollection<IAnomalyClientMod>(_mods.Values.ToList());

    /// <summary>
    /// Registers a mod. The mod's <see cref="IAnomalyClientMod.OnEnable"/>
    /// fires immediately. Re-registration with the same id is a no-op with a warning.
    /// Throws if <see cref="IAnomalyClientMod.Id"/> is invalid.
    /// </summary>
    public static void Register(IAnomalyClientMod mod)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        string id = mod.Id;
        ValidateId(id);

        if (_mods.ContainsKey(id))
        {
            MelonLogger.Warning($"AnomalyClientMods: '{id}' already registered; ignoring duplicate registration.");
            return;
        }

        _mods[id] = mod;
        MelonLogger.Msg($"AnomalyClientMods: Registered '{id}' ({mod.DisplayName} v{mod.Version}).");

        try
        {
            mod.OnEnable();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AnomalyClientMods: OnEnable for '{id}' threw: {ex}");
        }
    }

    /// <summary>
    /// Returns the mod with the given id, or null if not registered.
    /// </summary>
    public static IAnomalyClientMod GetById(string id) =>
        id != null && _mods.TryGetValue(id, out var mod) ? mod : null;

    /// <summary>
    /// Returns true if a mod with the given id is registered.
    /// </summary>
    public static bool IsRegistered(string id) =>
        id != null && _mods.ContainsKey(id);

    /// <summary>
    /// Validates that <paramref name="key"/> exactly matches a registered
    /// mod id, or is a key prefixed by one (e.g. "acme.wargames.feature.foo"
    /// is valid if mod "acme.wargames" is registered).
    /// </summary>
    /// <remarks>
    /// Used by other subsystem registries (InputRegistry, CommandRegistry, etc.)
    /// to enforce the namespace convention.
    /// </remarks>
    public static bool IsKeyOwnedByRegisteredMod(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (var id in _mods.Keys)
        {
            if (key == id) return true;
            if (key.Length > id.Length && key[id.Length] == '.' && key.StartsWith(id, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Scans all loaded assemblies for types marked with
    /// <see cref="AnomalyClientModAttribute"/>, instantiates each, and registers
    /// the result. Idempotent. Called once during Anomaly bootstrap.
    /// </summary>
    internal static void Discover()
    {
        if (_discovered) return;
        _discovered = true;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Tolerate mods with missing dependencies; use whatever types loaded.
                types = ex.Types.Where(t => t != null).ToArray();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"AnomalyClientMods: failed to enumerate types in {assembly.GetName().Name}: {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (type.GetCustomAttribute<AnomalyClientModAttribute>() == null) continue;

                if (!typeof(IAnomalyClientMod).IsAssignableFrom(type))
                {
                    MelonLogger.Warning(
                        $"AnomalyClientMods: '{type.FullName}' has [AnomalyClientMod] but does not implement IAnomalyClientMod; skipping.");
                    continue;
                }

                IAnomalyClientMod instance;
                try
                {
                    instance = (IAnomalyClientMod)Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error(
                        $"AnomalyClientMods: failed to instantiate '{type.FullName}': {ex.Message}");
                    continue;
                }

                Register(instance);
            }
        }
    }

    /// <summary>
    /// Calls <see cref="IAnomalyClientMod.OnDisable"/> on all registered mods
    /// in reverse registration order. Invoked during application shutdown.
    /// </summary>
    internal static void DisableAll()
    {
        foreach (var mod in _mods.Values.Reverse().ToList())
        {
            try
            {
                mod.OnDisable();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AnomalyClientMods: OnDisable for '{mod.Id}' threw: {ex}");
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Mod id must be non-empty.", nameof(id));
        if (!IdPattern.IsMatch(id))
            throw new ArgumentException(
                $"Mod id '{id}' is invalid. Must be lowercase dotted (e.g. \"acme.wargames\"), " +
                "each segment alphanumeric or underscore, starting with a letter.",
                nameof(id));
    }
}
