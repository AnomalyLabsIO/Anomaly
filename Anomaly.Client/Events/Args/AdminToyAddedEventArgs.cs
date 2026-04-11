using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class AdminToyAddedEventArgs : EventArgs
{
    public AdminToyAddedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    public AdminToy AdminToy { get; }
}
