using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class ProjectileSpawnedEventArgs : EventArgs
{
    public ProjectileSpawnedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile Projectile { get; }
}
