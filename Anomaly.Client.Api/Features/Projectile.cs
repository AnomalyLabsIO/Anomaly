using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInventorySystem.Items.ThrowableProjectiles;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class Projectile : Pickup
{
    private static readonly Dictionary<ThrownProjectile, Projectile> _projectiles = new();
    private static readonly Dictionary<ushort, Projectile> _serialCache = new();

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
            List<Projectile> snapshot = new(_projectiles.Count);
            foreach (Projectile p in _projectiles.Values)
            {
                if (p != null && p.ProjectileBase != null)
                    snapshot.Add(p);
            }
            return snapshot.AsReadOnly();
        }
    }

    public static Projectile Get(ThrownProjectile projectileBase)
    {
        if (projectileBase == null)
            return null;

        if (_projectiles.TryGetValue(projectileBase, out Projectile projectile))
        {
            RefreshSerialIndex(projectile);
            return projectile;
        }

        ScriptHostAccess.ChargeRegistryEntries(_projectiles.Count);
        projectile = CreateWrapper(projectileBase);
        _projectiles[projectileBase] = projectile;
        RefreshSerialIndex(projectile);
        return projectile;
    }

    private static Projectile CreateWrapper(ThrownProjectile value)
    {
        if (value.TryCast<Il2CppInventorySystem.Items.ThrowableProjectiles.Scp018Projectile>() is { } ball)
            return new Scp018Projectile(ball);
        if (value.TryCast<Il2CppInventorySystem.Items.ThrowableProjectiles.Scp2176Projectile>() is { } light)
            return new Scp2176Projectile(light);
        if (value.TryCast<FlashbangGrenade>() is { } flashbang)
            return new FlashbangProjectile(flashbang);
        if (value.TryCast<ExplosionGrenade>() is { } explosive)
            return new ExplosiveGrenadeProjectile(explosive);
        if (value.TryCast<TimeGrenade>() is { } timed)
            return new TimedGrenadeProjectile(timed);
        return new Projectile(value);
    }

    /// <summary>
    /// Look up a projectile by its serial. Returns <c>null</c> if no projectile
    /// with that serial is currently tracked.
    /// </summary>
    public static new Projectile Get(ushort serial)
    {
        if (serial == 0)
            return null;
        _serialCache.TryGetValue(serial, out Projectile p);
        return p != null && !p.IsDestroyed ? p : null;
    }

    /// <inheritdoc cref="Get(ushort)"/>
    public static bool TryGet(ushort serial, out Projectile projectile)
    {
        if (serial == 0)
        {
            projectile = null;
            return false;
        }
        projectile = Get(serial);
        return projectile != null;
    }

    internal override void OnRemove()
    {
        if (ProjectileBase != null)
            _projectiles.Remove(ProjectileBase);

        ushort serial = SafeProjectileSerial();
        if (serial != 0 && _serialCache.TryGetValue(serial, out Projectile cached) && ReferenceEquals(cached, this))
            _serialCache.Remove(serial);

        base.OnRemove();
    }

    /// <summary>
    /// Drops every cached projectile. Called by the loader on client disconnect
    /// alongside <see cref="Pickup.ClearAll"/> so the projectile-specific
    /// caches are also cleared.
    /// </summary>
    internal static void ClearAllProjectiles()
    {
        _projectiles.Clear();
        _serialCache.Clear();
    }

    private static void RefreshSerialIndex(Projectile projectile)
    {
        ushort serial = projectile.SafeProjectileSerial();
        if (serial == 0)
            return;

        bool known = _serialCache.TryGetValue(serial, out Projectile existing);
        if (!known)
            ScriptHostAccess.ChargeRegistryEntries(_serialCache.Count);
        if (known && !ReferenceEquals(existing, projectile))
        {
            if (existing != null)
                _projectiles.Remove(existing.ProjectileBase);
        }
        _serialCache[serial] = projectile;
    }

    private ushort SafeProjectileSerial()
    {
        try
        {
            if (ProjectileBase == null)
                return 0;
            return ProjectileBase.Info.Serial;
        }
        catch (Exception) when (!ScriptHostAccess.IsActive)
        {
            return 0;
        }
    }

    public override string ToString() => $"Projectile({TypeName}, {Serial})";
}
