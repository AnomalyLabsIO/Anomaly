using Il2CppHazards;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>An SCP-173 tantrum visible in the local scene.</summary>
[ScriptType]
public sealed class TantrumHazard : DecayableHazard
{
    internal TantrumHazard(TantrumEnvironmentalHazard value) : base(value) => TantrumBase = value;

    public TantrumEnvironmentalHazard TantrumBase { get; }
    public new static IReadOnlyCollection<TantrumHazard> List => Hazard.List.OfType<TantrumHazard>().ToArray();
    public Vector3 SyncedPosition => IsDestroyed ? Vector3.zero : TantrumBase.SynchronizedPosition.Position;
    public static TantrumHazard Get(TantrumEnvironmentalHazard value) => Hazard.Get(value) as TantrumHazard;
    public static bool TryGet(TantrumEnvironmentalHazard value, out TantrumHazard hazard) => (hazard = Get(value)) != null;
}
