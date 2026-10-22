using Il2CppInteractables.Interobjects;
using ElevatorDoorBase = Il2CppInteractables.Interobjects.ElevatorDoor;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="ElevatorDoorBase"/> — the doors mounted on an
/// elevator chamber's entry/exit. Adds the chamber link and elevator group
/// on top of the base <see cref="Door"/> surface.
/// </summary>
public class ElevatorDoor : Door
{
    internal ElevatorDoor(ElevatorDoorBase doorBase) : base(doorBase)
    {
        ElevatorBase = doorBase;
    }

    /// <summary>The underlying <see cref="ElevatorDoorBase"/>.</summary>
    public ElevatorDoorBase ElevatorBase { get; }

    public new static IReadOnlyCollection<ElevatorDoor> List => Door.List.OfType<ElevatorDoor>().ToArray();
    public static ElevatorDoor Get(ElevatorDoorBase value) => Door.Get(value) as ElevatorDoor;
    public static bool TryGet(ElevatorDoorBase value, out ElevatorDoor door) => (door = Get(value)) != null;

    /// <summary>The elevator group this door belongs to (LCZ / HCZ / Nuke / etc), or null if base is destroyed.</summary>
    public ElevatorGroup? Group => ElevatorBase?.Group;

    /// <summary>The chamber this door is mounted on, or null if no chamber is linked yet.</summary>
    public Elevator Chamber
    {
        get
        {
            ElevatorChamber chamber = ElevatorBase?.Chamber;
            return chamber != null ? Elevator.Get(chamber) : null;
        }
    }
}
