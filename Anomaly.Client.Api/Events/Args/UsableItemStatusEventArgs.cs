using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An observed server use-start/cancel notification. This is not a cancellable input request or a use-completed event.</summary>
[ScriptType]
public class UsableItemStatusEventArgs : EventArgs, IItemEvent, IPlayerEvent
{
    public UsableItemStatusEventArgs(ushort itemSerial, Item item)
    {
        ItemSerial = itemSerial;
        Item = item;
        Player = item?.CurrentOwner;
    }

    [ScriptVisible]
    public ushort ItemSerial { get; }

    /// <summary>The item when it is resolvable locally; may be null after a switch/removal or for hidden inventory.</summary>
    [ScriptVisible]
    public Item Item { get; }
    [ScriptVisible]
    public Player Player { get; }
}
