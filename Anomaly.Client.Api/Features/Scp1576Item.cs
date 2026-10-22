using Scp1576ItemBase = Il2CppInventorySystem.Items.Usables.Scp1576.Scp1576Item;

namespace Anomaly.Client.Api.Features;

/// <summary>SCP-1576 with the common usable-item facade and transmission configuration.</summary>
public class Scp1576Item : UsableItem
{
    internal Scp1576Item(Scp1576ItemBase scp1576Base) : base(scp1576Base)
    {
        Scp1576Base = scp1576Base;
    }

    public Scp1576ItemBase Scp1576Base { get; }
    public static float TransmissionDuration => Scp1576ItemBase.TransmissionDuration;
    public static float CooldownDuration => Scp1576ItemBase.UseCooldown;
}
