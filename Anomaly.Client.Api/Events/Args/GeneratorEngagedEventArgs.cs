using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when an <see cref="Il2CppMapGeneration.Distributors.Scp079Generator"/>
/// reaches the engaged state.
/// </summary>
[ScriptType]
public class GeneratorEngagedEventArgs : EventArgs
{
    public GeneratorEngagedEventArgs(Generator generator)
    {
        Generator = generator;
    }

    /// <summary>The generator that just engaged.</summary>
    [ScriptVisible]
    public Generator Generator { get; }
}
