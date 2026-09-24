using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Advertises complete, versioned client feature snapshots after the server
/// establishes an authenticated round epoch.
/// </summary>
public static class ClientCapabilities
{
    private sealed class Registration : IDisposable
    {
        private readonly string _id;
        private bool _disposed;

        internal Registration(string id)
        {
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister(_id, this);
        }
    }

    private sealed class Entry
    {
        internal CapabilityDescriptor Descriptor;
        internal Registration Registration;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly TransactionalInitializer Initializer = new();
    private static bool _initialized;
    private static uint _roundEpoch;
    private static uint _txSequence;
    private static uint _lastChallengeSequence;

    public static uint CurrentRoundEpoch
    {
        get
        {
            lock (Sync)
                return _roundEpoch;
        }
    }

    public static IDisposable Register(CapabilityDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        Registration registration;
        lock (Sync)
        {
            if (Entries.ContainsKey(descriptor.Id))
                throw new InvalidOperationException($"Capability '{descriptor.Id}' is already registered.");
            if (Entries.Count >= CapabilitySnapshotMessage.MaxCapabilities)
                throw new InvalidOperationException(
                    $"At most {CapabilitySnapshotMessage.MaxCapabilities} client capabilities can be registered.");

            registration = new Registration(descriptor.Id);
            Entries.Add(descriptor.Id, new Entry
            {
                Descriptor = descriptor,
                Registration = registration
            });
        }

        SendSnapshot();
        return registration;
    }

    public static bool TryGet(string id, out CapabilityDescriptor descriptor)
    {
        lock (Sync)
        {
            if (id != null && Entries.TryGetValue(id, out Entry entry))
            {
                descriptor = entry.Descriptor;
                return true;
            }
        }

        descriptor = null;
        return false;
    }

    internal static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized) return;
            Initializer.Initialize(
                new InitializationStep(
                    () => AnomalyMessaging.MessageReceived += OnMessageReceived,
                    () => AnomalyMessaging.MessageReceived -= OnMessageReceived),
                new InitializationStep(
                    () => ClientEvents.Disconnected += OnDisconnected,
                    () => ClientEvents.Disconnected -= OnDisconnected));
            _initialized = true;
        }
    }

    internal static void ResetSession()
    {
        lock (Sync)
        {
            _roundEpoch = 0;
            _txSequence = 0;
            _lastChallengeSequence = 0;
        }

        AnomalyMessaging.ResetSession(0);
    }

    internal static void Shutdown()
    {
        lock (Sync)
        {
            if (!_initialized) return;
            _initialized = false;
            Entries.Clear();
            _roundEpoch = 0;
            _txSequence = 0;
            _lastChallengeSequence = 0;
        }

        Initializer.Reset();
        AnomalyMessaging.ResetSession(0);
    }

    private static void OnMessageReceived(IAnomalyMessage message)
    {
        if (message is not CapabilityChallengeMessage challenge)
            return;
        if (challenge.RoundEpoch == 0 || challenge.Sequence == 0)
            return;

        lock (Sync)
        {
            if (challenge.RoundEpoch == _roundEpoch &&
                challenge.Sequence <= _lastChallengeSequence)
                return;

            if (challenge.RoundEpoch != _roundEpoch)
            {
                _roundEpoch = challenge.RoundEpoch;
                _txSequence = 0;
                AnomalyMessaging.ResetSession(_roundEpoch);
                LocalPlayerMotion.ResetSequence();
            }

            _lastChallengeSequence = challenge.Sequence;
        }

        SendSnapshot();
    }

    private static void OnDisconnected(ClientDisconnectedEventArgs _)
    {
        ResetSession();
    }

    private static void Unregister(string id, Registration registration)
    {
        bool removed = false;
        lock (Sync)
        {
            if (Entries.TryGetValue(id, out Entry entry) &&
                ReferenceEquals(entry.Registration, registration))
            {
                Entries.Remove(id);
                removed = true;
            }
        }

        if (removed)
            SendSnapshot();
    }

    private static void SendSnapshot()
    {
        CapabilitySnapshotMessage message;
        lock (Sync)
        {
            if (_roundEpoch == 0)
                return;

            message = new CapabilitySnapshotMessage
            {
                RoundEpoch = _roundEpoch,
                Sequence = unchecked(++_txSequence)
            };
            if (message.Sequence == 0)
                message.Sequence = unchecked(++_txSequence);

            foreach (Entry entry in Entries.Values.OrderBy(x => x.Descriptor.Id, StringComparer.Ordinal))
                message.Capabilities.Add(entry.Descriptor);
        }

        AnomalyMessaging.Send(message);
    }
}
