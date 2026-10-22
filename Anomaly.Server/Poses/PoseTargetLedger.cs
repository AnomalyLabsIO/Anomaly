using System.Collections.Generic;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Poses;

internal sealed class PoseTargetState
{
    internal PoseBinding Delivery { get; set; }
    internal PoseBinding SentBinding { get; set; }
    internal bool Visible { get; set; }
    internal bool Active { get; set; }
    internal bool Retired { get; set; }
    internal ulong OverrideGeneration { get; set; }
    internal ulong CompositionRevision { get; set; }
    internal ulong SampleSequence { get; set; }
}

/// <summary>Bounded delivery history; failed clear delivery retains only a bounded tombstone.</summary>
internal sealed class PoseTargetLedger
{
    internal const int MaximumTargets = 256;
    internal readonly Dictionary<uint, PoseTargetState> Targets = new Dictionary<uint, PoseTargetState>();

    internal PoseTargetState GetOrAdd(uint id, out bool resetRequired)
    {
        resetRequired = false;
        if (Targets.TryGetValue(id, out PoseTargetState target)) return target;
        if (Targets.Count >= MaximumTargets) { Targets.Clear(); resetRequired = true; }
        target = new PoseTargetState(); Targets.Add(id, target); return target;
    }
    internal void Retire(uint id)
    {
        if (!Targets.TryGetValue(id, out PoseTargetState target)) return;
        target.Retired = true; target.Delivery = null; target.Visible = false;
    }
    internal void Prune(uint id)
    {
        if (Targets.TryGetValue(id, out PoseTargetState target) && target.Retired && !target.Active)
            Targets.Remove(id);
    }
    internal void Reset() => Targets.Clear();
}
