using System;
using System.Collections.Generic;
using System.Text;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Networking
{
    /// <summary>
    /// Shared registry for Anomaly network messages. Maintains FNV-1a hash ↔ factory
    /// mappings so client and server can agree on a compact wire identifier for each
    /// message type.
    /// </summary>
    /// <remarks>
    /// Logging is delegated via the <see cref="Log"/> and <see cref="Warn"/> hooks so
    /// the core stays free of MelonLoader / LabAPI references. Each side should wire
    /// these up once during initialization before calling <see cref="RegisterDefaults"/>.
    /// </remarks>
    public static class AnomalyMessageRegistry
    {
        /// <summary>
        /// Informational log sink. Defaults to a no-op; set during side-specific bootstrap.
        /// </summary>
        public static Action<string> Log = _ => { };

        /// <summary>
        /// Warning log sink. Defaults to a no-op; set during side-specific bootstrap.
        /// </summary>
        public static Action<string> Warn = _ => { };

        private static readonly Dictionary<uint, Func<IAnomalyMessage>> Factories = new Dictionary<uint, Func<IAnomalyMessage>>();
        private static readonly Dictionary<uint, string> HashToName = new Dictionary<uint, string>();
        private static readonly Dictionary<string, uint> NameToHash = new Dictionary<string, uint>();
        private static readonly Dictionary<uint, MessageChannel> HashToChannel = new Dictionary<uint, MessageChannel>();
        private static readonly Dictionary<uint, AnomalyMessagePolicy> HashToPolicy = new Dictionary<uint, AnomalyMessagePolicy>();
        private static readonly Dictionary<uint, Type> HashToType = new Dictionary<uint, Type>();

        public static void Register<T>(string name, Func<T> factory) where T : IAnomalyMessage
        {
            RegisterCore(name, factory, builtIn: false);
        }

        private static void RegisterBuiltIn<T>(string name, Func<T> factory) where T : IAnomalyMessage
        {
            RegisterCore(name, factory, builtIn: true);
        }

        private static void RegisterCore<T>(string name, Func<T> factory, bool builtIn) where T : IAnomalyMessage
        {
            ValidateName(name, builtIn);
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // Sample one instance so the receive path can read TransportChannel from a
            // static lookup rather than allocating a fresh message per packet (which an
            // attacker could exploit via replay-before-validation GC pressure).
            var sample = factory();
            if (sample == null)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' factory returned null.");
            if (!string.Equals(sample.MessageName, name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: factory message name '{sample.MessageName}' did not match registration '{name}'.");

            if (!Enum.IsDefined(typeof(MessageChannel), sample.TransportChannel))
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' declared unsupported transport channel {sample.TransportChannel}.");

            var directional = sample as IDirectionalAnomalyMessage;
            var bounded = sample as IBoundedAnomalyMessage;
            var phased = sample as IAuthPhaseAnomalyMessage;
            if (directional == null || bounded == null || phased == null)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' must explicitly declare direction, authentication phase, and maximum payload size.");

            if (directional.Direction != AnomalyMessageDirection.ClientToServer &&
                directional.Direction != AnomalyMessageDirection.ServerToClient)
            {
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' must declare exactly one wire direction; " +
                    $"'{directional.Direction}' is not permitted.");
            }

            int maxPayloadBytes = bounded.MaxPayloadBytes;
            if (maxPayloadBytes < 0 || maxPayloadBytes > AnomalyChannel.MaxEnvelopeBytes)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' declared invalid payload ceiling {maxPayloadBytes}.");
            if (phased.AuthPhase != AnomalyMessageAuthPhase.AuthenticatedSession)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' declared unsupported authentication phase {phased.AuthPhase}.");

            var policy = new AnomalyMessagePolicy(
                directional.Direction,
                phased.AuthPhase,
                maxPayloadBytes,
                sample is IRoundScopedAnomalyMessage);

            uint hash = ComputeHash(name);
            if (HashToName.TryGetValue(hash, out var existing))
            {
                if (!string.Equals(existing, name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"AnomalyMessageRegistry: FNV-1a hash collision between '{existing}' and '{name}' (hash=0x{hash:X8}). Rename one of them.");
                }

                bool sameContract = HashToType.TryGetValue(hash, out Type existingType) &&
                                    existingType == sample.GetType() &&
                                    HashToChannel.TryGetValue(hash, out MessageChannel existingChannel) &&
                                    existingChannel == sample.TransportChannel &&
                                    HashToPolicy.TryGetValue(hash, out AnomalyMessagePolicy existingPolicy) &&
                                    existingPolicy.Direction == policy.Direction &&
                                    existingPolicy.AuthPhase == policy.AuthPhase &&
                                    existingPolicy.MaxPayloadBytes == policy.MaxPayloadBytes &&
                                    existingPolicy.IsRoundScoped == policy.IsRoundScoped;
                if (!sameContract)
                {
                    throw new InvalidOperationException(
                        $"AnomalyMessageRegistry: '{name}' is already registered with a different message contract.");
                }

                InvokeLogSafely(Warn, $"AnomalyMessageRegistry: '{name}' is already registered with the same contract, skipping.");
                return;
            }

            // Commit all lookup tables only after the factory and policy have
            // passed validation, so a failed registration cannot leave a partial
            // hash entry behind.
            Factories[hash] = () => factory();
            HashToName[hash] = name;
            NameToHash[name] = hash;
            HashToChannel[hash] = sample.TransportChannel;
            HashToPolicy[hash] = policy;
            HashToType[hash] = sample.GetType();

            InvokeLogSafely(
                Log,
                $"AnomalyMessageRegistry: Registered '{name}' (hash=0x{hash:X8}, channel={sample.TransportChannel}, direction={policy.Direction}, phase={policy.AuthPhase}, max={policy.MaxPayloadBytes})");
        }

        private static void InvokeLogSafely(Action<string> sink, string message)
        {
            try { sink?.Invoke(message); }
            catch { /* diagnostics must not invalidate a committed registry entry */ }
        }

        private static void ValidateName(string name, bool builtIn)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                throw new ArgumentException("Message name must be a non-empty namespaced identifier of at most 128 characters.", nameof(name));

            string[] segments = name.Split(':');
            if (segments.Length < 2 || Array.Exists(segments, segment => segment.Length == 0))
                throw new ArgumentException("Message name must contain non-empty colon-separated namespace segments.", nameof(name));
            foreach (char character in name)
            {
                bool allowed = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or ':';
                if (!allowed)
                    throw new ArgumentException("Message names may contain only lowercase ASCII letters, digits, '.', '-', '_', and ':'.", nameof(name));
            }

            bool reserved = name.StartsWith("anomaly:", StringComparison.Ordinal);
            if (builtIn != reserved)
            {
                throw new InvalidOperationException(builtIn
                    ? "Built-in message names must use the reserved 'anomaly:' namespace."
                    : "The 'anomaly:' message namespace is reserved for built-in protocol messages.");
            }
        }

        public static bool TryCreate(uint hash, out IAnomalyMessage message)
        {
            message = null;
            if (Factories.TryGetValue(hash, out var factory))
            {
                try
                {
                    var candidate = factory();
                    if (MatchesRegisteredContract(hash, candidate))
                    {
                        message = candidate;
                        return true;
                    }
                }
                catch { /* A broken mod factory cannot escape the receive boundary. */ }
            }
            return false;
        }

        internal static bool IsRegisteredType<T>(uint hash) where T : IAnomalyMessage =>
            HashToType.TryGetValue(hash, out Type type) && type == typeof(T);

        // Transport code uses the immutable registered contract, never a caller's
        // mutable channel or a different implementation borrowing a registered name.
        internal static bool TryGetSendContract(
            IAnomalyMessage message,
            AnomalyMessageDirection direction,
            out uint hash,
            out MessageChannel channel,
            out AnomalyMessagePolicy policy)
        {
            hash = 0;
            channel = default;
            policy = default;
            try
            {
                if (message == null || message.MessageName == null ||
                    !NameToHash.TryGetValue(message.MessageName, out uint registeredHash) ||
                    !MatchesRegisteredContract(registeredHash, message) ||
                    HashToPolicy[registeredHash].Direction != direction)
                    return false;

                hash = registeredHash;
                channel = HashToChannel[hash];
                policy = HashToPolicy[hash];
                return true;
            }
            catch { return false; }
        }

        private static bool MatchesRegisteredContract(uint hash, IAnomalyMessage message)
        {
            return message != null &&
                   HashToType.TryGetValue(hash, out Type type) && message.GetType() == type &&
                   HashToName.TryGetValue(hash, out string name) &&
                   string.Equals(name, message.MessageName, StringComparison.Ordinal) &&
                   HashToChannel.TryGetValue(hash, out MessageChannel channel) &&
                   message.TransportChannel == channel &&
                   HashToPolicy.TryGetValue(hash, out AnomalyMessagePolicy policy) &&
                   message.Direction == policy.Direction &&
                   message.AuthPhase == policy.AuthPhase &&
                   message.MaxPayloadBytes == policy.MaxPayloadBytes &&
                   (message is IRoundScopedAnomalyMessage) == policy.IsRoundScoped;
        }

        public static bool TryGetChannel(uint hash, out MessageChannel channel)
        {
            return HashToChannel.TryGetValue(hash, out channel);
        }

        public static bool TryGetPolicy(uint hash, out AnomalyMessagePolicy policy)
        {
            return HashToPolicy.TryGetValue(hash, out policy);
        }

        public static uint GetHash(string name)
        {
            if (NameToHash.TryGetValue(name, out var hash))
                return hash;
            throw new KeyNotFoundException($"AnomalyMessageRegistry: '{name}' is not registered.");
        }

        /// <summary>
        /// Registers the built-in Anomaly messages that live in <see cref="Anomaly.Shared"/>.
        /// Safe to call multiple times — duplicates are skipped with a warning.
        /// </summary>
        public static void RegisterDefaults()
        {
            RegisterBuiltIn("anomaly:silentrolechange", () => new SilentRoleChangeMessage());
            RegisterBuiltIn("anomaly:round:state", () => new RoundStateMessage());
            RegisterBuiltIn("anomaly:facility:snapshot", () => new FacilitySnapshotMessage());
            RegisterBuiltIn("anomaly:filemanifest", () => new FileManifestMessage());
            RegisterBuiltIn("anomaly:filerequest", () => new FileRequestMessage());
            RegisterBuiltIn("anomaly:filechunk", () => new FileChunkMessage());
            RegisterBuiltIn("anomaly:filecomplete", () => new FileTransferCompleteMessage());
            RegisterBuiltIn("anomaly:fileack", () => new FileAckMessage());

            RegisterBuiltIn("anomaly:asset:override-texture", () => new AssetOverrideTextureMessage());
            RegisterBuiltIn("anomaly:asset:override-clear", () => new AssetClearOverrideMessage());
            RegisterBuiltIn("anomaly:asset:override-clearall", () => new AssetClearAllOverridesMessage());

            RegisterBuiltIn("anomaly:announcer:vocabulary", () => new AnnouncerVocabularyMessage());
            RegisterBuiltIn("anomaly:visual:snapshot", () => new DecorativeVisualSnapshotMessage());
            RegisterBuiltIn("anomaly:visual:update", () => new DecorativeVisualUpdateMessage());
            RegisterBuiltIn("anomaly:pose:control", () => new PlayerPoseControlMessage());
            RegisterBuiltIn("anomaly:pose:sample", () => new PlayerPoseSampleMessage());
            RegisterBuiltIn("anomaly:combat-pose:control", () => new PlayerCombatPoseControlMessage());
            RegisterBuiltIn("anomaly:combat-pose:sample", () => new PlayerCombatPoseSampleMessage());

            RegisterBuiltIn("anomaly:content:catalog", () => new ContentCatalogMessage());
            RegisterBuiltIn("anomaly:content:bootstrapready", () => new ContentBootstrapReadyMessage());
            RegisterBuiltIn("anomaly:content:decision", () => new ContentCatalogDecisionMessage());
            RegisterBuiltIn("anomaly:content:catalogack", () => new ContentCatalogAckMessage());
            RegisterBuiltIn("anomaly:script:client", () => new ScriptToClientMessage());
            RegisterBuiltIn("anomaly:script:server", () => new ScriptToServerMessage());
            RegisterBuiltIn("anomaly:identity:enrollment", () => new EnrollmentIdentityMessage());

            RegisterBuiltIn("anomaly:capability:challenge", () => new CapabilityChallengeMessage());
            RegisterBuiltIn("anomaly:capability:snapshot", () => new CapabilitySnapshotMessage());
        }

        /// <summary>
        /// FNV-1a 32-bit hash of the UTF-8 bytes of the input string.
        /// Must produce identical results on client and server.
        /// </summary>
        public static uint ComputeHash(string input)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(input))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
