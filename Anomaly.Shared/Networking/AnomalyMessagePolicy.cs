using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Anomaly.Shared.Networking
{
    public enum AnomalyMessageDirection : byte
    {
        Bidirectional = 0,
        ClientToServer = 1,
        ServerToClient = 2
    }

    /// <summary>
    /// Earliest authentication phase in which a message may be sent or received.
    /// Anomaly application protocol v4 has no custom-message traffic before the encrypted session key is
    /// installed, so every built-in message currently declares
    /// <see cref="AuthenticatedSession"/> explicitly.
    /// </summary>
    public enum AnomalyMessageAuthPhase : byte
    {
        AuthenticatedSession = 1
    }

    /// <summary>
    /// Message contract used to reject a valid hash on the wrong side before
    /// invoking any mod handler.
    /// </summary>
    public interface IDirectionalAnomalyMessage
    {
        AnomalyMessageDirection Direction { get; }
    }

    /// <summary>
    /// Per-message payload ceiling. The size is measured after the
    /// channel counter and message hash have been removed.
    /// </summary>
    public interface IBoundedAnomalyMessage
    {
        int MaxPayloadBytes { get; }
    }

    /// <summary>
    /// Declares the minimum authenticated protocol phase for a message.
    /// </summary>
    public interface IAuthPhaseAnomalyMessage
    {
        AnomalyMessageAuthPhase AuthPhase { get; }
    }

    /// <summary>
    /// Fail-closed policy base for built-in server-to-client messages.
    /// Derived messages still declare their individual payload ceiling.
    /// </summary>
    public abstract class AuthenticatedServerToClientMessage :
        IDirectionalAnomalyMessage,
        IBoundedAnomalyMessage,
        IAuthPhaseAnomalyMessage
    {
        public AnomalyMessageDirection Direction => AnomalyMessageDirection.ServerToClient;
        public AnomalyMessageAuthPhase AuthPhase => AnomalyMessageAuthPhase.AuthenticatedSession;
        public abstract int MaxPayloadBytes { get; }
    }

    /// <summary>
    /// Fail-closed policy base for built-in client-to-server messages.
    /// Derived messages still declare their individual payload ceiling.
    /// </summary>
    public abstract class AuthenticatedClientToServerMessage :
        IDirectionalAnomalyMessage,
        IBoundedAnomalyMessage,
        IAuthPhaseAnomalyMessage
    {
        public AnomalyMessageDirection Direction => AnomalyMessageDirection.ClientToServer;
        public AnomalyMessageAuthPhase AuthPhase => AnomalyMessageAuthPhase.AuthenticatedSession;
        public abstract int MaxPayloadBytes { get; }
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
            AnomalyMessageAuthPhase authPhase,
            int maxPayloadBytes,
            bool isRoundScoped)
        {
            Direction = direction;
            AuthPhase = authPhase;
            MaxPayloadBytes = maxPayloadBytes;
            IsRoundScoped = isRoundScoped;
        }

        public AnomalyMessageDirection Direction { get; }
        public AnomalyMessageAuthPhase AuthPhase { get; }
        public int MaxPayloadBytes { get; }
        public bool IsRoundScoped { get; }
    }

    public readonly struct AnomalyRateLimitResult
    {
        public AnomalyRateLimitResult(bool allowed, bool shouldLog, bool shouldDisconnect, int droppedInWindow)
        {
            Allowed = allowed;
            ShouldLog = shouldLog;
            ShouldDisconnect = shouldDisconnect;
            DroppedInWindow = droppedInWindow;
        }

        public bool Allowed { get; }
        public bool ShouldLog { get; }
        public bool ShouldDisconnect { get; }
        public int DroppedInWindow { get; }
    }

    /// <summary>
    /// Bounded per-peer token buckets applied before message deserialization. Aggregate
    /// message/byte limits prevent hash rotation from bypassing the guard; per-hash buckets
    /// contain a single hot message type. Limits preserve the paced 16 KiB file stream.
    /// </summary>
    public sealed class AnomalyInboundRateLimiter
    {
        public const int MaxTrackedHashes = 64;
        public const int AggregateMessageBurst = 512;
        public const double AggregateMessagesPerSecond = 256d;
        public const long AggregateByteBurst = 8L * 1024L * 1024L;
        public const double AggregateBytesPerSecond = 4d * 1024d * 1024d;
        public const int PerHashMessageBurst = 512;
        public const double PerHashMessagesPerSecond = 256d;

        private const int DisconnectDropThreshold = 32;
        private static readonly long DropWindowTicks = Stopwatch.Frequency * 10L;

        private readonly object _sync = new object();
        private readonly Dictionary<uint, MessageBucket> _byHash = new Dictionary<uint, MessageBucket>();
        private readonly MessageBucket _aggregate = new MessageBucket(
            AggregateMessageBurst,
            AggregateByteBurst,
            Stopwatch.GetTimestamp());

        private long _dropWindowStart;
        private int _droppedInWindow;
        private bool _disconnectRaised;

        public AnomalyRateLimitResult TryAccept(uint messageHash, int payloadBytes)
        {
            long now = Stopwatch.GetTimestamp();
            lock (_sync)
            {
                if (payloadBytes < 0 || payloadBytes > AnomalyChannel.MaxEnvelopeBytes)
                    return RegisterDropLocked(now);

                _aggregate.Refill(
                    now,
                    AggregateMessageBurst,
                    AggregateMessagesPerSecond,
                    AggregateByteBurst,
                    AggregateBytesPerSecond);

                if (!_byHash.TryGetValue(messageHash, out var perHash))
                {
                    if (_byHash.Count >= MaxTrackedHashes)
                        return RegisterDropLocked(now);
                    perHash = new MessageBucket(
                        PerHashMessageBurst,
                        AggregateByteBurst,
                        now);
                    _byHash.Add(messageHash, perHash);
                }
                else
                {
                    perHash.Refill(
                        now,
                        PerHashMessageBurst,
                        PerHashMessagesPerSecond,
                        AggregateByteBurst,
                        AggregateBytesPerSecond);
                }

                if (!_aggregate.CanConsume(payloadBytes) || !perHash.CanConsume(payloadBytes))
                    return RegisterDropLocked(now);

                _aggregate.Consume(payloadBytes);
                perHash.Consume(payloadBytes);
                return new AnomalyRateLimitResult(true, false, false, _droppedInWindow);
            }
        }

        /// <summary>
        /// Counts a syntactically invalid, wrong-direction, or replayed frame in the same
        /// bounded escalation window without consuming normal traffic tokens.
        /// </summary>
        public AnomalyRateLimitResult RegisterProtocolViolation()
        {
            lock (_sync)
                return RegisterDropLocked(Stopwatch.GetTimestamp());
        }

        private AnomalyRateLimitResult RegisterDropLocked(long now)
        {
            if (_dropWindowStart == 0 || now - _dropWindowStart > DropWindowTicks || now < _dropWindowStart)
            {
                _dropWindowStart = now;
                _droppedInWindow = 0;
                _disconnectRaised = false;
            }

            _droppedInWindow++;
            bool shouldLog = _droppedInWindow == 1 ||
                             (_droppedInWindow & (_droppedInWindow - 1)) == 0;
            bool shouldDisconnect = !_disconnectRaised &&
                                    _droppedInWindow >= DisconnectDropThreshold;
            if (shouldDisconnect)
                _disconnectRaised = true;
            return new AnomalyRateLimitResult(false, shouldLog, shouldDisconnect, _droppedInWindow);
        }

        private sealed class MessageBucket
        {
            private double _messageTokens;
            private double _byteTokens;
            private long _lastTimestamp;

            public MessageBucket(double messageTokens, double byteTokens, long timestamp)
            {
                _messageTokens = messageTokens;
                _byteTokens = byteTokens;
                _lastTimestamp = timestamp;
            }

            public void Refill(
                long now,
                double maxMessages,
                double messagesPerSecond,
                double maxBytes,
                double bytesPerSecond)
            {
                if (now <= _lastTimestamp)
                    return;
                double seconds = (double)(now - _lastTimestamp) / Stopwatch.Frequency;
                _messageTokens = Math.Min(maxMessages, _messageTokens + (seconds * messagesPerSecond));
                _byteTokens = Math.Min(maxBytes, _byteTokens + (seconds * bytesPerSecond));
                _lastTimestamp = now;
            }

            public bool CanConsume(int bytes) => _messageTokens >= 1d && _byteTokens >= bytes;

            public void Consume(int bytes)
            {
                _messageTokens -= 1d;
                _byteTokens -= bytes;
            }
        }
    }
}
