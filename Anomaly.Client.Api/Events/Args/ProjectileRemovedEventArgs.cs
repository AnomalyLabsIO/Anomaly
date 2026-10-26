using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class ProjectileRemovedEventArgs : EventArgs
{
    public ProjectileRemovedEventArgs(Projectile projectile)
    {
        Projectile = projectile;
    }

    [ScriptVisible]
    public Projectile Projectile { get; }
}
