using System;
using System.Collections.Generic;
using System.Linq;

namespace Anomaly.AssetValidation;

public sealed class BundleValidationRequest {
    public BundleValidationRequest(string expectedSha256, IEnumerable<BundleAssetExpectation> assets) {
        ExpectedSha256 = expectedSha256 ?? throw new ArgumentNullException(nameof(expectedSha256));
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        Assets = Array.AsReadOnly(assets.Take(4096).ToArray());
    }

    public string ExpectedSha256 { get; }
    public IReadOnlyList<BundleAssetExpectation> Assets { get; }
}

public sealed class BundleAssetExpectation {
    public BundleAssetExpectation(string address, string entryName, int classId, int width = 0, int height = 0,
        long audioFrames = 0, int sampleRate = 0, int channels = 0, int vertices = 0, int triangles = 0) {
        Address = address;
        EntryName = entryName;
        ClassId = classId;
        Width = width;
        Height = height;
        AudioFrames = audioFrames;
        SampleRate = sampleRate;
        Channels = channels;
        Vertices = vertices;
        Triangles = triangles;
    }

    public string Address { get; }
    public string EntryName { get; }
    public int ClassId { get; }
    public int Width { get; }
    public int Height { get; }
    public long AudioFrames { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public int Vertices { get; }
    public int Triangles { get; }
}
