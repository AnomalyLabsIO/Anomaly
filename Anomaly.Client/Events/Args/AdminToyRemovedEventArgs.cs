using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class AdminToyRemovedEventArgs : EventArgs
{
    public AdminToyRemovedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    public AdminToy AdminToy { get; }
}
