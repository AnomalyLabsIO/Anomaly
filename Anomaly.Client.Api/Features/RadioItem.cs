using Il2CppInventorySystem.Items.Radio;
using RadioItemBase = Il2CppInventorySystem.Items.Radio.RadioItem;

namespace Anomaly.Client.Api.Features;

/// <summary>A radio with owning-client battery and range information.</summary>
public class RadioItem : Item
{
    internal RadioItem(RadioItemBase radioBase) : base(radioBase)
    {
        RadioBase = radioBase;
    }

    public RadioItemBase RadioBase { get; }
    public bool HasLocalState => !IsDestroyed && IsLocalPlayerItem && !IsDummyItem;

    /// <summary>Last battery percentage received for the owning client. Null for remote items.</summary>
    public byte? BatteryPercent => HasLocalState ? RadioBase.BatteryPercent : null;

    /// <summary>Last owning-client range, including the disabled value. Null for remote items.</summary>
    public RadioMessages.RadioRangeLevel? RangeLevel => HasLocalState ? RadioBase.RangeLevel : null;

    public bool? IsUsable => HasLocalState ? RadioBase.IsUsable : null;
}
