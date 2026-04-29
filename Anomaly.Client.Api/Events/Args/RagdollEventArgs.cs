using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="Ragdoll"/> reference for spawn / removed events.</summary>
public class RagdollEventArgs : EventArgs
{
    public RagdollEventArgs(Ragdoll ragdoll) { Ragdoll = ragdoll; }
    public Ragdoll Ragdoll { get; }
}
