using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>The replicated state of a room lighting controller.</summary>
public sealed class LightsController
{
    private static readonly SceneWrapperCache<RoomLightController, LightsController> Cache = new(value => new LightsController(value));

    private LightsController(RoomLightController value) => Base = value;

    public RoomLightController Base { get; }
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;
    public Room Room => IsDestroyed ? null : Room.Get(Base.Room);
    public bool LightsEnabled => !IsDestroyed && Base.NetworkLightsEnabled;
    public Color OverrideColor => IsDestroyed ? default : Base.NetworkOverrideColor;

    public static ReadOnlyCollection<LightsController> List =>
        Cache.Snapshot(UnityEngine.Object.FindObjectsOfType<RoomLightController>());

    public static LightsController Get(RoomLightController value) => Cache.Get(value);
    public static bool TryGet(RoomLightController value, out LightsController lights) => (lights = Get(value)) != null;
    public static IEnumerable<LightsController> Get(Room room) => room == null ? Enumerable.Empty<LightsController>() : List.Where(value => value.Room == room);

    internal static void ClearAll() => Cache.Clear();
    internal static void RemoveDestroyed() => Cache.RemoveDestroyed();
}
