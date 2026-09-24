using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Anomaly.Shared.Assets;

/// <summary>Supported stock targets from the pinned game database, not audio filenames.</summary>
public static class StockAssetProfile
{
    // Provenance: current SCPSL_Data/sharedassets2.assets Cassie Words pathID291292.
    // Extraction report SHA256:817535c25fef23a881c08c0c92de2cc916c0228fb0a88d9efbac1683db5df6fb.
    // GameAssembly SHA256:aea301eec781008828c8bb82e73b8689e5cf3041384eb896a8585d3dcf57badb.
    private static readonly Dictionary<string, CassieAssetTiming> Cassie = Load();
    public static bool TryGetCassie(AssetAddress address, out CassieAssetTiming timing) => TryGetCassie(address.Value, out timing);
    public static bool TryGetCassie(string address, out CassieAssetTiming timing)
    {
        timing = default;
        return address != null && Cassie.TryGetValue(address, out timing);
    }

    private static Dictionary<string, CassieAssetTiming> Load()
    {
        var result = new Dictionary<string, CassieAssetTiming>(StringComparer.Ordinal);
        using (Stream stream = typeof(StockAssetProfile).Assembly.GetManifestResourceStream("Anomaly.Shared.Assets.CassieProfile.tsv"))
        using (var reader = new StreamReader(stream ?? throw new InvalidDataException("The stock asset profile is missing.")))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split('|');
                if (parts.Length != 6 || result.Count >= 973) throw new InvalidDataException("Invalid compiled CASSIE profile.");
                int durationBits = unchecked((int)uint.Parse(parts[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                result.Add(parts[0], new CassieAssetTiming(parts[1],
                    int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture),
                    int.Parse(parts[4], CultureInfo.InvariantCulture), BitConverter.ToSingle(BitConverter.GetBytes(durationBits), 0)));
            }
        }
        if (result.Count != 973) throw new InvalidDataException("Incomplete compiled CASSIE profile.");
        return result;
    }
}

/// <summary>Scheduling duration and physical clip duration are deliberately distinct.</summary>
public readonly struct CassieAssetTiming
{
    public string ApiName { get; }
    public int SampleFrames { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public float SchedulingDurationSeconds { get; }
    public double ClipDurationSeconds => SampleFrames / (double)SampleRate;
    internal CassieAssetTiming(string apiName, int frames, int rate, int channels, float duration)
    {
        ApiName = apiName; SampleFrames = frames; SampleRate = rate; Channels = channels;
        SchedulingDurationSeconds = duration;
    }
    public bool MatchesClip(int frames, int rate) => frames > 0 && rate > 0 &&
        Math.Abs(frames / (double)rate - ClipDurationSeconds) <= 1d / SampleRate + 0.0000001d;
}
