using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Assets;

/// <summary>
/// Process-wide containment for server-originated decoded assets. A reservation accounts for
/// peak working memory before allocation and can atomically transition to retained live bytes.
/// </summary>
internal static class RemoteAssetDecodeBudget
{
    internal const int MaxTextureDimension = 4096;
    internal const long MaxTexturePixels = 16L * 1024L * 1024L;
    internal const int MaxEncodedTextureBytes = 16 * 1024 * 1024;
    internal const int MaxEncodedAudioBytes = 32 * 1024 * 1024;
    internal const int MaxAudioChannels = 2;
    internal const int MinAudioSampleRate = 8000;
    internal const int MaxAudioSampleRate = 96000;
    internal const double MaxAudioDurationSeconds = 30d;
    internal const long MaxDecodedBytesPerAsset = 64L * 1024L * 1024L;

    private const int MaxConcurrentDecodes = 2;
    private const long MaxPeakBytesPerDecode = 160L * 1024L * 1024L;
    private const long MaxAggregateBytes = 256L * 1024L * 1024L;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, long> LiveBytesByKey =
        new(StringComparer.Ordinal);

    private static int _inFlightCount;
    private static long _inFlightBytes;
    private static long _liveBytes;

    internal static bool TryReserve(
        long peakBytes,
        long liveBytes,
        out Reservation reservation,
        out string reason)
    {
        reservation = null;
        if (peakBytes <= 0 || liveBytes <= 0 || liveBytes > MaxDecodedBytesPerAsset)
        {
            reason = $"decoded asset exceeded the {MaxDecodedBytesPerAsset}-byte per-asset limit";
            return false;
        }
        if (peakBytes < liveBytes || peakBytes > MaxPeakBytesPerDecode)
        {
            reason = $"decode working set exceeded the {MaxPeakBytesPerDecode}-byte per-decode limit";
            return false;
        }

        lock (Sync)
        {
            if (_inFlightCount >= MaxConcurrentDecodes)
            {
                reason = $"the {MaxConcurrentDecodes}-decode concurrency limit is busy";
                return false;
            }
            if (_liveBytes > MaxAggregateBytes - _inFlightBytes ||
                _liveBytes + _inFlightBytes > MaxAggregateBytes - peakBytes)
            {
                reason = $"decoded assets exceeded the {MaxAggregateBytes}-byte aggregate budget";
                return false;
            }

            _inFlightCount++;
            _inFlightBytes += peakBytes;
            reservation = new Reservation(peakBytes, liveBytes);
            reason = null;
            return true;
        }
    }

    internal static void ReleaseLive(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        lock (Sync)
        {
            if (!LiveBytesByKey.TryGetValue(key, out long bytes))
                return;
            LiveBytesByKey.Remove(key);
            _liveBytes = Math.Max(0, _liveBytes - bytes);
        }
    }

    internal sealed class Reservation : IDisposable
    {
        private readonly long _peakBytes;
        private readonly long _liveBytes;
        private bool _active = true;

        internal Reservation(long peakBytes, long liveBytes)
        {
            _peakBytes = peakBytes;
            _liveBytes = liveBytes;
        }

        internal void CommitLive(string key)
        {
            lock (Sync)
            {
                if (!_active)
                    return;

                ReleaseInFlightLocked();
                if (!string.IsNullOrEmpty(key))
                {
                    if (LiveBytesByKey.TryGetValue(key, out long existingBytes))
                        RemoteAssetDecodeBudget._liveBytes =
                            Math.Max(0, RemoteAssetDecodeBudget._liveBytes - existingBytes);
                    LiveBytesByKey[key] = _liveBytes;
                    RemoteAssetDecodeBudget._liveBytes += _liveBytes;
                }
                _active = false;
            }
        }

        public void Dispose()
        {
            lock (Sync)
            {
                if (!_active)
                    return;
                ReleaseInFlightLocked();
                _active = false;
            }
        }

        private void ReleaseInFlightLocked()
        {
            _inFlightCount = Math.Max(0, _inFlightCount - 1);
            _inFlightBytes = Math.Max(0, _inFlightBytes - _peakBytes);
        }
    }
}
