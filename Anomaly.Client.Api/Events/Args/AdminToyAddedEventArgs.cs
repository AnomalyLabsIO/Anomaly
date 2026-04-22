using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class AdminToyAddedEventArgs : EventArgs
{
    public AdminToyAddedEventArgs(AdminToy adminToy)
    {
        AdminToy = adminToy;
    }

    public AdminToy AdminToy { get; }
}
