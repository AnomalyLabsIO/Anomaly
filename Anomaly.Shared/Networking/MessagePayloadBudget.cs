namespace Anomaly.Shared.Networking;

/// <summary>
/// Current outgoing capacity for the bytes written by <see cref="IAnomalyMessage.Serialize"/>.
/// Envelope, authentication and transport framing are already reserved. Capacity is a
/// snapshot; delivery still rechecks the recipient and transport.
/// </summary>
public readonly struct MessagePayloadBudget
{
    internal MessagePayloadBudget(int maxPayloadBytes, int maxUnfragmentedPayloadBytes)
    {
        MaxPayloadBytes = maxPayloadBytes;
        MaxUnfragmentedPayloadBytes = maxUnfragmentedPayloadBytes;
    }

    /// <summary>Maximum serialized payload, including transport fragmentation where supported.</summary>
    public int MaxPayloadBytes { get; }

    /// <summary>Maximum serialized payload that fits one transport packet.</summary>
    public int MaxUnfragmentedPayloadBytes { get; }
}
