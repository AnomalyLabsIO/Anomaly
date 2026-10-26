using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Scripting;

/// <summary>Checks untrusted source bytes before any interpreter receives them.</summary>
public static class ScriptSourceValidator
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    public const string EmptySha256Hex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public static string Validate(byte[] bytes)
    {
        if (bytes == null || bytes.Length > ScriptPackLimits.MaxScriptBytesPerFile)
            throw new InvalidDataException("Lua source exceeded its byte limit.");
        string source;
        try { source = Utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Lua source must use strict UTF-8.", exception); }
        foreach (char character in source)
            if (character == '\ufeff' || (char.IsControl(character) && character != '\t' && character != '\r' && character != '\n'))
                throw new InvalidDataException("Lua source contained a byte-order mark or binary control character.");
        if (source.IndexOf("MoonSharp_dump_b64::", StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Lua source cannot contain a compiled dump.");
        return source;
    }

    public static byte[] Sha256(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        using (var hash = SHA256.Create()) return hash.ComputeHash(bytes);
    }

    public static string HashHex(byte[] bytes) => BitConverter.ToString(Sha256(bytes)).Replace("-", string.Empty).ToLowerInvariant();
}
