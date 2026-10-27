#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Scripting;
using Anomaly.Shared.Content.Packs;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Content;

/// <summary>A captured descriptor and its link-free local pack directory.</summary>
internal sealed class LocalContentPack
{
    internal CapturedContentPack Contents { get; }
    internal string Directory { get; }

    internal LocalContentPack(string directory, CapturedContentPack contents)
    {
        Directory = Path.GetFullPath(directory);
        Contents = contents;
    }

    internal string Resolve(string relative)
    {
        if (!ContentPackCodec.IsRelativePath(relative))
            throw new InvalidDataException("Content pack path is not canonical.");
        string path = Path.GetFullPath(Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(Directory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content pack path escaped its directory.");
        ContentPackDirectoryReader.RejectLinks(path);
        return path;
    }
}

/// <summary>Captures only explicitly declared content, without following filesystem links.</summary>
internal static class ContentPackDirectoryReader
{
    internal static IReadOnlyList<ScriptSource> Read(string root)
    {
        var result = new List<ScriptSource>();
        foreach (var pack in ReadPacks(root))
        {
            var contents = pack.Contents;
            if (contents.ScriptIndex == null) continue;
            var index = contents.ScriptIndex;
            var source = index.Sources.Single();
            result.Add(new ScriptSource(index.PackId, source.Address, contents.SourceText,
                source.SourceSha256, source.SourceBytes,
                index.Messages.Select(schema => schema.Clone()).ToArray(),
                ScriptSourceValidator.HashHex(contents.ScriptIndexBytes)));
        }
        return result.ToArray();
    }

    internal static IReadOnlyList<LocalContentPack> ReadPacks(string root)
    {
        root = Path.GetFullPath(root);
        if (!System.IO.Directory.Exists(root)) return Array.Empty<LocalContentPack>();
        RejectLinks(root);
        var descriptors = new List<(string Directory, ContentPackDescriptor Descriptor)>();
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        int scanned = 0, scriptCount = 0;
        foreach (string owner in Children(root, skipSpool: true))
        foreach (string directory in Children(owner, skipSpool: false))
        {
            if (descriptors.Count >= ContentPackCodec.MaxPacks)
                throw new InvalidDataException("Local content pack count exceeded.");
            string descriptorPath = Path.Combine(directory, "pack.json");
            if (!File.Exists(descriptorPath)) throw new InvalidDataException("Local content pack is missing pack.json.");
            var descriptor = ContentPackCodec.Parse(ReadBounded(descriptorPath, ContentPackCodec.MaxDescriptorBytes), remote: false);
            string identity = Path.GetFileName(owner) + ":" + Path.GetFileName(directory);
            if (identity != descriptor.PackId)
                throw new InvalidDataException("Content pack directory identity disagrees with its descriptor.");
            if (descriptor.Scripts != null)
            {
                if (++scriptCount > ScriptPackLimits.MaxScripts)
                    throw new InvalidDataException("Local script count exceeded.");
                if (!componentIds.Add(descriptor.Scripts.PackId)) throw new InvalidDataException("Duplicate content component identity.");
            }
            if (descriptor.Assets != null && !componentIds.Add(descriptor.Assets.PackId))
                throw new InvalidDataException("Duplicate content component identity.");
            descriptors.Add((directory, descriptor));
        }

        int sourceBytes = 0;
        var result = new List<LocalContentPack>(descriptors.Count);
        foreach (var entry in descriptors)
        {
            var local = new LocalContentPack(entry.Directory, null);
            var contents = ContentPackCodec.Capture(entry.Descriptor, (relative, maximum) =>
            {
                bool source = relative == entry.Descriptor.Scripts?.EntryPoint;
                if (source) maximum = Math.Min(maximum, ScriptPackLimits.MaxScriptBytes - sourceBytes);
                byte[] bytes = ReadBounded(local.Resolve(relative), maximum);
                if (source) sourceBytes = checked(sourceBytes + bytes.Length);
                return bytes;
            }, remote: false);
            result.Add(new LocalContentPack(entry.Directory, contents));
        }
        return result.ToArray();

        IEnumerable<string> Children(string directory, bool skipSpool)
        {
            RejectLinks(directory);
            var children = new List<string>();
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                if (++scanned > ContentPackCodec.MaxScannedEntries)
                    throw new InvalidDataException("Local content directory entry budget exceeded.");
                if (skipSpool && Path.GetFileName(path) == ".anomaly-spool") continue;
                RejectLinks(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) children.Add(path);
            }
            children.Sort(StringComparer.Ordinal);
            return children;
        }
    }

    internal static byte[] ReadBounded(string path, int maximum)
    {
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        RejectLinks(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new InvalidDataException("Content file exceeded its byte limit.");
        byte[] bytes = new byte[checked((int)input.Length)];
        int position = 0;
        while (position < bytes.Length)
        {
            int read = input.Read(bytes, position, bytes.Length - position);
            if (read == 0) throw new EndOfStreamException();
            position += read;
        }
        if (input.ReadByte() != -1) throw new InvalidDataException("Content file changed while its snapshot was read.");
        return bytes;
    }

    internal static void RejectLinks(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Content paths cannot contain reparse points.");
    }
}
