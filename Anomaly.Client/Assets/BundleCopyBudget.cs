using System;
using System.Collections.Generic;
using System.IO;

namespace Anomaly.Client.Assets;

/// <summary>Plans combined residency before acquisition; full bundle validation remains authoritative.</summary>
internal static class BundleCopyBudget
{
    internal const long MaximumBytes = 256L * 1024 * 1024;
    internal const int MaximumCopies = 128;

    internal readonly struct DecodedDemand
    {
        internal readonly string BundleHash, CopyKey;
        internal readonly long Bytes;
        internal DecodedDemand(string bundleHash, string copyKey, long bytes)
        { BundleHash = bundleHash; CopyKey = copyKey; Bytes = bytes; }
    }

    internal static void ValidateDecoded(IEnumerable<DecodedDemand> pinned, IEnumerable<DecodedDemand> offered)
    {
        // Native bundles are shared by bundle hash, while the exact index is
        // part of validation identity. Match Materialize/PreflightLayer semantics.
        var bundles = new Dictionary<string, DecodedDemand>(StringComparer.Ordinal);
        long localBytes = 0;
        foreach (var demand in pinned)
            if (AddDecoded(bundles, demand)) localBytes = checked(localBytes + demand.Bytes);
        long totalBytes = localBytes;
        foreach (var demand in offered)
            if (AddDecoded(bundles, demand)) totalBytes = checked(totalBytes + demand.Bytes);
        if (totalBytes > MaximumBytes)
            throw new BundleCopyBudgetException(
                $"Your active local packs and this server's bundles need {totalBytes:N0} bytes " +
                $"of decoded asset capacity; the limit is {MaximumBytes:N0} bytes (256 MiB). " +
                $"Local packs retain {localBytes:N0} bytes. The server must offer a smaller asset set, or conflicting local packs must be disabled before reconnecting.");
    }

    internal static void ValidateMeasurement(long declaredBytes, long measuredBytes)
    {
        if (declaredBytes <= 0 || declaredBytes > MaximumBytes || measuredBytes != declaredBytes)
            throw new InvalidDataException("The independently validated decoded bundle size disagreed with its authored resource budget.");
    }

    private static bool AddDecoded(Dictionary<string, DecodedDemand> bundles, DecodedDemand demand)
    {
        if (string.IsNullOrEmpty(demand.BundleHash) || string.IsNullOrEmpty(demand.CopyKey) ||
            demand.Bytes <= 0 || demand.Bytes > MaximumBytes)
            throw new InvalidDataException("Decoded capacity requires an exact bundle/index identity and bounded positive size.");
        if (bundles.TryGetValue(demand.BundleHash, out var previous))
        {
            if (previous.CopyKey != demand.CopyKey || previous.Bytes != demand.Bytes)
                throw new InvalidDataException("One native bundle was offered with inconsistent indexes or decoded resource budgets.");
            return false;
        }
        bundles.Add(demand.BundleHash, demand);
        return true;
    }

    internal static void Validate(IEnumerable<KeyValuePair<string, long>> pinnedLocal,
        IEnumerable<KeyValuePair<string, long>> offered)
    {
        var copies = new Dictionary<string, long>(StringComparer.Ordinal);
        long localBytes = 0;
        foreach (var copy in pinnedLocal)
            if (Add(copies, copy)) localBytes = checked(localBytes + copy.Value);
        long totalBytes = localBytes;
        foreach (var copy in offered)
            if (Add(copies, copy)) totalBytes = checked(totalBytes + copy.Value);
        if (totalBytes > MaximumBytes)
            throw new BundleCopyBudgetException(
                $"Your active local packs and this server's bundles need {totalBytes:N0} bytes " +
                $"of validated bundle storage; the limit is {MaximumBytes:N0} bytes (256 MiB). " +
                $"Local packs retain {localBytes:N0} bytes. Disable conflicting local packs or join a server with a smaller content offer, then reconnect.");
        if (copies.Count > MaximumCopies)
            throw new BundleCopyBudgetException(
                $"Your active local packs and this server's bundles need {copies.Count} distinct validated bundle copies; " +
                $"the limit is {MaximumCopies}. Disable conflicting local packs or join a server with a smaller content offer, then reconnect.");
    }

    private static bool Add(Dictionary<string, long> copies, KeyValuePair<string, long> copy)
    {
        if (string.IsNullOrEmpty(copy.Key) || copy.Value <= 0)
            throw new InvalidDataException("Bundle-copy capacity requires an exact cache key and positive size.");
        if (copies.TryGetValue(copy.Key, out long size))
        {
            if (size != copy.Value)
                throw new InvalidDataException("An exact validated bundle copy was offered with inconsistent sizes.");
            return false;
        }
        copies.Add(copy.Key, copy.Value);
        return true;
    }
}

internal sealed class BundleCopyBudgetException : IOException
{
    internal BundleCopyBudgetException(string message) : base(message) { }
}
