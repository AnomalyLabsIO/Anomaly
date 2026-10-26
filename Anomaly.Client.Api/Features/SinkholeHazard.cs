using Il2CppHazards;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A static sinkhole visible in the local scene.</summary>
[ScriptType]
public sealed class SinkholeHazard : Hazard
{
    internal SinkholeHazard(SinkholeEnvironmentalHazard value) : base(value) => SinkholeBase = value;

    public SinkholeEnvironmentalHazard SinkholeBase { get; }
    public new static IReadOnlyCollection<SinkholeHazard> List => Hazard.List.OfType<SinkholeHazard>().ToArray();
    public static SinkholeHazard Get(SinkholeEnvironmentalHazard value) => Hazard.Get(value) as SinkholeHazard;
    public static bool TryGet(SinkholeEnvironmentalHazard value, out SinkholeHazard hazard) => (hazard = Get(value)) != null;
}
