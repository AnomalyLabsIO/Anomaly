using System;
using System.Text;

namespace Anomaly.Client.Api.Internal;

/// <summary>Reads only the existing LiteNetLib string containing the stock base64 UTF-8 reason.</summary>
internal static class StockDisconnectReason
{
    internal const int MaximumPayloadBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryDecode(byte[] payload, out string reason)
    {
        reason = null;
        if (payload == null || payload.Length < 6 || payload.Length > MaximumPayloadBytes) return false;
        int encodedBytes = payload[0] | payload[1] << 8;
        // NetDataWriter.Put(string) stores UTF-8 byte length + 1 as a ushort.
        // Require exactly one field; do not accept truncation or trailing data.
        if (encodedBytes <= 1 || encodedBytes - 1 != payload.Length - 2) return false;
        int count = encodedBytes - 1;
        if ((count & 3) != 0) return false;
        for (int index = 2; index < payload.Length; index++)
        {
            byte value = payload[index];
            if (!(value >= 'A' && value <= 'Z') && !(value >= 'a' && value <= 'z') &&
                !(value >= '0' && value <= '9') && value != '+' && value != '/' && value != '=') return false;
        }
        try
        {
            string encoded = Encoding.ASCII.GetString(payload, 2, count);
            byte[] decoded = Convert.FromBase64String(encoded);
            if (!string.Equals(Convert.ToBase64String(decoded), encoded, StringComparison.Ordinal)) return false;
            string text = StrictUtf8.GetString(decoded);
            if (string.IsNullOrWhiteSpace(text)) return false;
            reason = text;
            return true;
        }
        catch (Exception exception) when (exception is FormatException || exception is DecoderFallbackException)
        { return false; }
    }
}
