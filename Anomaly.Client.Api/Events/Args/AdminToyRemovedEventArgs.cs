using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class AdminToyRemovedEventArgs : EventArgs
{
    public AdminToyRemovedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    [ScriptVisible]
    public AdminToy AdminToy { get; }
}
