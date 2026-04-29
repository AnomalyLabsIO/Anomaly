using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2Cpp;
using Il2CppMapGeneration.Distributors;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="Il2CppMapGeneration.Distributors.Locker"/>.
/// Aggregates the locker's <see cref="LockerChamber"/> array for chamber-aware
/// HUDs (loot indicators, "what's inside" overlays, faction-themed locker tints).
/// </summary>
/// <remarks>
/// <para>Lockers are <c>SpawnableStructure</c>s placed by the map generator;
/// the wrapper enumerates via <see cref="Object.FindObjectsOfType{T}()"/>.
/// Chamber state is replicated through the locker's <c>OpenedChambers</c>
/// SyncVar (a flag bitmask).</para>
/// </remarks>
public class Locker
{
    private static readonly Dictionary<Il2CppMapGeneration.Distributors.Locker, Locker> _lockers = new();

    private LockerChamberWrapper[] _chamberCache;

    private Locker(Il2CppMapGeneration.Distributors.Locker lockerBase)
    {
        Base = lockerBase;
    }

    /// <summary>The underlying <see cref="Il2CppMapGeneration.Distributors.Locker"/>.</summary>
    public Il2CppMapGeneration.Distributors.Locker Base { get; }

    /// <summary>Bitmask of currently-opened chambers (bit N set ⇒ chamber N is open).</summary>
    public ushort OpenedChambersMask => Base != null ? Base.NetworkOpenedChambers : (ushort)0;

    /// <summary>Wrapped chamber views. Cached on first access.</summary>
    public IReadOnlyList<LockerChamberWrapper> Chambers
    {
        get
        {
            if (_chamberCache != null)
                return _chamberCache;

            if (Base == null || Base.Chambers == null)
            {
                _chamberCache = System.Array.Empty<LockerChamberWrapper>();
                return _chamberCache;
            }

            LockerChamber[] raw = Base.Chambers;
            LockerChamberWrapper[] wrapped = new LockerChamberWrapper[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                wrapped[i] = new LockerChamberWrapper(raw[i], (byte)i, this);

            _chamberCache = wrapped;
            return _chamberCache;
        }
    }

    /// <summary>The room containing the locker, or null if not in any tracked room.</summary>
    public Room Room
    {
        get
        {
            if (Base == null || Base.transform == null) return null;
            Transform t = Base.transform;
            while (t != null)
            {
                Il2CppMapGeneration.RoomIdentifier rid = t.GetComponent<Il2CppMapGeneration.RoomIdentifier>();
                if (rid != null)
                    return Room.Get(rid);
                t = t.parent;
            }
            return null;
        }
    }

    public Vector3 Position => Base != null && Base.transform != null ? Base.transform.position : Vector3.zero;

    public GameObject GameObject => Base != null ? Base.gameObject : null;

    /// <summary>All lockers currently in the scene.</summary>
    public static ReadOnlyCollection<Locker> List
    {
        get
        {
            List<Locker> result = new();
            foreach (Il2CppMapGeneration.Distributors.Locker l in UnityEngine.Object.FindObjectsOfType<Il2CppMapGeneration.Distributors.Locker>())
            {
                Locker wrapper = Get(l);
                if (wrapper != null)
                    result.Add(wrapper);
            }
            return result.AsReadOnly();
        }
    }

    public static Locker Get(Il2CppMapGeneration.Distributors.Locker lockerBase)
    {
        if (lockerBase == null) return null;
        if (_lockers.TryGetValue(lockerBase, out Locker wrapper))
            return wrapper;
        wrapper = new Locker(lockerBase);
        _lockers[lockerBase] = wrapper;
        return wrapper;
    }

    internal static void ClearAll() => _lockers.Clear();

    public override string ToString() => $"Locker(ChamberCount={Chambers.Count}, OpenedMask=0x{OpenedChambersMask:X4})";
}

/// <summary>
/// Per-chamber view inside a <see cref="Locker"/>. Read-only — interaction is
/// server-authoritative via <c>Locker.ServerInteract</c>.
/// </summary>
public sealed class LockerChamberWrapper
{
    internal LockerChamberWrapper(LockerChamber chamberBase, byte index, Locker parent)
    {
        Base = chamberBase;
        Index = index;
        Parent = parent;
    }

    /// <summary>The underlying <see cref="LockerChamber"/> MonoBehaviour.</summary>
    public LockerChamber Base { get; }

    /// <summary>Zero-based index of this chamber within its locker's <see cref="Locker.Chambers"/>.</summary>
    public byte Index { get; }

    /// <summary>The locker this chamber belongs to.</summary>
    public Locker Parent { get; }

    /// <summary>True if the chamber's door is currently open.</summary>
    public bool IsOpen => Base != null && Base.IsOpen;

    /// <summary>True if any player has ever opened this chamber this round.</summary>
    public bool WasEverOpened => Base != null && Base.WasEverOpened;

    /// <summary>The keycard permissions required to open this chamber.</summary>
    public Il2CppInteractables.Interobjects.DoorUtils.DoorPermissionFlags RequiredPermissions =>
        Base != null ? Base.RequiredPermissions : default;

    /// <summary>The item types that this chamber is allowed to spawn.</summary>
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ItemType> AcceptableItems =>
        Base?.AcceptableItems;

    /// <summary>The current contents of the chamber as raw <see cref="ItemPickupBase"/> instances.</summary>
    public Il2CppSystem.Collections.Generic.List<Il2CppInventorySystem.Items.Pickups.ItemPickupBase> Contents =>
        Base?.Content;

    /// <summary>Spawnpoint transform for items placed in this chamber.</summary>
    public Transform Spawnpoint => Base?.Spawnpoint;

    public override string ToString() => $"LockerChamber[{Index}](IsOpen={IsOpen})";
}
