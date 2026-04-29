using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="TeslaGate"/>. Exposes the in-progress flag and
/// inactivity timer for HUD overlays (pre-fire warnings, tesla counters,
/// faction-tinted gate visualization).
/// </summary>
/// <remarks>
/// <see cref="TeslaGate"/> exposes a static <see cref="TeslaGate.AllGates"/>
/// hashset that the wrapper iterates for <see cref="List"/>. Lifecycle is
/// driven by the engine's static <c>OnAdded</c> / <c>OnRemoved</c> events;
/// the wrapper uses lazy creation via <see cref="Get"/> to stay in sync
/// without a dedicated patch.
/// </remarks>
public class Tesla
{
    private static readonly Dictionary<TeslaGate, Tesla> _teslas = new();

    private Tesla(TeslaGate teslaBase)
    {
        Base = teslaBase;
    }

    /// <summary>The underlying <see cref="TeslaGate"/> NetworkBehaviour.</summary>
    public TeslaGate Base { get; }

    /// <summary>True while the tesla is in its shock animation.</summary>
    public bool InProgress => Base != null && Base.InProgress;

    /// <summary>Seconds the gate has been idle since the last burst.</summary>
    public float InactiveTime => Base != null ? Base.InactiveTime : 0f;

    /// <summary>The <see cref="Room"/> the tesla is mounted in, or null if not placed in any tracked room.</summary>
    public Room Room
    {
        get
        {
            if (Base == null || Base.Room == null) return null;
            return Room.Get(Base.Room);
        }
    }

    public Vector3 Position => Base != null && Base.transform != null ? Base.transform.position : Vector3.zero;

    public GameObject GameObject => Base != null ? Base.gameObject : null;

    /// <summary>All tesla gates currently registered with the engine.</summary>
    public static ReadOnlyCollection<Tesla> List
    {
        get
        {
            List<Tesla> result = new();
            if (TeslaGate.AllGates != null)
            {
                foreach (TeslaGate t in TeslaGate.AllGates)
                {
                    Tesla wrapper = Get(t);
                    if (wrapper != null)
                        result.Add(wrapper);
                }
            }
            return result.AsReadOnly();
        }
    }

    /// <summary>Gets or creates the wrapper for the given <see cref="TeslaGate"/>.</summary>
    public static Tesla Get(TeslaGate teslaBase)
    {
        if (teslaBase == null)
            return null;

        if (_teslas.TryGetValue(teslaBase, out Tesla wrapper))
            return wrapper;

        wrapper = new Tesla(teslaBase);
        _teslas[teslaBase] = wrapper;
        return wrapper;
    }

    internal static void ClearAll() => _teslas.Clear();

    public override string ToString() => $"Tesla(InProgress={InProgress}, InactiveTime={InactiveTime:F1}s)";
}
