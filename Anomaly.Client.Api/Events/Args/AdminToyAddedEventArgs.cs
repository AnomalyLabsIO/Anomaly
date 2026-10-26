using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class AdminToyAddedEventArgs : EventArgs
{
    public AdminToyAddedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    [ScriptVisible]
    public AdminToy AdminToy { get; }
}
