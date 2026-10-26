using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items.Usables.Scp330;
using Scp330PickupBase = Il2CppInventorySystem.Items.Usables.Scp330.Scp330Pickup;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around a world-spawned <see cref="Scp330PickupBase"/> — the SCP-330
/// candy bag dropped on the ground. Exposes the bag's contents and the
/// currently exposed (visually displayed) candy.
/// </summary>
[ScriptType]
public class Scp330Pickup : Pickup
{
    internal Scp330Pickup(Scp330PickupBase scp330PickupBase) : base(scp330PickupBase)
    {
        Scp330PickupBase = scp330PickupBase;
    }

    /// <summary>The underlying <see cref="Scp330PickupBase"/>.</summary>
    public Scp330PickupBase Scp330PickupBase { get; }

    /// <summary>The candy currently visible on the dropped bag (the one a player would grab next).</summary>
    public CandyKindID ExposedCandy => Scp330PickupBase != null ? Scp330PickupBase.ExposedCandy : CandyKindID.None;

    /// <summary>Every candy currently stored in the dropped bag.</summary>
    public ReadOnlyCollection<CandyKindID> StoredCandies
    {
        get
        {
            List<CandyKindID> result = new();
            if (Scp330PickupBase?.StoredCandies == null)
                return result.AsReadOnly();
            foreach (CandyKindID candy in Scp330PickupBase.StoredCandies)
                result.Add(candy);
            return result.AsReadOnly();
        }
    }

    public int CandyCount => Scp330PickupBase?.StoredCandies?.Count ?? 0;

    public override string ToString() => $"Scp330Pickup(Exposed={ExposedCandy}, Count={CandyCount})";
}
