using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects.DoorUtils;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class Door
{
    private static readonly Dictionary<DoorVariant, Door> _doors = new();

    protected Door(DoorVariant doorVariant)
    {
        Base = doorVariant;
    }

    public DoorVariant Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;

    [ScriptVisible]
    public string Name => Base.DoorName;

    [ScriptVisible]
    public byte Id => Base.DoorId;

    [ScriptVisible]
    public bool IsOpen => Base.TargetState;

    [ScriptVisible]
    public bool IsMoving => Base.IsMoving;

    [ScriptVisible]
    public bool CanSeeThrough => Base.CanSeeThrough;

    [ScriptVisible]
    public ushort ActiveLocks => Base.ActiveLocks;

    public DoorPermissionsPolicy RequiredPermissions => Base.RequiredPermissions;

    public ReadOnlyCollection<Room> Rooms
    {
        get
        {
            List<Room> rooms = new();
            if (Base.Rooms == null)
                return rooms.AsReadOnly();

            foreach (var roomIdentifier in Base.Rooms)
            {
                Room room = Room.Get(roomIdentifier);
                if (room != null)
                    rooms.Add(room);
            }

            return rooms.AsReadOnly();
        }
    }

    public static ReadOnlyCollection<Door> List
    {
        get
        {
            List<Door> doors = new();
            foreach (DoorVariant doorVariant in DoorVariant.AllDoors)
            {
                Door door = Get(doorVariant);
                if (door != null)
                    doors.Add(door);
            }

            return doors.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets or creates the wrapper for the given <see cref="DoorVariant"/>.
    /// Dispatches to a subtype wrapper (e.g. <see cref="BreakableDoor"/>,
    /// <see cref="CheckpointDoor"/>, <see cref="ElevatorDoor"/>) when one is
    /// registered for the runtime type.
    /// </summary>
    public static Door Get(DoorVariant doorVariant)
    {
        if (doorVariant == null)
            return null;

        if (_doors.TryGetValue(doorVariant, out Door door))
            return door;

        door = CreateForType(doorVariant);
        _doors[doorVariant] = door;
        return door;
    }

    public static bool TryGet(DoorVariant doorVariant, out Door door) => (door = Get(doorVariant)) != null;

    /// <summary>Finds an observed door by its game identifier.</summary>
    public static Door Get(byte id) => List.FirstOrDefault(door => door.Id == id);

    public static bool TryGet(byte id, out Door door) => (door = Get(id)) != null;

    public static IEnumerable<Door> Get(Room room) => room == null ? Enumerable.Empty<Door>() : List.Where(door => door.Rooms.Contains(room));

    /// <summary>Convenience downcast. Returns <c>null</c> if this wrapper isn't a <typeparamref name="T"/>.</summary>
    public T As<T>() where T : Door => this as T;

    /// <summary>True if this wrapper is a <typeparamref name="T"/>.</summary>
    public bool Is<T>() where T : Door => this is T;

    private static Door CreateForType(DoorVariant doorVariant)
    {
        // IL2CPP may hand us a base managed proxy for a derived native object.
        if (doorVariant.TryCast<Il2CppInteractables.Interobjects.CheckpointDoor>() is { } checkpoint)
            return new CheckpointDoor(checkpoint);
        if (doorVariant.TryCast<Il2CppInteractables.Interobjects.ElevatorDoor>() is { } elevator)
            return new ElevatorDoor(elevator);
        if (doorVariant.TryCast<Il2CppInteractables.Interobjects.BreakableDoor>() is { } breakable)
            return new BreakableDoor(breakable);
        return new Door(doorVariant);
    }

    internal static bool Unregister(DoorVariant value, out Door door)
    {
        door = null;
        return !ReferenceEquals(value, null) && _doors.Remove(value, out door);
    }

    internal static void ClearAll() => _doors.Clear();

    public override string ToString() => $"{GetType().Name}({Name}, {Id})";
}
