using Anomaly.Client.Api.Features;
using Il2CppMapGeneration.Distributors;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An observed generator flag transition after replication, without an inferred interacting player.</summary>
public sealed class GeneratorStateChangedEventArgs : EventArgs
{
    public GeneratorStateChangedEventArgs(Generator generator, Scp079Generator.GeneratorFlags previousFlags, Scp079Generator.GeneratorFlags flags)
    {
        Generator = generator;
        PreviousFlags = previousFlags;
        Flags = flags;
    }

    public Generator Generator { get; }
    public Scp079Generator.GeneratorFlags PreviousFlags { get; }
    public Scp079Generator.GeneratorFlags Flags { get; }
}
