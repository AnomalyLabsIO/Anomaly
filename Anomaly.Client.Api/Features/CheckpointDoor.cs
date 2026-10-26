using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppInteractables.Interobjects.DoorUtils;
using CheckpointDoorBase = Il2CppInteractables.Interobjects.CheckpointDoor;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="CheckpointDoorBase"/> — multi-stage checkpoint
/// gates (LCZ entrance, EZ/HCZ checkpoints) that control multiple sub-doors
/// in sequence. Adds the sub-door list and current sequence state on top of
/// the base <see cref="Door"/> surface.
/// </summary>
[ScriptType]
public class CheckpointDoor : Door
{
    internal CheckpointDoor(CheckpointDoorBase doorBase) : base(doorBase)
    {
        CheckpointBase = doorBase;
    }

    /// <summary>The underlying <see cref="CheckpointDoorBase"/>.</summary>
    public CheckpointDoorBase CheckpointBase { get; }

    public new static IReadOnlyCollection<CheckpointDoor> List => Door.List.OfType<CheckpointDoor>().ToArray();
    public static CheckpointDoor Get(CheckpointDoorBase value) => Door.Get(value) as CheckpointDoor;
    public static bool TryGet(CheckpointDoorBase value, out CheckpointDoor door) => (door = Get(value)) != null;

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
