using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class AdminToyRemovedEventArgs : EventArgs
{
    public AdminToyRemovedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    public AdminToy AdminToy { get; }
}
