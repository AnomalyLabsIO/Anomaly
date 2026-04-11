using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PlayerHealedEventArgs : PlayerEventArgs
{
    public PlayerHealedEventArgs(Player player, float previousHealth, float currentHealth) : base(player)
    {
        PreviousHealth = previousHealth;
        CurrentHealth = currentHealth;
    }

    public float PreviousHealth { get; }

    public float CurrentHealth { get; }

    public float AmountHealed => CurrentHealth - PreviousHealth;
}
