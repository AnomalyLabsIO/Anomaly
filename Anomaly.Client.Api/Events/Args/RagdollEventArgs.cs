using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="Ragdoll"/> reference for spawn / removed events.</summary>
[ScriptType]
public class RagdollEventArgs : EventArgs
{
    public RagdollEventArgs(Ragdoll ragdoll) { Ragdoll = ragdoll; }
    [ScriptVisible]
    public Ragdoll Ragdoll { get; }
}
