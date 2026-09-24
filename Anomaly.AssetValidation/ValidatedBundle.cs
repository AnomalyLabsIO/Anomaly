using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Anomaly.AssetValidation;

/// <summary>Owns the exact validated input. No writable buffer or file path escapes this lease.</summary>
public sealed class ValidatedBundle : IDisposable {
    private byte[] _bytes;
    private readonly object _sync = new object();

    internal ValidatedBundle(byte[] bytes, string sha256, int expandedBytes, long decodedBytes, List<ValidatedAsset> assets) {
        _bytes = bytes;
        Sha256 = sha256;
        BundleLength = bytes.Length;
        ExpandedBytes = expandedBytes;
        TotalDecodedBytes = decodedBytes;
        Assets = assets.AsReadOnly();
    }

    public string Sha256 { get; }
    public int BundleLength { get; }
    public int ExpandedBytes { get; }
    public long TotalDecodedBytes { get; }
    public IReadOnlyList<ValidatedAsset> Assets { get; }

    /// <summary>Write to a new protected native-load file and hold it against replacement until unloading.</summary>
    public void WriteVerifiedBytes(Stream destination, CancellationToken cancellationToken = default) {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        lock (_sync) {
            if (_bytes == null) throw new ObjectDisposedException(nameof(ValidatedBundle));
            for (var offset = 0; offset < _bytes.Length;) {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(65536, _bytes.Length - offset);
                destination.Write(_bytes, offset, count);
                offset += count;
            }
        }
    }

    public void Dispose() {
        lock (_sync) _bytes = null;
    }
}

public sealed class ValidatedAsset {
    internal ValidatedAsset(long pathId, int classId) { PathId = pathId; ClassId = classId; }
    public long PathId { get; }
    public int ClassId { get; }
    public string Address { get; internal set; }
    public string EntryName { get; internal set; }
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    public long AudioFrames { get; internal set; }
    public int SampleRate { get; internal set; }
    public int Channels { get; internal set; }
    public int Vertices { get; internal set; }
    public int Triangles { get; internal set; }
    public long DecodedBytes { get; internal set; }
    public long CompressedBytes { get; internal set; }
}
