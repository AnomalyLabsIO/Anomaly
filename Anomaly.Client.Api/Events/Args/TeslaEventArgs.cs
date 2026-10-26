using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="Tesla"/> reference for tesla lifecycle / burst events.</summary>
[ScriptType]
public class TeslaEventArgs : EventArgs
{
    public TeslaEventArgs(Tesla tesla) { Tesla = tesla; }
    [ScriptVisible]
    public Tesla Tesla { get; }
}
