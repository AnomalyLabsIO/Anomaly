using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class InventoryChangedEventArgs : PlayerEventArgs
{
    public InventoryChangedEventArgs(Player player) : base(player)
    {
    }
}
