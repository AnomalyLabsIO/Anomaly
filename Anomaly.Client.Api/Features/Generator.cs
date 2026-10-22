using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppMapGeneration.Distributors;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="Scp079Generator"/>. Exposes activation /
/// engagement state, cooldown timing, and door state for HUD overlays
/// (charge percentages, "generators remaining" counters, faction-tinted
/// glow indicators).
/// </summary>
/// <remarks>
/// Generators are <c>NetworkBehaviour</c>s; their state is replicated to
/// every client. The wrapper cache uses the same factory pattern as
/// <see cref="Door"/> / <see cref="Pickup"/>: <see cref="Get"/> creates
/// on first access, <see cref="List"/> walks the scene's
/// <c>FindObjectsOfType&lt;Scp079Generator&gt;</c>.
/// </remarks>
public class Generator
{
    private static readonly Dictionary<Scp079Generator, Generator> _generators = new();

    private Generator(Scp079Generator generatorBase)
    {
        Base = generatorBase;
    }

    /// <summary>The underlying <see cref="Scp079Generator"/> NetworkBehaviour.</summary>
    public Scp079Generator Base { get; }

    public bool IsDestroyed => Base == null;
    public Transform Transform => IsDestroyed ? null : Base.transform;

    /// <summary>True if the door panel is open (lever / interior accessible).</summary>
    public bool IsOpen => Base.IsOpen;

    /// <summary>True if the unlock cooldown has elapsed and the generator can be engaged.</summary>
    public bool IsUnlocked => Base.IsUnlocked;

    /// <summary>True once the generator has finished its activation sequence.</summary>
    public bool IsEngaged => HasFlag(Base, Scp079Generator.GeneratorFlags.Engaged);

    /// <summary>True while the generator is in its activation countdown phase (lever pulled, not yet engaged).</summary>
    public bool IsActivating => HasFlag(Base, Scp079Generator.GeneratorFlags.Activating);

    /// <summary>True if the generator's activation cooldown is satisfied (matches game's <c>ActivationReady</c>).</summary>
    public bool IsActivationReady => Base.ActivationReady;

    /// <summary>Total seconds the activation sequence takes from lever-pull to engaged.</summary>
    public float TotalActivationTime => Base.TotalActivationTime;

    /// <summary>Total seconds the deactivation sequence takes.</summary>
    public float TotalDeactivationTime => Base.TotalDeactivationTime;

    /// <summary>The <see cref="Room"/> containing this generator, or null if the generator isn't placed in any tracked room.</summary>
    public Room Room
    {
        get
        {
            if (Base == null || Base.transform == null) return null;
            // Walk up the transform parent chain looking for a RoomIdentifier.
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

    /// <summary>All generators currently in the scene.</summary>
    public static ReadOnlyCollection<Generator> List
    {
        get
        {
            List<Generator> result = new();
            foreach (Scp079Generator g in UnityEngine.Object.FindObjectsOfType<Scp079Generator>())
            {
                Generator wrapper = Get(g);
                if (wrapper != null)
                    result.Add(wrapper);
            }
            return result.AsReadOnly();
        }
    }

    /// <summary>Gets or creates the wrapper for the given <see cref="Scp079Generator"/>.</summary>
    public static Generator Get(Scp079Generator generatorBase)
    {
        if (generatorBase == null)
            return null;

        if (_generators.TryGetValue(generatorBase, out Generator wrapper))
            return wrapper;

        wrapper = new Generator(generatorBase);
        _generators[generatorBase] = wrapper;
        return wrapper;
    }

    public static bool TryGet(Scp079Generator value, out Generator generator) => (generator = Get(value)) != null;

    internal static void ClearAll() => _generators.Clear();

    internal static void RemoveDestroyed()
    {
        foreach (Scp079Generator value in _generators.Keys.Where(value => value == null).ToArray())
            _generators.Remove(value);
    }

    private static bool HasFlag(Scp079Generator g, Scp079Generator.GeneratorFlags flag)
    {
        try
        {
            return ((byte)g.Network_flags & (byte)flag) != 0;
        }
        catch
        {
            return false;
        }
    }

    public override string ToString() => $"Generator(IsEngaged={IsEngaged}, IsActivating={IsActivating})";
}
