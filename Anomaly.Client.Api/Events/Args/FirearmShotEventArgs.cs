using Il2Cpp;
using Il2CppInventorySystem.Items.Firearms.ShotEvents;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class FirearmShotEventArgs : EventArgs
{
    public FirearmShotEventArgs(ShotEvent shot)
    {
        Shot = shot;
    }

    public ShotEvent Shot { get; }

    [ScriptVisible]
    public ItemType Firearm => Shot.ItemId.TypeId;
}
