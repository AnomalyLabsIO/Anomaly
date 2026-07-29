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

        public static void Register<T>(string name, Func<T> factory) where T : IAnomalyMessage
        {
            uint hash = ComputeHash(name);

            if (HashToName.TryGetValue(hash, out var existing))
            {
                if (existing == name)
                {
                    Warn($"AnomalyMessageRegistry: '{name}' is already registered, skipping.");
                    return;
                }
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: FNV-1a hash collision between '{existing}' and '{name}' (hash=0x{hash:X8}). Rename one of them.");
            }

            // Sample one instance so the receive path can read TransportChannel from a
            // static lookup rather than allocating a fresh message per packet (which an
            // attacker could exploit via replay-before-validation GC pressure).
            var sample = factory();
            if (sample == null)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' factory returned null.");

            var directional = sample as IDirectionalAnomalyMessage;
            var bounded = sample as IBoundedAnomalyMessage;
            int maxPayloadBytes = bounded?.MaxPayloadBytes ?? AnomalyChannel.MaxEnvelopeBytes;
            if (maxPayloadBytes <= 0 || maxPayloadBytes > AnomalyChannel.MaxEnvelopeBytes)
                throw new InvalidOperationException(
                    $"AnomalyMessageRegistry: '{name}' declared invalid payload ceiling {maxPayloadBytes}.");

            var policy = new AnomalyMessagePolicy(
                directional?.Direction ?? AnomalyMessageDirection.Bidirectional,
                maxPayloadBytes,
                sample is IRoundScopedAnomalyMessage);

            // Commit all lookup tables only after the factory and policy have
            // passed validation, so a failed registration cannot leave a partial
            // hash entry behind.
            Factories[hash] = () => factory();
            HashToName[hash] = name;
            NameToHash[name] = hash;
            HashToChannel[hash] = sample.TransportChannel;
            HashToPolicy[hash] = policy;

            Log($"AnomalyMessageRegistry: Registered '{name}' (hash=0x{hash:X8}, channel={sample.TransportChannel})");
        }

        public static bool TryCreate(uint hash, out IAnomalyMessage message)
        {
            if (Factories.TryGetValue(hash, out var factory))
            {
                message = factory();
                return true;
            }
            message = null;
            return false;
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
            Register("anomaly:blendshapes", () => new EmotionBlendShapesMessage());
            Register("anomaly:bonetransforms", () => new BoneTransformsMessage());
            Register("anomaly:silentrolechange", () => new SilentRoleChangeMessage());
            Register("anomaly:round:state", () => new RoundStateMessage());
            Register("anomaly:filemanifest", () => new FileManifestMessage());
            Register("anomaly:filerequest", () => new FileRequestMessage());
            Register("anomaly:filechunk", () => new FileChunkMessage());
            Register("anomaly:filecomplete", () => new FileTransferCompleteMessage());
            Register("anomaly:fileack", () => new FileAckMessage());

            Register("anomaly:asset:spawn", () => new AssetSpawnMessage());
            Register("anomaly:asset:despawn", () => new AssetDespawnMessage());
            Register("anomaly:asset:transform", () => new AssetSetTransformMessage());
            Register("anomaly:asset:texture", () => new AssetSetTextureMessage());
            Register("anomaly:asset:active", () => new AssetSetActiveMessage());
            Register("anomaly:asset:reload", () => new AssetReloadMessage());
            Register("anomaly:asset:override-texture", () => new AssetOverrideTextureMessage());
            Register("anomaly:asset:override-clear", () => new AssetClearOverrideMessage());
            Register("anomaly:asset:override-clearall", () => new AssetClearAllOverridesMessage());

            Register("anomaly:announcer:vocabulary", () => new AnnouncerVocabularyMessage());

            Register("anomaly:player:setfavoritecolor", () => new SetFavoriteColorMessage());
            Register("anomaly:player:colorchanged", () => new PlayerColorChangedMessage());

            Register("anomaly:content:catalog", () => new ContentCatalogMessage());
            Register("anomaly:content:catalogack", () => new ContentCatalogAckMessage());

            Register("anomaly:capability:challenge", () => new CapabilityChallengeMessage());
            Register("anomaly:capability:snapshot", () => new CapabilitySnapshotMessage());
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
