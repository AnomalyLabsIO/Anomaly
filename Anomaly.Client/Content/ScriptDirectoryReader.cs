#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Scripting;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Content;

/// <summary>Captures a bounded local directory without following links.</summary>
internal static class ScriptDirectoryReader
{
    internal static IReadOnlyList<ScriptSource> Read(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<ScriptSource>();
        var result = new List<ScriptSource>();
        var packIds = new HashSet<string>(StringComparer.Ordinal);
        int entries = 0, scannedEntries = 0, sourceBytes = 0, schemaCount = 0;
        foreach (string owner in Children(root, directories: true))
        foreach (string pack in Children(owner, directories: true))
        {
            string packId = (Path.GetFileName(owner) + ":" + Path.GetFileName(pack)).ToLowerInvariant();
            if (!AssetPackIndexCodec.IsPackId(packId) || !packIds.Add(packId))
                throw new InvalidDataException("Invalid or duplicate local script pack directory identity.");
            var files = new List<(ScriptPackSource Entry, string Text)>();
            var index = new ScriptPackIndex { PackId = packId };
            string descriptor = Path.Combine(pack, "messages.json");
            if (File.Exists(descriptor))
            {
                index.Messages = ScriptPackIndexCodec.ParseMessageSchemas(ReadBounded(descriptor, ScriptPackLimits.MaxIndexBytes));
                schemaCount = checked(schemaCount + index.Messages.Count);
                if (schemaCount > ScriptMessageLimits.MaxSchemasPerCatalog) throw new InvalidDataException("Local message schema budget exceeded.");
            }
            Visit(pack, 0);
            if (files.Count == 0) continue;
            byte[] encoded = ScriptPackIndexCodec.Encode(index);
            string indexHash = ScriptSourceValidator.HashHex(encoded);
            var schemas = index.Messages.Select(schema => schema.Clone()).ToArray();
            foreach (var file in files.OrderBy(file => file.Entry.Address, StringComparer.Ordinal))
                result.Add(new ScriptSource(packId, file.Entry.Address, file.Text, file.Entry.SourceSha256,
                    file.Entry.SourceBytes, schemas, indexHash));

            void Visit(string directory, int depth)
            {
                if (depth > 8) throw new InvalidDataException("Local script directory depth exceeded.");
                foreach (string path in Children(directory, directories: null))
                {
                    if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) { Visit(path, depth + 1); continue; }
                    if (!path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
                    if (++entries > ScriptPackLimits.MaxScripts) throw new InvalidDataException("Local script count exceeded.");
                    // Reject the file before allocating its bytes when the
                    // remaining connection budget is smaller than the file cap.
                    byte[] bytes = ReadBounded(path, Math.Min(ScriptPackLimits.MaxScriptBytesPerFile,
                        ScriptPackLimits.MaxScriptBytes - sourceBytes));
                    sourceBytes = checked(sourceBytes + bytes.Length);
                    if (sourceBytes > ScriptPackLimits.MaxScriptBytes) throw new InvalidDataException("Local script source budget exceeded.");
                    string address = ("mods/" + Path.GetFileName(owner) + "/" + Path.GetFileName(pack) + "/" +
                        Path.GetRelativePath(pack, path).Replace('\\', '/')).ToLowerInvariant();
                    var source = new ScriptPackSource { Address = address, SourceBytes = bytes.Length, SourceSha256 = ScriptSourceValidator.HashHex(bytes) };
                    index.Sources.Add(source);
                    files.Add((source, ScriptSourceValidator.Validate(bytes)));
                }
            }
        }
        return result.ToArray();

        IEnumerable<string> Children(string directory, bool? directories)
        {
            RejectLinks(directory);
            int scanned = 0;
            foreach (string path in Directory.EnumerateFileSystemEntries(directory).Take(257).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (++scanned > 256 || ++scannedEntries > 1024) throw new InvalidDataException("Local script directory entry budget exceeded.");
                RejectLinks(path);
                bool isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                if (directories == null || directories == isDirectory) yield return path;
            }
        }
    }

    internal static byte[] ReadBounded(string path, int maximum)
    {
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        RejectLinks(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new InvalidDataException("Script file exceeded its byte limit.");
        byte[] bytes = new byte[checked((int)input.Length)];
        int position = 0;
        while (position < bytes.Length)
        {
            int read = input.Read(bytes, position, bytes.Length - position);
            if (read == 0) throw new EndOfStreamException();
            position += read;
        }
        if (input.ReadByte() != -1) throw new InvalidDataException("Script file changed while its snapshot was read.");
        return bytes;
    }

    private static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Script paths cannot contain reparse points.");
    }
}
