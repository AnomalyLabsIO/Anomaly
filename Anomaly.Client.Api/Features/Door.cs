using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects.DoorUtils;

namespace Anomaly.Client.Api.Features;

public class Door
{
    private static readonly Dictionary<DoorVariant, Door> _doors = new();

    private Door(DoorVariant doorVariant)
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

    public static Door Get(DoorVariant doorVariant)
    {
        if (doorVariant == null)
            return null;

        if (_doors.TryGetValue(doorVariant, out Door door))
            return door;

        door = new Door(doorVariant);
        _doors[doorVariant] = door;
        return door;
    }

    public override string ToString() => $"Door({Name}, {Id})";
}
