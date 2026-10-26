using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class ProjectileSpawnedEventArgs : EventArgs
{
    public ProjectileSpawnedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    [ScriptVisible]
    public Projectile Projectile { get; }
}
