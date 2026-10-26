using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="ElevatorChamber"/>. Exposes sequence state,
/// destination level, and direction for floor-indicator HUDs and
/// elevator-music mods. Read-only — call/recall is server-authoritative.
/// </summary>
/// <remarks>
/// Lifecycle is driven by the engine's static
/// <see cref="ElevatorChamber.OnElevatorSpawned"/> /
/// <see cref="ElevatorChamber.OnElevatorRemoved"/> events; the wrapper uses
/// lazy creation via <see cref="Get"/> against
/// <see cref="ElevatorChamber.AllChambers"/> for enumeration.
/// </remarks>
[ScriptType]
public class Elevator
{
    private static readonly Dictionary<ElevatorChamber, Elevator> _elevators = new();

    private Elevator(ElevatorChamber chamberBase)
    {
        Base = chamberBase;
    }

    /// <summary>The underlying <see cref="ElevatorChamber"/> NetworkBehaviour.</summary>
    public ElevatorChamber Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;

    public static bool TryGet(ElevatorChamber value, out Elevator wrapper)
    {
        wrapper = Get(value);
        return wrapper != null;
    }

    /// <summary>True if the chamber is in <see cref="ElevatorChamber.ElevatorSequence.Ready"/> phase.</summary>
    [ScriptVisible]
    public bool IsReady => Base != null && Base.IsReady;

    /// <summary>True if the chamber is ready to accept user input (e.g. a button press).</summary>
    [ScriptVisible]
    public bool IsReadyForUserInput => Base != null && Base.IsReadyForUserInput;

    /// <summary>The level the chamber is currently moving to (or has stopped at).</summary>
    [ScriptVisible]
    public int DestinationLevel => Base != null ? Base.DestinationLevel : 0;

    /// <summary>True if the chamber is currently traveling upward.</summary>
    [ScriptVisible]
    public bool GoingUp => Base != null && Base.GoingUp;

    /// <summary>The level above the current one.</summary>
    [ScriptVisible]
    public int NextLevel => Base != null ? Base.NextLevel : 0;

    /// <summary>The level below the current one.</summary>
    [ScriptVisible]
    public int PreviousLevel => Base != null ? Base.PreviousLevel : 0;

    /// <summary>The current sequence state (Ready / Arrived / DoorOpening / Moving / DoorClosing).</summary>
    [ScriptVisible]
    public ElevatorChamber.ElevatorSequence CurrentSequence => Base != null ? Base.CurSequence : ElevatorChamber.ElevatorSequence.Ready;

    /// <summary>Seconds elapsed in the current sequence phase.</summary>
    [ScriptVisible]
    public float SequenceElapsed => Base != null ? Base.SequenceElapsed : 0f;

    /// <summary>The room containing the chamber, or null.</summary>
    [ScriptVisible]
    public Room Room
    {
        get
        {
            if (Base == null || Base.CurrentRoom == null) return null;
            return Room.Get(Base.CurrentRoom);
        }
    }

    public Vector3 Position => Base != null && Base.transform != null ? Base.transform.position : Vector3.zero;

    public GameObject GameObject => Base != null ? Base.gameObject : null;

    /// <summary>All elevator chambers currently registered with the engine.</summary>
    public static ReadOnlyCollection<Elevator> List
    {
        get
        {
            List<Elevator> result = new();
            if (ElevatorChamber.AllChambers != null)
            {
                foreach (ElevatorChamber c in ElevatorChamber.AllChambers)
                {
                    Elevator wrapper = Get(c);
                    if (wrapper != null)
                        result.Add(wrapper);
                }
            }
            return result.AsReadOnly();
        }
    }

    public static Elevator Get(ElevatorChamber chamberBase)
    {
        if (chamberBase == null) return null;
        if (_elevators.TryGetValue(chamberBase, out Elevator wrapper))
            return wrapper;
        wrapper = new Elevator(chamberBase);
        _elevators[chamberBase] = wrapper;
        return wrapper;
    }

    internal static bool Unregister(ElevatorChamber chamberBase) =>
        chamberBase != null && _elevators.Remove(chamberBase);

    internal static void ClearAll() => _elevators.Clear();

    public override string ToString() => $"Elevator(Sequence={CurrentSequence}, Destination={DestinationLevel})";
}
