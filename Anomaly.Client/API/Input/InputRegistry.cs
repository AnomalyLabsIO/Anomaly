using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Anomaly.Client.Internal;
using Anomaly.Client.Internal.Input;
using MelonLoader;

namespace Anomaly.Client.API.Input;

/// <summary>
/// Static registry for named input bindings. Mods register their bindings
/// (typically in their <c>MelonMod.OnInitializeMelon</c>), and poll state each
/// frame via <see cref="IsPressed"/>, <see cref="WasPressedThisFrame"/>, or
/// <see cref="WasReleasedThisFrame"/>.
/// </summary>
/// <remarks>
/// <para>User overrides from <c>bindings.json</c> take precedence over the
/// default key declared at registration time. Conflict detection warns when two
/// distinct bindings resolve to the same effective key but does not prevent the
/// collision — final say is the user's.</para>
/// <para>Binding ids must be syntactically valid dotted-lowercase namespaces
/// (e.g. <c>"mymod.jump"</c>). The reserved <c>"anomaly."</c> prefix is used
/// by Anomaly's own built-in bindings.</para>
/// </remarks>
public static class InputRegistry
{
    private sealed class Entry
    {
        public InputBindingDefinition Definition;
        public InputKey ResolvedKey;   // default OR user override
    }

    private static readonly Dictionary<string, Entry> _bindings =
        new Dictionary<string, Entry>(StringComparer.Ordinal);

    /// <summary>
    /// Fires when a binding transitions from not-pressed to pressed. Dispatched
    /// by the internal <see cref="InputPoller"/>.
    /// </summary>
    public static event Action<string> Pressed;

    /// <summary>
    /// Fires when a binding transitions from pressed to not-pressed.
    /// </summary>
    public static event Action<string> Released;

    /// <summary>
    /// Registers a binding. The id must be a syntactically valid dotted-lowercase
    /// namespace (e.g. <c>"mymod.jump"</c>); the reserved <c>"anomaly."</c>
    /// prefix is accepted for framework built-ins. Duplicate registrations are
    /// rejected with a warning.
    /// </summary>
    public static void Register(InputBindingDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        ValidateId(definition.Id);

        if (_bindings.ContainsKey(definition.Id))
        {
            MelonLogger.Warning($"InputRegistry: binding '{definition.Id}' already registered; ignoring duplicate.");
            return;
        }

        var resolved = InputConfigLoader.TryGetOverride(definition.Id, out var userKey)
            ? userKey
            : definition.DefaultKey;

        _bindings[definition.Id] = new Entry { Definition = definition, ResolvedKey = resolved };

        CheckForConflicts(definition.Id, resolved);
    }

    /// <summary>All registered binding definitions.</summary>
    public static IReadOnlyCollection<InputBindingDefinition> All =>
        new ReadOnlyCollection<InputBindingDefinition>(_bindings.Values.Select(e => e.Definition).ToList());

    /// <summary>Returns the binding definition or null if unknown.</summary>
    public static InputBindingDefinition GetBinding(string id) =>
        id != null && _bindings.TryGetValue(id, out var e) ? e.Definition : null;

    /// <summary>
    /// Returns the current effective key (default or user-overridden). Returns
    /// <c>default(InputKey)</c> if the id is unknown.
    /// </summary>
    public static InputKey GetResolvedKey(string id) =>
        id != null && _bindings.TryGetValue(id, out var e) ? e.ResolvedKey : default;

    /// <summary>True iff the bound primary key plus modifiers are currently held.</summary>
    public static bool IsPressed(string id) =>
        id != null && _bindings.TryGetValue(id, out var e) && InputPoller.IsHeld(e.ResolvedKey);

    /// <summary>True for a single frame after the binding transitions to pressed.</summary>
    public static bool WasPressedThisFrame(string id) =>
        id != null && _bindings.TryGetValue(id, out var e) && InputPoller.WasPressedThisFrame(e.ResolvedKey);

    /// <summary>True for a single frame after the binding transitions to released.</summary>
    public static bool WasReleasedThisFrame(string id) =>
        id != null && _bindings.TryGetValue(id, out var e) && InputPoller.WasReleasedThisFrame(e.ResolvedKey);

    /// <summary>
    /// Rebinds <paramref name="id"/> to <paramref name="newKey"/> and persists the
    /// override to <c>bindings.json</c>. Logs a warning for unknown ids.
    /// </summary>
    public static void Rebind(string id, InputKey newKey)
    {
        if (!_bindings.TryGetValue(id, out var entry))
        {
            MelonLogger.Warning($"InputRegistry: cannot rebind unknown binding '{id}'.");
            return;
        }

        entry.ResolvedKey = newKey;
        InputConfigLoader.SetOverride(id, newKey);
        CheckForConflicts(id, newKey);
    }

    /// <summary>
    /// Removes a user override, reverting to the binding's registered default.
    /// </summary>
    public static void ResetToDefault(string id)
    {
        if (!_bindings.TryGetValue(id, out var entry))
        {
            MelonLogger.Warning($"InputRegistry: cannot reset unknown binding '{id}'.");
            return;
        }
        entry.ResolvedKey = entry.Definition.DefaultKey;
        InputConfigLoader.RemoveOverride(id);
    }

    internal static void OnPressed(string id) => Pressed?.Invoke(id);
    internal static void OnReleased(string id) => Released?.Invoke(id);

    /// <summary>
    /// Exposes all resolved keys for the poller so it can compute transitions
    /// without re-walking our dictionary via public API.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, InputKey>> ResolvedKeys()
    {
        foreach (var kvp in _bindings)
            yield return new KeyValuePair<string, InputKey>(kvp.Key, kvp.Value.ResolvedKey);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Binding id must be non-empty.", nameof(id));

        if (ModNamespace.IsReserved(id)) return;
        if (ModNamespace.IsValid(id)) return;

        throw new ArgumentException(
            $"Binding id '{id}' must be a dotted-lowercase namespace prefixed by your mod id (e.g. \"mymod.jump\").",
            nameof(id));
    }

    private static void CheckForConflicts(string id, InputKey key)
    {
        foreach (var kvp in _bindings)
        {
            if (kvp.Key == id) continue;
            if (kvp.Value.ResolvedKey == key)
            {
                MelonLogger.Warning(
                    $"InputRegistry: binding '{id}' conflicts with '{kvp.Key}' — both mapped to {key}.");
            }
        }
    }
}
