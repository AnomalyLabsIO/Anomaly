using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Input;
using MelonLoader;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Input;

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
/// by Anomaly and cannot be used by mods.</para>
/// <para>Keep each registration handle and dispose it when your mod shuts down.
/// Disposal removes the binding without firing a release event and preserves
/// the user's saved key override. Call this API on the Unity main thread.</para>
/// </remarks>
public static class InputRegistry
{
    private sealed class Entry : IDisposable
    {
        public InputBindingDefinition Definition;
        public InputKey ResolvedKey;   // default OR user override

        public void Dispose() => Unregister(this);
    }

    private sealed class EmptyRegistration : IDisposable
    {
        internal static readonly EmptyRegistration Instance = new();
        public void Dispose() { }
    }

    private static readonly Dictionary<string, Entry> _bindings =
        new Dictionary<string, Entry>(StringComparer.Ordinal);

    /// <summary>
    /// Fires when a binding transitions from not-pressed to pressed. Dispatched
    /// by the internal <see cref="InputPoller"/>.
    /// </summary>
    [ScriptEvent]
    public static event Action<string> Pressed;

    /// <summary>
    /// Fires when a binding transitions from pressed to not-pressed.
    /// </summary>
    [ScriptEvent]
    public static event Action<string> Released;

    /// <summary>
    /// Registers a binding. The id must be a syntactically valid dotted-lowercase
    /// namespace (e.g. <c>"mymod.jump"</c>); the reserved <c>"anomaly."</c>
    /// prefix is reserved. Duplicate registrations are rejected with a warning
    /// and return an inert handle. Disposing a successful handle removes only
    /// that registration and preserves saved user overrides.
    /// </summary>
    public static IDisposable Register(InputBindingDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        ValidateId(definition.Id);

        if (_bindings.ContainsKey(definition.Id))
        {
            LogWarning($"InputRegistry: binding '{definition.Id}' already registered; ignoring duplicate.");
            return EmptyRegistration.Instance;
        }

        var resolved = InputConfigLoader.TryGetOverride(definition.Id, out var userKey)
            ? userKey
            : definition.DefaultKey;

        var entry = new Entry { Definition = definition, ResolvedKey = resolved };
        _bindings[definition.Id] = entry;

        CheckForConflicts(definition.Id, resolved);
        return entry;
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

    internal static void OnPressed(string id) => InvokeSafely(Pressed, id, "pressed");
    internal static void OnReleased(string id) => InvokeSafely(Released, id, "released");
    internal static void OnPressed(InputFrameBinding binding) =>
        InvokeSafely(Pressed, binding.Id, "pressed", binding.Registration);
    internal static void OnReleased(InputFrameBinding binding) =>
        InvokeSafely(Released, binding.Id, "released", binding.Registration);

    internal static bool IsCurrent(InputFrameBinding binding) =>
        IsCurrent(binding.Id, binding.Registration) &&
        _bindings[binding.Id].ResolvedKey == binding.Key;

    private static bool IsCurrent(string id, object registration) =>
        _bindings.TryGetValue(id, out var entry) && ReferenceEquals(entry, registration);

    private static void Unregister(Entry entry)
    {
        if (!IsCurrent(entry.Definition.Id, entry)) return;
        _bindings.Remove(entry.Definition.Id);
        InputPoller.Forget(entry);
    }

    /// <summary>
    /// Exposes all resolved keys for the poller so it can compute transitions
    /// without re-walking our dictionary via public API.
    /// </summary>
    internal static IReadOnlyList<InputFrameBinding> ResolvedKeysSnapshot()
    {
        var snapshot = new List<InputFrameBinding>(_bindings.Count);
        foreach (var kvp in _bindings)
            snapshot.Add(new InputFrameBinding(kvp.Key, kvp.Value.ResolvedKey, kvp.Value));
        return snapshot;
    }

    private static void InvokeSafely(Action<string> handlers, string id, string transition, object registration = null)
    {
        if (handlers == null)
            return;

        foreach (Action<string> subscriber in handlers.GetInvocationList())
        {
            if (registration != null && !IsCurrent(id, registration)) break;
            try { subscriber(id); }
            catch (Exception ex)
            {
                try
                {
                    MelonLogger.Error(
                        $"InputRegistry: {transition} handler for '{id}' threw and was isolated: {ex}");
                }
                catch
                {
                    // The loader logger is unavailable in metadata/unit-test
                    // hosts; logging must not defeat subscriber isolation.
                }
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Binding id must be non-empty.", nameof(id));

        if (ModNamespace.IsValid(id) && !ModNamespace.IsReserved(id)) return;

        throw new ArgumentException(
            $"Binding id '{id}' must be a non-reserved dotted-lowercase namespace prefixed by your mod id (e.g. \"mymod.jump\").",
            nameof(id));
    }

    private static void CheckForConflicts(string id, InputKey key)
    {
        foreach (var kvp in _bindings)
        {
            if (kvp.Key == id) continue;
            if (kvp.Value.ResolvedKey == key)
            {
                LogWarning(
                    $"InputRegistry: binding '{id}' conflicts with '{kvp.Key}' — both mapped to {key}.");
            }
        }
    }

    private static void LogWarning(string message)
    {
        try { MelonLogger.Warning(message); }
        catch { /* registration ownership must not depend on loader logging */ }
    }
}
