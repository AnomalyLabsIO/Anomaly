namespace Anomaly.Client.Api.Features;

/// <summary>A client-observed firearm snapshot. Null fields are unavailable, never an inferred zero or false.</summary>
public readonly struct FirearmState : IEquatable<FirearmState>
{
    internal FirearmState(int? magazineAmmo, int? magazineCapacity, bool? magazineInserted, bool? isAiming,
        bool? isReloading, bool? isUnloading)
    {
        MagazineAmmo = magazineAmmo;
        MagazineCapacity = magazineCapacity;
        MagazineInserted = magazineInserted;
        IsAiming = isAiming;
        IsReloading = isReloading;
        IsUnloading = isUnloading;
    }

    public int? MagazineAmmo { get; }
    public int? MagazineCapacity { get; }
    public bool? MagazineInserted { get; }
    public bool? IsAiming { get; }

    /// <summary>Local-owner reload prediction. Unavailable on remote representations.</summary>
    public bool? IsReloading { get; }
    public bool? IsUnloading { get; }

    public bool Equals(FirearmState other) => MagazineAmmo == other.MagazineAmmo &&
        MagazineCapacity == other.MagazineCapacity && MagazineInserted == other.MagazineInserted &&
        IsAiming == other.IsAiming && IsReloading == other.IsReloading && IsUnloading == other.IsUnloading;

    public override bool Equals(object obj) => obj is FirearmState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MagazineAmmo, MagazineCapacity, MagazineInserted,
        IsAiming, IsReloading, IsUnloading);
}
