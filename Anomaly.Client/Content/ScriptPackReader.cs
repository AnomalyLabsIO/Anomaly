#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Client.Scripting;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Content;

/// <summary>Verifies private source snapshots against the complete authenticated script inventory.</summary>
internal static class ScriptPackReader
{
    internal static IReadOnlyList<ScriptSource> Read(ContentCatalog catalog, Func<byte[], FileContentKind, byte[]> readFile)
    {
        ContentCatalogMessage.ValidateCatalog(catalog, validateDeclaredHash: true);
        if (readFile == null) throw new ArgumentNullException(nameof(readFile));
        var result = new List<ScriptSource>();
        // Physical source blobs can be shared by distinct logical entries. Keep
        // one decoded string per hash, rather than allocating it again per name.
        var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
        int schemaCount = 0;
        foreach (var pack in catalog.ScriptPacks)
        {
            byte[] bytes = readFile(pack.IndexHash, FileContentKind.ScriptIndex);
            Verify(bytes, pack.IndexSize, pack.IndexHash, ScriptPackLimits.MaxIndexBytes);
            ScriptPackIndex index = ScriptPackIndexCodec.Parse(bytes);
            schemaCount = checked(schemaCount + index.Messages.Count);
            if (schemaCount > ScriptMessageLimits.MaxSchemasPerCatalog)
                throw new InvalidDataException("Script message schema inventory exceeded its aggregate limit.");
            if (index.PackId != pack.PackId || index.Sources.Count != pack.Sources.Count)
                throw new InvalidDataException("Script index disagreed with its authenticated inventory.");
            string indexHash = ScriptSourceValidator.HashHex(bytes);
            var offered = new Dictionary<string, CatalogScriptSource>(StringComparer.Ordinal);
            foreach (var source in pack.Sources) offered.Add(source.Address, source);
            foreach (var source in index.Sources)
                if (!offered.TryGetValue(source.Address, out var expected) || expected.Size != source.SourceBytes ||
                    Hex(expected.Hash) != source.SourceSha256)
                    throw new InvalidDataException("Script source metadata disagreed with its authenticated inventory.");
            var schemas = index.Messages.Select(schema => schema.Clone()).ToArray();
            foreach (var source in index.Sources.OrderBy(source => source.Address, StringComparer.Ordinal))
            {
                if (!decoded.TryGetValue(source.SourceSha256, out string text))
                {
                    var expected = offered[source.Address];
                    byte[] content = expected.Size == 0 ? Array.Empty<byte>() : readFile(expected.Hash, FileContentKind.LuaSource);
                    Verify(content, expected.Size, expected.Hash, ScriptPackLimits.MaxScriptBytesPerFile);
                    text = ScriptSourceValidator.Validate(content);
                    decoded.Add(source.SourceSha256, text);
                }
                result.Add(new ScriptSource(pack.PackId, source.Address, text, source.SourceSha256,
                    source.SourceBytes, schemas, indexHash));
            }
        }
        return result.ToArray();
    }

    private static void Verify(byte[] bytes, uint expectedSize, byte[] expectedHash, int maximum)
    {
        if (bytes == null || bytes.Length > maximum || bytes.Length != expectedSize ||
            !CatalogBuilder.HashesEqual(ScriptSourceValidator.Sha256(bytes), expectedHash))
            throw new InvalidDataException("Script file length or SHA-256 disagreed with its authenticated offer.");
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
}
