using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects.DoorUtils;
using CheckpointDoorBase = Il2CppInteractables.Interobjects.CheckpointDoor;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="CheckpointDoorBase"/> — multi-stage checkpoint
/// gates (LCZ entrance, EZ/HCZ checkpoints) that control multiple sub-doors
/// in sequence. Adds the sub-door list and current sequence state on top of
/// the base <see cref="Door"/> surface.
/// </summary>
public class CheckpointDoor : Door
{
    internal CheckpointDoor(CheckpointDoorBase doorBase) : base(doorBase)
    {
        CheckpointBase = doorBase;
    }

    /// <summary>The underlying <see cref="CheckpointDoorBase"/>.</summary>
    public CheckpointDoorBase CheckpointBase { get; }

    /// <summary>The current sequence phase (Idle / Open / Closing / etc).</summary>
    public CheckpointDoorBase.SequenceState CurrentSequence =>
        CheckpointBase != null ? CheckpointBase.CurSequence : CheckpointDoorBase.SequenceState.Idle;

    /// <summary>The wrapped sub-doors that this checkpoint coordinates.</summary>
    public ReadOnlyCollection<Door> SubDoors
    {
        get
        {
            List<Door> result = new();
            if (CheckpointBase?.SubDoors == null)
                return result.AsReadOnly();
            foreach (DoorVariant sub in CheckpointBase.SubDoors)
            {
                Door door = Door.Get(sub);
                if (door != null)
                    result.Add(door);
            }
            return result.AsReadOnly();
        }
    }
}
