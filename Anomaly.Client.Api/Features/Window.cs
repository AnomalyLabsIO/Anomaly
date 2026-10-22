using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>A client-observed breakable window. The broken state is replicated; server health is not exposed.</summary>
public sealed class Window
{
    private static readonly SceneWrapperCache<BreakableWindow, Window> Cache = new(value => new Window(value));

    private Window(BreakableWindow value) => Base = value;

    public BreakableWindow Base { get; }
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;
    public Room Room => IsDestroyed ? null : Room.Get(Position);
    public bool IsBroken => !IsDestroyed && Base.NetworkIsBroken;
    public uint NetworkId => IsDestroyed ? 0 : Base.NetworkId;

    public static ReadOnlyCollection<Window> List =>
        Cache.Snapshot(UnityEngine.Object.FindObjectsOfType<BreakableWindow>());

    public static Window Get(BreakableWindow value) => Cache.Get(value);
    public static bool TryGet(BreakableWindow value, out Window window) => (window = Get(value)) != null;
    public static IEnumerable<Window> Get(Room room) => room == null ? Enumerable.Empty<Window>() : List.Where(value => value.Room == room);

    internal static void ClearAll() => Cache.Clear();
    internal static void RemoveDestroyed() => Cache.RemoveDestroyed();
}
