using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class ProjectileRemovedEventArgs : EventArgs
{
    public ProjectileRemovedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile Projectile { get; }
}
