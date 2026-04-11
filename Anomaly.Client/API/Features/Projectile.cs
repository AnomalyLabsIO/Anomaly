using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items.ThrowableProjectiles;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class Projectile : Pickup
{
    private static readonly Dictionary<ThrownProjectile, Projectile> _projectiles = new();

    protected Projectile(ThrownProjectile projectileBase) : base(projectileBase)
    {
        ProjectileBase = projectileBase;
    }

    public ThrownProjectile ProjectileBase { get; }

    public GameObject RenderersRoot => ProjectileBase._renderersRoot;

    public bool AreRenderersVisible => RenderersRoot != null && RenderersRoot.activeSelf;

    public static new ReadOnlyCollection<Projectile> List
    {
        get
        {
            List<Projectile> projectiles = new();
            foreach (ThrownProjectile projectile in UnityEngine.Object.FindObjectsOfType<ThrownProjectile>())
            {
                Projectile wrapper = Get(projectile);
                if (wrapper != null)
                    projectiles.Add(wrapper);
            }

            return projectiles.AsReadOnly();
        }
    }

    public static Projectile Get(ThrownProjectile projectileBase)
    {
        if (projectileBase == null)
            return null;

        if (_projectiles.TryGetValue(projectileBase, out Projectile projectile))
            return projectile;

        projectile = new Projectile(projectileBase);
        _projectiles[projectileBase] = projectile;
        return projectile;
    }

    public override string ToString() => $"Projectile({TypeName}, {Serial})";
}
