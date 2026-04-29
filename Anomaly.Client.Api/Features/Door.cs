using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects.DoorUtils;

namespace Anomaly.Client.Api.Features;

public class Door
{
    private static readonly Dictionary<DoorVariant, Door> _doors = new();
    private static readonly Dictionary<System.Type, System.Func<DoorVariant, Door>> _subtypeFactories = new()
    {
        { typeof(Il2CppInteractables.Interobjects.BreakableDoor),
            v => new BreakableDoor((Il2CppInteractables.Interobjects.BreakableDoor)v) },
        { typeof(Il2CppInteractables.Interobjects.CheckpointDoor),
            v => new CheckpointDoor((Il2CppInteractables.Interobjects.CheckpointDoor)v) },
        { typeof(Il2CppInteractables.Interobjects.ElevatorDoor),
            v => new ElevatorDoor((Il2CppInteractables.Interobjects.ElevatorDoor)v) },
    };

    protected Door(DoorVariant doorVariant)
    {
        Base = doorVariant;
    }

    public DoorVariant Base { get; }

    public string Name => Base.DoorName;

    public byte Id => Base.DoorId;

    public bool IsOpen => Base.TargetState;

    public bool IsMoving => Base.IsMoving;

    public bool CanSeeThrough => Base.CanSeeThrough;

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

    /// <summary>Convenience downcast. Returns <c>null</c> if this wrapper isn't a <typeparamref name="T"/>.</summary>
    public T As<T>() where T : Door => this as T;

    /// <summary>True if this wrapper is a <typeparamref name="T"/>.</summary>
    public bool Is<T>() where T : Door => this is T;

    private static Door CreateForType(DoorVariant doorVariant)
    {
        System.Type t = doorVariant.GetType();
        while (t != null && t != typeof(DoorVariant))
        {
            if (_subtypeFactories.TryGetValue(t, out var factory))
                return factory(doorVariant);
            t = t.BaseType;
        }
        return new Door(doorVariant);
    }

    internal static void ClearAll() => _doors.Clear();

    public override string ToString() => $"{GetType().Name}({Name}, {Id})";
}
