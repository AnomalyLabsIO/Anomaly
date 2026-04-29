using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when an <see cref="Il2CppMapGeneration.Distributors.Scp079Generator"/>
/// reaches the engaged state.
/// </summary>
public class GeneratorEngagedEventArgs : EventArgs
{
    public GeneratorEngagedEventArgs(Generator generator)
    {
        Generator = generator;
    }

    /// <summary>The generator that just engaged.</summary>
    public Generator Generator { get; }
}
