using ConsumableBase = Il2CppInventorySystem.Items.Usables.Consumable;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A consumable with an owning-client use progress bar.</summary>
[ScriptType]
public class ConsumableItem : UsableItem
{
    internal ConsumableItem(ConsumableBase consumableBase) : base(consumableBase)
    {
        ConsumableBase = consumableBase;
    }

    public ConsumableBase ConsumableBase { get; }

    /// <summary>Current local progress-bar value, or null for remote representations.</summary>
    public float? UseProgress => HasLocalState ? ConsumableBase.ProgressbarValue : null;

    public float UseProgressMinimum => ConsumableBase.ProgressbarMin;
    public float UseProgressMaximum => ConsumableBase.ProgressbarMax;
}
