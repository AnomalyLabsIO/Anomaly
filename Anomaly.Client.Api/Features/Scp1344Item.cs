using Il2CppInventorySystem.Items.Usables.Scp1344;
using Scp1344ItemBase = Il2CppInventorySystem.Items.Usables.Scp1344.Scp1344Item;

namespace Anomaly.Client.Api.Features;

/// <summary>SCP-1344's observed state and local use controls.</summary>
public class Scp1344Item : UsableItem
{
    internal Scp1344Item(Scp1344ItemBase scp1344Base) : base(scp1344Base)
    {
        Scp1344Base = scp1344Base;
    }

    public Scp1344ItemBase Scp1344Base { get; }

    /// <summary>Last client-observed or locally predicted state.</summary>
    public Scp1344Status Status => Scp1344Base.Status;
    public bool? IsWorn => HasLocalState ? Scp1344Base.IsWorn : null;
    public bool? CanInspect => HasLocalState ? Scp1344Base.AllowInspect : null;
}
