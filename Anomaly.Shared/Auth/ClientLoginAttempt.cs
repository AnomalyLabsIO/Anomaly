using System;

namespace Anomaly.Shared.Auth;

public class ClientLoginAttempt
{
    public byte[] signature; // ECDSA DER + AES-GCM tag. Typical ~96 bytes; bounds below give ample headroom.

    /// <summary>Lower bound: ECDSA DER signatures can't legitimately be shorter than this.</summary>
    public const int MinSize = 32;

    /// <summary>Upper bound: prevents a client claiming a gigantic signature from forcing per-connection allocations.</summary>
    public const int MaxSize = 256;

    public byte[] Encode()
    {
        return signature;
    }

    public static ClientLoginAttempt Decode(byte[] buf)
    {
        if (buf == null)
            throw new ArgumentNullException(nameof(buf));
        if (buf.Length < MinSize || buf.Length > MaxSize)
            throw new ArgumentException(
                $"ClientLoginAttempt payload length {buf.Length} outside accepted range [{MinSize}, {MaxSize}].",
                nameof(buf));

        var attempt = new ClientLoginAttempt();
        attempt.signature = buf;
        return attempt;
    }
}