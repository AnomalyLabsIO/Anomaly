using RadioPickupBase = Il2CppInventorySystem.Items.Radio.RadioPickup;

namespace Anomaly.Client.Api.Features;

/// <summary>A dropped radio's replicated enabled and range state. Pickup battery is server-only and is not exposed here.</summary>
public class RadioPickup : Pickup
{
    internal RadioPickup(RadioPickupBase radioBase) : base(radioBase)
    {
        RadioBase = radioBase;
    }

    public RadioPickupBase RadioBase { get; }
    public bool IsEnabled => RadioBase.NetworkSavedEnabled;
    public byte RangeId => RadioBase.NetworkSavedRange;
}
