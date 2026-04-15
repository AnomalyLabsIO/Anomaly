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

            Factories[hash] = () => factory();
            HashToName[hash] = name;
            NameToHash[name] = hash;

            Log($"AnomalyMessageRegistry: Registered '{name}' (hash=0x{hash:X8})");
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
