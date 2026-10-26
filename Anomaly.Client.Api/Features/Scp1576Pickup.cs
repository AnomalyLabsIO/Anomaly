using Scp1576PickupBase = Il2CppInventorySystem.Items.Usables.Scp1576.Scp1576Pickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>SCP-1576's world pickup and replicated horn position.</summary>
[ScriptType]
public class Scp1576Pickup : Pickup
{
    internal Scp1576Pickup(Scp1576PickupBase scp1576Base) : base(scp1576Base)
    {
        Scp1576Base = scp1576Base;
    }

    public Scp1576PickupBase Scp1576Base { get; }
    public float HornPosition => Scp1576Base.HornPos;
}
