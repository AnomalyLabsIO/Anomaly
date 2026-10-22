using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items.Usables.Scp330;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="Scp330Bag"/> — the SCP-330 candy bag item.
/// Exposes the carried candy list and selected-candy index for HUD overlays
/// (candy picker, current-candy indicator, "what did I get" toasts).
/// </summary>
/// <remarks>
/// Named <see cref="Scp330Item"/> for naming consistency with LabAPI's
/// server-side wrapper. The underlying IL2CPP type is <see cref="Scp330Bag"/>.
/// Maximum candies per bag is <see cref="Scp330Bag.MaxCandies"/> (6).
/// </remarks>
public class Scp330Item : UsableItem
{
    internal Scp330Item(Scp330Bag bagBase) : base(bagBase)
    {
        BagBase = bagBase;
    }

    /// <summary>The underlying <see cref="Scp330Bag"/>.</summary>
    public Scp330Bag BagBase { get; }

    /// <summary>Maximum candies a single bag can hold (6).</summary>
    public static int MaxCandies => Scp330Bag.MaxCandies;

    /// <summary>The candies currently in the bag, in order. Empty if the bag is empty.</summary>
    public ReadOnlyCollection<CandyKindID> Candies
    {
        get
        {
            List<CandyKindID> result = new();
            if (BagBase?.Candies == null)
                return result.AsReadOnly();
            foreach (CandyKindID candy in BagBase.Candies)
                result.Add(candy);
            return result.AsReadOnly();
        }
    }

    /// <summary>Number of candies currently in the bag.</summary>
    public int CandyCount => BagBase?.Candies?.Count ?? 0;

    /// <summary>True if the bag is full (<see cref="MaxCandies"/> candies).</summary>
    public bool IsFull => CandyCount >= MaxCandies;

    /// <summary>True if a specific candy is currently selected (about to be eaten).</summary>
    public bool IsCandySelected => BagBase != null && BagBase.IsCandySelected;

    /// <summary>The index of the selected candy, or -1 if none is selected.</summary>
    public int SelectedCandyId => BagBase != null ? BagBase.SelectedCandyId : -1;

    /// <summary>The selected candy's kind, or null if none is selected.</summary>
    public CandyKindID? SelectedCandy
    {
        get
        {
            int idx = SelectedCandyId;
            if (idx < 0 || BagBase?.Candies == null || idx >= BagBase.Candies.Count) return null;
            return BagBase.Candies[idx];
        }
    }

    public override string ToString() => $"Scp330Item({CandyCount}/{MaxCandies}, Selected={SelectedCandy?.ToString() ?? "none"})";
}
