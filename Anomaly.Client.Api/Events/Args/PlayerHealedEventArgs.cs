using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class PlayerHealedEventArgs : PlayerEventArgs
{
    public PlayerHealedEventArgs(Player player, float previousHealth, float currentHealth) : base(player)
    {
        PreviousHealth = previousHealth;
        CurrentHealth = currentHealth;
    }

    [ScriptVisible]
    public float PreviousHealth { get; }

    [ScriptVisible]
    public float CurrentHealth { get; }

    [ScriptVisible]
    public float AmountHealed => CurrentHealth - PreviousHealth;
}
