using Anomaly.Client.Api.Features;
using Il2CppInventorySystem.Items;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class CurrentItemChangedEventArgs : PlayerEventArgs
{
    // todo: Replace ItemIdentifier type with Item when we have a way to map Serial to Items
    public CurrentItemChangedEventArgs(Player player, ItemIdentifier previousItem, ItemIdentifier currentItem) : base(player)
    {
        PreviousItem = previousItem;
        CurrentItem = currentItem;
    }

    public ItemIdentifier PreviousItem { get; }

    public ItemIdentifier CurrentItem { get; }

    /// <summary>The serial held before the observed change.</summary>
    [ScriptVisible]
    public ushort PreviousSerial => PreviousItem.SerialNumber;
    /// <summary>The serial held after the observed change.</summary>
    [ScriptVisible]
    public ushort CurrentSerial => CurrentItem.SerialNumber;
    /// <summary>The item type held before the observed change.</summary>
    [ScriptVisible]
    public Il2Cpp.ItemType PreviousType => PreviousItem.TypeId;
    /// <summary>The item type held after the observed change.</summary>
    [ScriptVisible]
    public Il2Cpp.ItemType CurrentType => CurrentItem.TypeId;
}
