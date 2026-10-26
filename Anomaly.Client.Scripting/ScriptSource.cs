using System.Security.Cryptography;
using System.Text;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Scripting;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting;

/// <summary>Immutable source copied from one independently validated script-pack entry.</summary>
internal sealed record ScriptSource(string PackId, string Address, string Source, string SourceSha256, int SourceBytes,
    IReadOnlyList<ScriptMessageSchema>? MessageSchemas = null, string? IndexSha256 = null)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal void Validate()
    {
        if (MessageSchemas != null)
        {
            if (MessageSchemas.Count > ScriptMessageSchema.MaxSchemas || !AssetPackIndexCodec.IsHash(IndexSha256))
                throw new InvalidDataException("Script-pack message contracts require an exact approved index digest.");
            for (var index = 0; index < MessageSchemas.Count; index++)
            {
                var schema = MessageSchemas[index] ?? throw new InvalidDataException("A script-pack message contract is null.");
                schema.Validate();
                for (var previous = 0; previous < index; previous++)
                    if (schema.Name == MessageSchemas[previous].Name)
                        throw new InvalidDataException("A script-pack message contract name is duplicated.");
            }
        }
        // The producer applies remote/local namespace policy. This shared source
        // layer also accepts reserved namespaces from verified local packs.
        if (!IsCanonicalIdentity(PackId, Address))
            throw new InvalidDataException("Script identity or address is not canonical.");
        if (Source == null || Source.Length > ScriptPackLimits.MaxScriptBytesPerFile ||
            SourceBytes < 0 || SourceBytes > ScriptPackLimits.MaxScriptBytesPerFile ||
            !AssetPackIndexCodec.IsHash(SourceSha256))
            throw new InvalidDataException("Script source metadata exceeds its limits or is incomplete.");
        int byteCount;
        try { byteCount = StrictUtf8.GetByteCount(Source); }
        catch (EncoderFallbackException) { throw new InvalidDataException("Script source is not strict UTF-8 text."); }
        if (byteCount != SourceBytes)
            throw new InvalidDataException("Script source byte count does not match its approved entry.");
        // Empty encoding/decoding returns process-lifetime singletons. There is no
        // backing allocation to reserve or to wait for collection to reclaim.
        var bytes = byteCount == 0 ? Array.Empty<byte>() : EncodeAndValidateSource(byteCount);
        if (byteCount == 0) _ = ScriptSourceValidator.Validate(bytes);
        Span<byte> actual = stackalloc byte[32];
        SHA256.HashData(bytes, actual);
        for (var index = 0; index < actual.Length; index++)
        {
            var high = Hex(SourceSha256[index * 2]);
            var low = Hex(SourceSha256[index * 2 + 1]);
            if (actual[index] != (high << 4 | low))
                throw new InvalidDataException("Script source digest does not match its approved entry.");
        }
    }

    private byte[] EncodeAndValidateSource(int byteCount)
    {
        // Do not use capturing allocation factories here: the compiler can
        // allocate their display class at Validate entry even for empty sources.
        // Each positive-sized backing store is reserved before it is created.
        using var encodedMemory = MemoryBudget.Reserve(32L + byteCount);
        var bytes = StrictUtf8.GetBytes(Source);
        encodedMemory.Bind(bytes);
        using var decodedMemory = MemoryBudget.Reserve(32L + 2L * Source.Length);
        var validated = ScriptSourceValidator.Validate(bytes);
        decodedMemory.Bind(validated);
        return bytes;
    }

    // This check runs repeatedly during admission, including for empty sources.
    // Match the wire address/pack rules without allocating substrings, split
    // arrays, regex runners or a composed prefix outside allocation accounting.
    private static bool IsCanonicalIdentity(string packId, string address)
    {
        if (string.IsNullOrEmpty(packId) || packId.Length > 128 || string.IsNullOrEmpty(address) ||
            address.Length > AssetAddress.MaxLength || !address.StartsWith("mods/", StringComparison.Ordinal) ||
            !address.EndsWith(".lua", StringComparison.Ordinal)) return false;
        var colon = packId.IndexOf(':');
        if (colon < 1 || colon > 64 || colon == packId.Length - 1) return false;
        var owner = packId.AsSpan(0, colon);
        var segmentLength = 0;
        var segments = 1;
        foreach (var character in owner)
        {
            if (character == '.')
            {
                if (segmentLength == 0 || ++segments > 40) return false;
                segmentLength = 0;
            }
            else
            {
                if ((segmentLength == 0 && character is not (>= 'a' and <= 'z')) ||
                    (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_') ||
                    ++segmentLength > 32) return false;
            }
        }
        if (segmentLength == 0) return false;
        var name = packId.AsSpan(colon + 1);
        if (name.SequenceEqual(".") || name.SequenceEqual("..")) return false;
        foreach (var character in name)
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-')
                return false;
        if (address.Length <= 5 + owner.Length || !address.AsSpan(5, owner.Length).SequenceEqual(owner) ||
            address[5 + owner.Length] != '/') return false;
        var path = address.AsSpan(6 + owner.Length);
        var start = 0;
        for (var index = 0; index <= path.Length; index++)
        {
            if (index == path.Length || path[index] == '/')
            {
                var segment = path.Slice(start, index - start);
                if (segment.Length == 0 || segment.SequenceEqual(".") || segment.SequenceEqual("..")) return false;
                start = index + 1;
            }
            else if (path[index] is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-')
                return false;
        }
        return true;
    }

    private static int Hex(char value) => value <= '9' ? value - '0' : value - 'a' + 10;
}
