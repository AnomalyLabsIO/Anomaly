using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2CppHazards;
using Il2CppPlayerRoles.PlayableScps.Scp939;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A hazard known to the local scene. This does not report server damage or the server's affected-player list.</summary>
[ScriptType]
public class Hazard
{
    private static readonly SceneWrapperCache<EnvironmentalHazard, Hazard> Cache = new(Create);

    protected Hazard(EnvironmentalHazard value) => Base = value;

    public EnvironmentalHazard Base { get; }
    [ScriptVisible]
    public bool IsDestroyed => Base == null;
    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;
    public Transform Transform => IsDestroyed ? null : Base.transform;
    public Vector3 Position => IsDestroyed ? Vector3.zero : Base.transform.position;
    [ScriptVisible]
    public Room Room => IsDestroyed ? null : Room.Get(Position);
    public Vector3 SourcePosition => IsDestroyed ? Vector3.zero : Base.SourcePosition;
    public Vector3 SourceOffset => IsDestroyed ? Vector3.zero : Base.SourceOffset;
    [ScriptVisible]
    public float MaxDistance => IsDestroyed ? 0f : Base.MaxDistance;
    [ScriptVisible]
    public float MaxHeightDistance => IsDestroyed ? 0f : Base.MaxHeightDistance;

    /// <summary>The local hazard component's active state; this is not a server acceptance or damage guarantee.</summary>
    [ScriptVisible]
    public bool IsActive => !IsDestroyed && Base.IsActive;

    public static ReadOnlyCollection<Hazard> List =>
        Cache.Snapshot(UnityEngine.Object.FindObjectsOfType<EnvironmentalHazard>());

    public static Hazard Get(EnvironmentalHazard value) => Cache.Get(value);
    public static bool TryGet(EnvironmentalHazard value, out Hazard hazard) => (hazard = Get(value)) != null;
    public static IEnumerable<Hazard> Get(Room room) => room == null ? Enumerable.Empty<Hazard>() : List.Where(value => value.Room == room);
    public T As<T>() where T : Hazard => this as T;
    public bool Is<T>() where T : Hazard => this is T;

    private static Hazard Create(EnvironmentalHazard value)
    {
        if (value.TryCast<TantrumEnvironmentalHazard>() is { } tantrum)
            return new TantrumHazard(tantrum);
        if (value.TryCast<Scp939AmnesticCloudInstance>() is { } cloud)
            return new AmnesticCloudHazard(cloud);
        if (value.TryCast<SinkholeEnvironmentalHazard>() is { } sinkhole)
            return new SinkholeHazard(sinkhole);
        if (value.TryCast<TemporaryHazard>() is { } temporary)
            return new DecayableHazard(temporary);
        return new Hazard(value);
    }

    internal static bool Unregister(EnvironmentalHazard value, out Hazard hazard) => Cache.Remove(value, out hazard);
    internal static void ClearAll() => Cache.Clear();
    internal static void RemoveDestroyed() => Cache.RemoveDestroyed();
}
