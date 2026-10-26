using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileManifestMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaxFiles = 256;
        public const int MaxPathBytes = 255;
        public const uint MaxFileBytes = 64u * 1024u * 1024u;
        public const long MaxAggregateBytes = 256L * 1024L * 1024L;
        public override int MaxPayloadBytes => 96 * 1024;

        public string MessageName => "anomaly:filemanifest";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public ulong SessionId { get; set; }
        public byte[] ManifestSha256 { get; set; } = new byte[32];
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");
            if (Files == null || Files.Count > MaxFiles)
                throw new InvalidDataException($"Manifest file count exceeded {MaxFiles}.");
            if (!HashesEqual(ManifestSha256, ComputeManifestSha256(Files)))
                throw new InvalidDataException("Manifest SHA-256 did not match its canonical entries.");

            writer.Write(SessionId);
            writer.Write(ManifestSha256);
            writer.Write((ushort)Files.Count);
            long aggregateBytes = 0;
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Files)
            {
                ValidateRelativePath(file.Path);
                var pathBytes = Utf8.GetBytes(file.Path);
                if (pathBytes.Length == 0 || pathBytes.Length > MaxPathBytes)
                    throw new InvalidDataException($"Manifest path must be 1-{MaxPathBytes} UTF-8 bytes.");
                if (file.Hash == null || file.Hash.Length != 32)
                    throw new InvalidDataException("Manifest hash must be 32 bytes.");
                if (file.Size == 0 || file.Size > MaxFileBytes)
                    throw new InvalidDataException($"Manifest file size must be 1-{MaxFileBytes} bytes.");
                aggregateBytes += file.Size;
                if (aggregateBytes > MaxAggregateBytes)
                    throw new InvalidDataException($"Manifest aggregate size exceeded {MaxAggregateBytes} bytes.");
                if (!hashes.Add(BitConverter.ToString(file.Hash)))
                    throw new InvalidDataException("Manifest contained a duplicate content hash.");
                ValidateKind(file.Kind);
                ValidateKindPath(file.Kind, file.Path, file.Hash, file.Size);
                writer.Write((byte)pathBytes.Length);
                writer.Write(pathBytes);
                writer.Write(file.Hash);
                writer.Write(file.Size);
                writer.Write((byte)file.Kind);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");

            ManifestSha256 = reader.ReadBytes(32);
            if (ManifestSha256.Length != 32)
                throw new EndOfStreamException("Manifest SHA-256 was truncated.");

            int count = reader.ReadUInt16();
            if (count > MaxFiles)
                throw new InvalidDataException($"Manifest file count exceeded {MaxFiles}.");
            Files = new List<FileEntry>(count);
            long aggregateBytes = 0;
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                int pathLen = reader.ReadByte();
                if (pathLen == 0 || pathLen > MaxPathBytes)
                    throw new InvalidDataException($"Manifest path must be 1-{MaxPathBytes} UTF-8 bytes.");
                byte[] pathBytes = reader.ReadBytes(pathLen);
                if (pathBytes.Length != pathLen)
                    throw new EndOfStreamException("Manifest path was truncated.");
                string path = Utf8.GetString(pathBytes);
                ValidateRelativePath(path);
                byte[] hash = reader.ReadBytes(32);
                if (hash.Length != 32)
                    throw new EndOfStreamException("Manifest hash was truncated.");
                if (!hashes.Add(BitConverter.ToString(hash)))
                    throw new InvalidDataException("Manifest contained a duplicate content hash.");
                uint size = reader.ReadUInt32();
                if (size == 0 || size > MaxFileBytes)
                    throw new InvalidDataException($"Manifest file size must be 1-{MaxFileBytes} bytes.");
                aggregateBytes += size;
                if (aggregateBytes > MaxAggregateBytes)
                    throw new InvalidDataException($"Manifest aggregate size exceeded {MaxAggregateBytes} bytes.");
                FileContentKind kind = (FileContentKind)reader.ReadByte();
                ValidateKind(kind);
                ValidateKindPath(kind, path, hash, size);
                Files.Add(new FileEntry(path, hash, size, kind));
            }
            if (!HashesEqual(ManifestSha256, ComputeManifestSha256(Files)))
                throw new InvalidDataException("Manifest SHA-256 did not match its canonical entries.");
        }

        private static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Manifest paths must be non-empty relative paths.");
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOf(':') >= 0)
                throw new InvalidDataException("Manifest path contained invalid characters.");
            foreach (char character in path)
            {
                if (char.IsControl(character))
                    throw new InvalidDataException("Manifest path contained control characters.");
            }
            if (Path.IsPathRooted(path))
                throw new InvalidDataException("Manifest paths must be non-empty relative paths.");

            string[] segments = path.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new InvalidDataException("Manifest path contained an unsafe segment.");
            }
        }

        private static void ValidateKind(FileContentKind kind)
        {
            if (kind != FileContentKind.Bundle && kind != FileContentKind.PackIndex && kind != FileContentKind.ScriptIndex && kind != FileContentKind.LuaSource)
                throw new InvalidDataException("Manifest contained an unsupported asset kind.");
        }

        private static void ValidateKindPath(FileContentKind kind, string path, byte[] hash, uint size)
        {
            string extension = Path.GetExtension(path);
            if (kind == FileContentKind.Bundle &&
                !string.Equals(extension, ".bundle", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Remote bundles must use the .bundle format.");
            if (kind == FileContentKind.PackIndex &&
                !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pack indexes must use JSON.");
            if (kind == FileContentKind.ScriptIndex || kind == FileContentKind.LuaSource)
            {
                if (hash == null || hash.Length != 32 || size == 0 || size > ScriptPackLimits.MaxScriptBytesPerFile)
                    throw new InvalidDataException("Script transfer size or hash is invalid.");
                string suffix = kind == FileContentKind.ScriptIndex ? ".scripts.json" : ".lua";
                string canonicalName = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() + suffix;
                if (path.IndexOf('\\') >= 0 || Path.GetFileName(path) != canonicalName)
                    throw new InvalidDataException("Script transfer paths require their canonical content-hash filename.");
            }
        }

        public static byte[] ComputeManifestSha256(IEnumerable<FileEntry> files)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var sha256 = SHA256.Create())
            {
                writer.Write(Encoding.ASCII.GetBytes("ANOMALY-FILE-MANIFEST-V3"));
                var ordered = files.OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ThenBy(file => BitConverter.ToString(file.Hash ?? Array.Empty<byte>()), StringComparer.Ordinal)
                    .ToList();
                writer.Write(ordered.Count);
                foreach (FileEntry file in ordered)
                {
                    ValidateRelativePath(file.Path);
                    ValidateKind(file.Kind);
                    ValidateKindPath(file.Kind, file.Path, file.Hash, file.Size);
                    byte[] path = Utf8.GetBytes(file.Path);
                    if (path.Length == 0 || path.Length > MaxPathBytes ||
                        file.Hash == null || file.Hash.Length != 32 ||
                        file.Size == 0 || file.Size > MaxFileBytes)
                        throw new InvalidDataException("Manifest entry was invalid for canonicalization.");
                    writer.Write((byte)path.Length);
                    writer.Write(path);
                    writer.Write(file.Hash);
                    writer.Write(file.Size);
                    writer.Write((byte)file.Kind);
                }
                writer.Flush();
                stream.Position = 0;
                return sha256.ComputeHash(stream);
            }
        }

        private static bool HashesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != 32 || right.Length != 32)
                return false;
            int difference = 0;
            for (int index = 0; index < 32; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        public struct FileEntry
        {
            public string Path;
            public byte[] Hash;
            public uint Size;
            public FileContentKind Kind;

            public FileEntry(string path, byte[] hash, uint size, FileContentKind kind)
            {
                Path = path;
                Hash = hash;
                Size = size;
                Kind = kind;
            }
        }
    }
}
