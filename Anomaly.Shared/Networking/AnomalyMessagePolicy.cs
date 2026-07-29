namespace Anomaly.Shared.Networking
{
    public enum AnomalyMessageDirection : byte
    {
        Bidirectional = 0,
        ClientToServer = 1,
        ServerToClient = 2
    }

    /// <summary>
    /// Optional message contract used to reject a valid hash on the wrong side
    /// before invoking any mod handler.
    /// </summary>
    public interface IDirectionalAnomalyMessage
    {
        AnomalyMessageDirection Direction { get; }
    }

    /// <summary>
    /// Optional per-message payload ceiling. The size is measured after the
    /// channel counter and message hash have been removed.
    /// </summary>
    public interface IBoundedAnomalyMessage
    {
        int MaxPayloadBytes { get; }
    }

    /// <summary>
    /// Optional semantic replay guard in addition to the transport counter.
    /// Sequences are strictly increasing per message type within a server-issued
    /// round epoch.
    /// </summary>
    public interface IRoundScopedAnomalyMessage
    {
        uint RoundEpoch { get; }
        uint Sequence { get; }
    }

    public readonly struct AnomalyMessagePolicy
    {
        public AnomalyMessagePolicy(
            AnomalyMessageDirection direction,
            int maxPayloadBytes,
            bool isRoundScoped)
        {
            Direction = direction;
            MaxPayloadBytes = maxPayloadBytes;
            IsRoundScoped = isRoundScoped;
        }

        public AnomalyMessageDirection Direction { get; }
        public int MaxPayloadBytes { get; }
        public bool IsRoundScoped { get; }
    }
}
