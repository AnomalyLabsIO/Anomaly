using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class ShootingTargetAdminToy : AdminToy
{
    internal ShootingTargetAdminToy(ShootingTarget toyBase) : base(toyBase)
    {
        TargetBase = toyBase;
    }

    public ShootingTarget TargetBase { get; }

    public string TargetName => TargetBase._targetName;

    public bool SyncMode => TargetBase._syncMode;

    public float HitPoints => TargetBase._hp;

    public int MaxHitPoints => TargetBase._maxHp;

    public Vector3 CenterOfMass => TargetBase.CenterOfMass;
}
