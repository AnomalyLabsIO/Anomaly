using Il2CppInventorySystem.Items.Firearms;
using Il2CppInventorySystem.Items.Firearms.Modules;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="Il2CppInventorySystem.Items.Firearms.Firearm"/>.
/// Exposes magazine state, fire-mode info, and module access for HUD overlays
/// (custom ammo counters, hit-marker mods, attachment indicators) without
/// requiring consumers to dig through the modular firearm subcomponent system.
/// </summary>
/// <remarks>
/// SCP:SL firearms are <c>ModularAutosyncItem</c>s — most state lives on
/// per-firearm modules (<see cref="MagazineModule"/>, etc.) rather than on
/// the firearm itself. The wrapper exposes the most-used modules directly
/// and provides <see cref="TryGetModule{T}"/> as the escape hatch.
/// </remarks>
[ScriptType]
public class FirearmItem : Item
{
    internal FirearmItem(Firearm firearmBase) : base(firearmBase)
    {
        FirearmBase = firearmBase;
    }

    /// <summary>The underlying <see cref="Firearm"/>.</summary>
    public Firearm FirearmBase { get; }

    /// <summary>The firearm's magazine module, or null if it doesn't have one.</summary>
    public MagazineModule Magazine =>
        TryGetModule<MagazineModule>(out var m) ? m : null;

    /// <summary>Rounds currently loaded in the magazine, or 0 if no magazine module.</summary>
    public int AmmoInMagazine
    {
        get
        {
            MagazineModule mag = Magazine;
            return mag != null ? mag.AmmoStored : 0;
        }
    }

    /// <summary>Magazine capacity, or 0 if no magazine module.</summary>
    public int MagazineCapacity
    {
        get
        {
            MagazineModule mag = Magazine;
            return mag != null ? mag.AmmoMax : 0;
        }
    }

    /// <summary>
    /// True if the firearm is currently being aimed (ADS). Reads the
    /// <c>LinearAdsModule</c> if present; firearms without an ADS module
    /// (e.g. melee weapons treated as firearms) always return false.
    /// </summary>
    public bool IsAiming
    {
        get
        {
            return TryGetModule<LinearAdsModule>(out LinearAdsModule ads) && ads != null && ads.AdsTarget;
        }
    }

    /// <summary>
    /// Reads the state known to this client. Magazine values require received sync data;
    /// ADS is local prediction or received remote state. Reload/unload is exposed only
    /// for the owning client. Unsupported or unavailable fields remain null.
    /// </summary>
    public FirearmState GetState()
    {
        if (IsDestroyed)
            return default;

        MagazineModule magazine = Magazine;
        bool hasMagazineData = magazine != null && MagazineModule.SyncData.ContainsKey(Serial);
        bool? aiming = null;
        if (TryGetModule<LinearAdsModule>(out var ads) &&
            (IsLocalPlayerItem || LinearAdsModule.SyncData.ContainsKey(Serial)))
            aiming = ads.AdsTarget;

        bool? reloading = null;
        bool? unloading = null;
        if (IsLocalPlayerItem && FirearmBase.TryGetModule<IReloaderModule>(out var reloader))
        {
            reloading = reloader.IsReloading;
            unloading = reloader.IsUnloading;
        }

        return new FirearmState(
            hasMagazineData ? magazine.AmmoStored : null,
            magazine != null ? magazine.AmmoMax : null,
            hasMagazineData ? magazine.MagazineInserted : null,
            aiming, reloading, unloading);
    }

    /// <summary>
    /// Returns the typed module of <typeparamref name="T"/> if present, mirroring
    /// the engine's <c>ModularAutosyncItem.TryGetModule&lt;T&gt;</c> method.
    /// Use for any module not directly exposed by this wrapper.
    /// </summary>
    public bool TryGetModule<T>(out T module) where T : ModuleBase
    {
        module = null;
        try
        {
            return FirearmBase != null && FirearmBase.TryGetModule(out module);
        }
        catch
        {
            return false;
        }
    }

    public override string ToString() => $"FirearmItem({TypeName}, {Serial}, Ammo={AmmoInMagazine}/{MagazineCapacity})";
}
