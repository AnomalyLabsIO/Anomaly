using Il2Cpp;
using Il2CppInventorySystem.Items.Firearms.ShotEvents;

namespace Anomaly.Client.Api.Events.Args;

public class FirearmShotEventArgs : EventArgs
{
    public FirearmShotEventArgs(ShotEvent shot)
    {
        Shot = shot;
    }

    public ShotEvent Shot { get; }

    public ItemType Firearm => Shot.ItemId.TypeId;
}
