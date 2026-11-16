using System;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Networking;

/// <summary>Bounds for the negotiated file-only transport pool. Physical lanes are not message channels.</summary>
internal static class BulkTransferProtocol
{
    internal const byte ProfileVersion = 2;
    internal const int MaxDomains = 16;
    internal const int FirstPhysicalChannel = 6;
    internal const int MaxPhysicalChannels = 64;
    internal const uint MaxWindowBytes = 2u * 1024u * 1024u;
    internal const ushort MaxWindowFrames = 128;
    internal const int MaxOutstandingFramesPerDomain = 8;
    internal const int MaxFilesInFlight = 4;
    internal const byte MaxRetries = 2;
    internal const int PrefetchBytes = 256 * 1024;
    internal const int BatchThresholdBytes = 24 * 1024;
    internal const int MaxBytesPerUpdate = 512 * 1024;
    internal const int MaxFramesPerUpdate = 32;
    internal const double MaxMillisecondsPerUpdate = 2;

    internal static bool IsValidMask(ulong mask, bool allowEmpty = true) =>
        (allowEmpty || mask != 0) && (mask & ((1UL << FirstPhysicalChannel) - 1)) == 0 &&
        DomainCount(mask) <= MaxDomains;

    internal static int DomainCount(ulong mask)
    {
        int count = 0;
        while (mask != 0) { mask &= mask - 1; count++; }
        return count;
    }

    internal static ulong SelectLaneMask(ulong localMask, ulong remoteMask)
    {
        if (!IsValidMask(localMask) || !IsValidMask(remoteMask))
            throw new ArgumentException("Bulk transport masks are invalid.");
        return localMask & remoteMask;
    }

    internal static int GetPhysicalChannel(ulong mask, int domain)
    {
        if (!IsValidMask(mask, allowEmpty: false) || domain < 0 || domain >= DomainCount(mask))
            throw new ArgumentOutOfRangeException(nameof(domain), "Bulk domain is outside the negotiated pool.");
        for (int channel = FirstPhysicalChannel; channel < MaxPhysicalChannels; channel++)
        {
            if ((mask & (1UL << channel)) == 0) continue;
            if (domain-- == 0) return channel;
        }
        throw new ArgumentOutOfRangeException(nameof(domain));
    }

    /// <summary>Reads the authenticated, fixed file-chunk prefix before selecting its replay domain.</summary>
    internal static bool TryReadChunkDomain(byte[] payload, int offset, int count, out byte domain)
    {
        domain = 0;
        if (payload == null || offset < 0 || count < FileChunkMessage.PrefixBytes ||
            offset > payload.Length - count || payload[offset] != ProfileVersion ||
            payload[offset + 1] >= MaxDomains || payload[offset + 2] > MaxRetries)
            return false;
        domain = payload[offset + 1];
        return true;
    }

    internal static bool TryReadChunkSession(byte[] payload, int offset, int count, out ulong sessionId)
    {
        sessionId = 0;
        if (!TryReadChunkDomain(payload, offset, count, out _) || count < FileChunkMessage.PrefixBytes + 8)
            return false;
        sessionId = BitConverter.ToUInt64(payload, offset + FileChunkMessage.PrefixBytes);
        return sessionId != 0;
    }
}
