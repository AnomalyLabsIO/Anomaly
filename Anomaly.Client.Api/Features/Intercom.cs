using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2CppPlayerRoles.Voice;
using UnityEngine;
using IntercomBase = Il2CppPlayerRoles.Voice.Intercom;

namespace Anomaly.Client.Api.Features;

/// <summary>The client's intercom state and timer. Speaker identity is not included in the replicated state.</summary>
public sealed class Intercom
{
    private static readonly SceneWrapperCache<IntercomBase, Intercom> Cache = new(value => new Intercom(value));

    private Intercom(IntercomBase value) => Base = value;

    public IntercomBase Base { get; }
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;
    public Room Room => IsDestroyed ? null : Room.Get(Position);
    public IntercomState State => IsDestroyed ? default : (IntercomState)Base.Network_state;
    public float RemainingTime => IsDestroyed ? 0f : Base.RemainingTime;

    public static Intercom Instance => Get(IntercomBase._singleton);
    public static ReadOnlyCollection<Intercom> List =>
        Cache.Snapshot(UnityEngine.Object.FindObjectsOfType<IntercomBase>());

    public static Intercom Get(IntercomBase value) => Cache.Get(value);
    public static bool TryGet(IntercomBase value, out Intercom intercom) => (intercom = Get(value)) != null;

    internal static void ClearAll() => Cache.Clear();
    internal static void RemoveDestroyed() => Cache.RemoveDestroyed();
}
