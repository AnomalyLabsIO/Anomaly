using Anomaly.Client.Api.Components;

namespace Anomaly.Client.Api.Events.Args;

public class CreditHookEventArgs : EventArgs
{
    public CreditHookEventArgs(CreditsHookComponent creditsHookComponent)
    {
        CreditsHookComponent = creditsHookComponent;
    }
    public CreditsHookComponent CreditsHookComponent { get; private set; }
}