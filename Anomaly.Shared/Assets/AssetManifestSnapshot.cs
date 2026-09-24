using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Assets
{
    /// <summary>
    /// Immutable hash-to-kind membership for one authenticated file-manifest
    /// snapshot. Callers decide when that snapshot becomes approved.
    /// </summary>
    internal sealed class AssetManifestSnapshot
    {
        private readonly Dictionary<string, FileContentKind> _assets =
            new Dictionary<string, FileContentKind>(StringComparer.Ordinal);

        public AssetManifestSnapshot(IEnumerable<FileManifestMessage.FileEntry> entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            foreach (FileManifestMessage.FileEntry entry in entries)
            {
                if (entry.Hash == null || entry.Hash.Length != 32)
                    throw new InvalidDataException("Manifest authorization requires 32-byte SHA-256 hashes.");
                if (entry.Kind != FileContentKind.Bundle && entry.Kind != FileContentKind.PackIndex)
                    throw new InvalidDataException("Manifest authorization contained an unsupported asset kind.");
                string hash = ToHex(entry.Hash);
                if (_assets.ContainsKey(hash))
                    throw new InvalidDataException("Manifest authorization contained a duplicate content hash.");
                _assets.Add(hash, entry.Kind);
            }
        }

        public int Count => _assets.Count;

        public bool Contains(byte[] hash, FileContentKind expectedKind)
        {
            if (hash == null || hash.Length != 32)
                return false;
            return _assets.TryGetValue(ToHex(hash), out FileContentKind actualKind) &&
                   actualKind == expectedKind;
        }

        private static string ToHex(byte[] hash) =>
            BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
