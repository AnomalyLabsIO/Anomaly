using Il2CppHazards;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A temporary hazard. Lifetime progression is server-owned, so no client countdown is promised.</summary>
[ScriptType]
public class DecayableHazard : Hazard
{
    internal DecayableHazard(TemporaryHazard value) : base(value) => TemporaryBase = value;

    public TemporaryHazard TemporaryBase { get; }

    public new static IReadOnlyCollection<DecayableHazard> List => Hazard.List.OfType<DecayableHazard>().ToArray();
    public static DecayableHazard Get(TemporaryHazard value) => Hazard.Get(value) as DecayableHazard;
    public static bool TryGet(TemporaryHazard value, out DecayableHazard hazard) => (hazard = Get(value)) != null;
}
