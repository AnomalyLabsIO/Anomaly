using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2CppInventorySystem.Items.Firearms.Attachments;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A client-observed attachment workstation. Interaction remains server-authoritative.</summary>
[ScriptType]
public sealed class Workstation
{
    private static readonly SceneWrapperCache<WorkstationController, Workstation> Cache = new(value => new Workstation(value));

    private Workstation(WorkstationController value) => Base = value;

    public WorkstationController Base { get; }
    [ScriptVisible]
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;
    [ScriptVisible]
    public Room Room => IsDestroyed ? null : Room.Get(Position);

    /// <summary>The replicated workstation status from the game.</summary>
    [ScriptVisible]
    public WorkstationController.WorkstationStatus Status => IsDestroyed ? default : (WorkstationController.WorkstationStatus)Base.NetworkStatus;

    public static ReadOnlyCollection<Workstation> List =>
        Cache.Snapshot(UnityEngine.Object.FindObjectsOfType<WorkstationController>());

    public static Workstation Get(WorkstationController value) => Cache.Get(value);
    public static bool TryGet(WorkstationController value, out Workstation workstation) => (workstation = Get(value)) != null;
    public static IEnumerable<Workstation> Get(Room room) => room == null ? Enumerable.Empty<Workstation>() : List.Where(value => value.Room == room);

    internal static void ClearAll() => Cache.Clear();
    internal static void RemoveDestroyed() => Cache.RemoveDestroyed();
}
