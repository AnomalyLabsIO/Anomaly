using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Content.Packs;
using Anomaly.Shared.Scripting;

namespace Anomaly.Server.FileTransfer;

/// <summary>Exact-depth discovery and bounded reads of owner-authored packs.</summary>
internal static class ContentPackFiles
{
    internal sealed class Pack
    {
        internal readonly CapturedContentPack Contents;
        internal readonly string Directory;
        internal readonly byte[] Bundle;
        internal Pack(CapturedContentPack contents, string directory, byte[] bundle = null)
        { Contents = contents; Directory = directory; Bundle = bundle; }
        internal Stream OpenBundle() => Bundle != null ? new MemoryStream(Bundle, false) :
            new FileStream(Resolve(Directory, Contents.BundlePath), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    internal static IEnumerable<Pack> Discover(string directory, int sourceBudget = ScriptPackLimits.MaxScriptBytes,
        int sourceLimit = ScriptPackLimits.MaxScripts, int schemaLimit = ScriptMessageLimits.MaxSchemasPerCatalog)
    {
        if (string.IsNullOrWhiteSpace(directory)) yield break;
        string root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        CheckAncestors(root);
        int visited = 0, sourceBytes = 0, sourceCount = 0, schemas = 0, packs = 0;
        foreach (string ns in Directory.EnumerateFileSystemEntries(root))
        {
            if (++visited > ContentPackCodec.MaxScannedEntries) throw new InvalidDataException("Content pack traversal budget exceeded.");
            if (Path.GetFileName(ns) == ".anomaly-spool") continue;
            if (!Directory.Exists(ns)) continue;
            CheckAncestors(ns);
            foreach (string pack in Directory.EnumerateFileSystemEntries(ns))
            {
                if (++visited > ContentPackCodec.MaxScannedEntries) throw new InvalidDataException("Content pack traversal budget exceeded.");
                if (!Directory.Exists(pack)) continue;
                CheckAncestors(pack);
                string descriptorPath = Resolve(pack, "pack.json");
                if (!File.Exists(descriptorPath)) throw new InvalidDataException("Every content pack directory must contain pack.json.");
                var descriptor = ContentPackCodec.Parse(ReadBounded(descriptorPath, ContentPackCodec.MaxDescriptorBytes));
                if (descriptor.PackId != Path.GetFileName(ns) + ":" + Path.GetFileName(pack))
                    throw new InvalidDataException("Pack directory identity must exactly equal its descriptor PackId.");
                if (++packs > ContentPackCodec.MaxPacks) throw new InvalidDataException("Too many owner content packs.");
                if (descriptor.Scripts != null && ++sourceCount > sourceLimit)
                    throw new InvalidDataException("Too many owner script sources.");
                var captured = ContentPackCodec.Capture(descriptor, (path, maximum) =>
                    ReadBounded(Resolve(pack, path), descriptor.Scripts != null && path == descriptor.Scripts.EntryPoint
                        ? Math.Min(maximum, sourceBudget - sourceBytes) : maximum));
                if (captured.SourceBytes != null) sourceBytes = checked(sourceBytes + captured.SourceBytes.Length);
                if (captured.ScriptIndex != null) schemas = checked(schemas + captured.ScriptIndex.Messages.Count);
                if (schemas > schemaLimit) throw new InvalidDataException("Too many owner message schemas.");
                yield return new Pack(captured, pack);
            }
        }
    }

    internal static string Resolve(string directory, string relative)
    {
        if (!ContentPackCodec.IsRelativePath(relative)) throw new InvalidDataException("Invalid content pack path.");
        string root = Path.GetFullPath(directory);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content pack path escapes its root.");
        CheckAncestors(path);
        return path;
    }

    internal static void CheckAncestors(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Content pack paths cannot contain reparse points.");
    }

    internal static byte[] ReadBounded(string path, int maximum)
    {
        CheckAncestors(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            return ReadBounded(stream, maximum);
    }

    internal static byte[] ReadBounded(Stream stream, int maximum)
    {
        if (stream == null) throw new InvalidDataException("A declared content pack resource does not exist.");
        if (!stream.CanSeek) throw new InvalidDataException("Content pack resources must expose a bounded length.");
        long length = stream.Length;
        if (length < 0 || length > maximum) throw new InvalidDataException("Content pack file exceeds its byte budget.");
        var bytes = new byte[(int)length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException("Incomplete content pack file.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Content pack file grew while reading.");
        return bytes;
    }
}
