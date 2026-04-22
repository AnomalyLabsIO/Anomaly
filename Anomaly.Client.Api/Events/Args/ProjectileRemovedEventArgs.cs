using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class ProjectileRemovedEventArgs : EventArgs
{
    public ProjectileRemovedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    public Projectile Projectile { get; }
}
