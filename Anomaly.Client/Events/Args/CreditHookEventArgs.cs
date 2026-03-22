using Anomaly.Client.Components;

namespace Anomaly.Client.Events.Args;

public class CreditHookEventArgs : EventArgs
{
    public CreditHookEventArgs(CreditsHookComponent creditsHookComponent)
    {
        CreditsHookComponent = creditsHookComponent;
    }
    public CreditsHookComponent CreditsHookComponent { get; private set; }
}