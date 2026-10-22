using Il2CppInventorySystem.Items.MicroHID;
using Il2CppInventorySystem.Items.MicroHID.Modules;
using MicroHIDItemBase = Il2CppInventorySystem.Items.MicroHID.MicroHIDItem;

namespace Anomaly.Client.Api.Features;

/// <summary>The Micro H.I.D.'s client-observed energy and firing cycle.</summary>
public class MicroHIDItem : Item
{
    internal MicroHIDItem(MicroHIDItemBase microHidBase) : base(microHidBase)
    {
        MicroHIDBase = microHidBase;
    }

    public MicroHIDItemBase MicroHIDBase { get; }

    /// <summary>Last replicated normalized energy, or null until a value for this serial has arrived.</summary>
    public float? Energy => EnergyManagerModule.SyncEnergy.TryGetValue(Serial, out float energy) ? energy : null;

    /// <summary>Current client-observed cycle, or null before the cycle controller exists.</summary>
    public MicroHidPhase? Phase => MicroHIDBase.CycleController?.Phase;
}
