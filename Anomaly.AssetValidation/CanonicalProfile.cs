using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetsTools.NET;

namespace Anomaly.AssetValidation;

internal sealed class CanonicalProfile {
    internal static readonly IReadOnlyDictionary<int, CanonicalProfile> Types = Load();
    internal readonly byte[] Record;
    internal readonly AssetTypeTemplateField Template;
    private CanonicalProfile(byte[] record) {
        Record = record;
        using (var stream = new MemoryStream(record, false)) {
            var type = new TypeTreeType();
            type.Read(new AssetsFileReader(stream), 22, true, false);
            Template = new AssetTypeTemplateField();
            Template.FromTypeTree(type); // Embedded, reviewed schema only; never an untrusted type tree.
        }
    }
    private static IReadOnlyDictionary<int, CanonicalProfile> Load() {
        var result = new Dictionary<int, CanonicalProfile>();
        foreach (var id in new[] { 28, 43, 83, 142, 213 }) {
            using (var stream = typeof(CanonicalProfile).GetTypeInfo().Assembly.GetManifestResourceStream("Anomaly.AssetValidation.Profiles." + id + ".bin")) {
                if (stream == null || stream.Length > 16384) throw new InvalidDataException("Missing canonical asset schema.");
                using (var data = new MemoryStream()) { stream.CopyTo(data); result.Add(id, new CanonicalProfile(data.ToArray())); }
            }
        }
        return result;
    }
}
