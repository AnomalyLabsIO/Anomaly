using KeycardPickupBase = Il2CppInventorySystem.Items.Keycards.KeycardPickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around a world-spawned <see cref="KeycardPickupBase"/>. Adds a
/// typed downcast plus the keycard-pickup-specific
/// <see cref="OpensDoorsOnCollision"/> flag.
/// </summary>
[ScriptType]
public class KeycardPickup : Pickup
{
    internal KeycardPickup(KeycardPickupBase keycardPickupBase) : base(keycardPickupBase)
    {
        KeycardPickupBase = keycardPickupBase;
    }

    /// <summary>The underlying <see cref="KeycardPickupBase"/>.</summary>
    public KeycardPickupBase KeycardPickupBase { get; }

    /// <summary>
    /// True if this keycard pickup will trigger door interactions when it
    /// physically collides with one (the "thrown card opens door" behaviour).
    /// </summary>
    public bool OpensDoorsOnCollision => KeycardPickupBase != null && KeycardPickupBase._openDoorsOnCollision;

    public override string ToString() => $"KeycardPickup({TypeName}, {Serial})";
}
