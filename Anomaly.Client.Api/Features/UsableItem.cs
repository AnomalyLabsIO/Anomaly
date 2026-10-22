using UsableItemBase = Il2CppInventorySystem.Items.Usables.UsableItem;

namespace Anomaly.Client.Api.Features;

/// <summary>A usable item, including consumables and wearable SCP items.</summary>
/// <remarks>Runtime use and cooldown fields are maintained for the owning client. Null values below mean unavailable, not idle or ready.</remarks>
public class UsableItem : Item
{
    internal UsableItem(UsableItemBase usableBase) : base(usableBase)
    {
        UsableBase = usableBase;
    }

    public UsableItemBase UsableBase { get; }

    public bool HasLocalState => !IsDestroyed && IsLocalPlayerItem && !IsDummyItem;

    /// <summary>The owning client's use flag; unavailable for remote item representations.</summary>
    public bool? IsUsing => HasLocalState ? UsableBase.IsUsing : null;

    /// <summary>Configured base use duration in seconds. Role/effect modifiers can change the actual duration.</summary>
    public float UseTime => UsableBase.UseTime;

    /// <summary>Configured base cancellation window in seconds.</summary>
    public float MaxCancellableTime => UsableBase.MaxCancellableTime;

    /// <summary>The game's last maintained local countdown. The game updates this while equipped; holstered values may be stale.</summary>
    public float? RemainingCooldown => HasLocalState ? Math.Max(0f, UsableBase.RemainingCooldown) : null;

    /// <summary>The local input gate. This does not guarantee that the server will accept a use request.</summary>
    public bool? CanStartUsing => HasLocalState ? UsableBase.CanStartUsing : null;
}
