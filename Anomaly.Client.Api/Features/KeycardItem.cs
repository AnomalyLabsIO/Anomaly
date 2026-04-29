using Il2CppInteractables.Interobjects.DoorUtils;
using KeycardItemBase = Il2CppInventorySystem.Items.Keycards.KeycardItem;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="KeycardItemBase"/>. Exposes the keycard's
/// resolved permissions for door-permission HUDs (which doors can I open?
/// access tier indicators, faction-themed keycard colors).
/// </summary>
/// <remarks>
/// SCP:SL keycards implement <c>IDoorPermissionProvider</c> — actual permission
/// flags are resolved per-request via <see cref="KeycardItemBase.GetPermissions"/>
/// because the same keycard type can grant different permissions depending on
/// the door (chaos-tagged keycards, scientist override, etc.). The wrapper
/// exposes both a "what permissions does this card carry" generic accessor and
/// a per-door <see cref="GetPermissionsFor"/> for the precise resolution.
/// </remarks>
public class KeycardItem : Item
{
    internal KeycardItem(KeycardItemBase keycardBase) : base(keycardBase)
    {
        KeycardBase = keycardBase;
    }

    /// <summary>The underlying <see cref="KeycardItemBase"/>.</summary>
    public KeycardItemBase KeycardBase { get; }

    /// <summary>
    /// The card's intrinsic permissions, resolved against a generic requester.
    /// For per-door precision use <see cref="GetPermissionsFor"/>.
    /// </summary>
    public DoorPermissionFlags Permissions
    {
        get
        {
            try { return KeycardBase != null ? KeycardBase.GetPermissions(null) : DoorPermissionFlags.None; }
            catch { return DoorPermissionFlags.None; }
        }
    }

    /// <summary>
    /// Resolves the keycard's permissions specifically for
    /// <paramref name="requester"/> (typically a door's permission requester
    /// component). Returns <see cref="DoorPermissionFlags.None"/> on failure.
    /// </summary>
    public DoorPermissionFlags GetPermissionsFor(IDoorPermissionRequester requester)
    {
        if (KeycardBase == null) return DoorPermissionFlags.None;
        try { return KeycardBase.GetPermissions(requester); }
        catch { return DoorPermissionFlags.None; }
    }

    public override string ToString() => $"KeycardItem({TypeName}, {Serial}, Perms={Permissions})";
}
