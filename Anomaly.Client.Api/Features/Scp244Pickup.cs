using Il2CppInventorySystem.Items.Usables.Scp244;

namespace Anomaly.Client.Api.Features;

/// <summary>A deployed SCP-244 pickup and its replicated fog state.</summary>
public class Scp244Pickup : Pickup
{
    internal Scp244Pickup(Scp244DeployablePickup scp244Base) : base(scp244Base)
    {
        Scp244Base = scp244Base;
    }

    public Scp244DeployablePickup Scp244Base { get; }
    public Scp244State State => Scp244Base.State;
    public float CurrentDiameter => Scp244Base.CurrentDiameter;
    public float MaximumDiameter => Scp244Base.MaxDiameter;
    public float CurrentSizePercent => Scp244Base.CurrentSizePercent;
}
