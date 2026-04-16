using System;

namespace Anomaly.Client.API.Input;

/// <summary>
/// Describes a single input binding a plugin registers with
/// <see cref="InputRegistry"/>. Once registered, the binding can be queried by id
/// every frame; its <see cref="DefaultKey"/> applies until the user rebinds via
/// <see cref="InputRegistry.Rebind"/> or an entry in <c>bindings.json</c>.
/// </summary>
public sealed class InputBindingDefinition
{
    /// <summary>
    /// Dotted, lowercase id prefixed with the registering plugin's id, e.g.
    /// "acme.wargames.fire_flare".
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Localization key used to render the binding's name in UI (rebind menu,
    /// tooltips, etc.). Resolved via the existing <c>Tr</c> localization facade.
    /// </summary>
    public string DisplayNameKey { get; }

    /// <summary>
    /// Optional localization key for a longer description. Empty if not provided.
    /// </summary>
    public string DescriptionKey { get; }

    /// <summary>
    /// Default key and modifiers. Applies until the user rebinds.
    /// </summary>
    public InputKey DefaultKey { get; }

    public InputBindingDefinition(string id, string displayNameKey, InputKey defaultKey, string descriptionKey = null)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("Id must be non-empty.", nameof(id));
        if (string.IsNullOrEmpty(displayNameKey)) throw new ArgumentException("DisplayNameKey must be non-empty.", nameof(displayNameKey));

        Id = id;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey ?? string.Empty;
        DefaultKey = defaultKey;
    }
}
