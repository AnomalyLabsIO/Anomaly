using Il2CppPlayerRoles.PlayableScps.Scp939;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>An SCP-939 cloud's replicated presentation state.</summary>
public sealed class AmnesticCloudHazard : DecayableHazard
{
    internal AmnesticCloudHazard(Scp939AmnesticCloudInstance value) : base(value) => CloudBase = value;

    public Scp939AmnesticCloudInstance CloudBase { get; }
    public new static IReadOnlyCollection<AmnesticCloudHazard> List => Hazard.List.OfType<AmnesticCloudHazard>().ToArray();
    public Player Owner => IsDestroyed ? null : Player.Get(CloudBase.Owner);
    public Scp939AmnesticCloudInstance.CloudState State => IsDestroyed ? default : CloudBase.State;
    public byte HoldDuration => IsDestroyed ? (byte)0 : CloudBase.HoldDuration;
    public Vector3 SyncedPosition => IsDestroyed ? Vector3.zero : CloudBase.SyncedPosition.Position;
    public static AmnesticCloudHazard Get(Scp939AmnesticCloudInstance value) => Hazard.Get(value) as AmnesticCloudHazard;
    public static bool TryGet(Scp939AmnesticCloudInstance value, out AmnesticCloudHazard hazard) => (hazard = Get(value)) != null;
}
