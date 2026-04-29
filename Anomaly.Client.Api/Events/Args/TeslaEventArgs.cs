using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="Tesla"/> reference for tesla lifecycle / burst events.</summary>
public class TeslaEventArgs : EventArgs
{
    public TeslaEventArgs(Tesla tesla) { Tesla = tesla; }
    public Tesla Tesla { get; }
}
