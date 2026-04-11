using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class ProjectileSpawnedEventArgs : EventArgs
{
    public ProjectileSpawnedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile Projectile { get; }
}
