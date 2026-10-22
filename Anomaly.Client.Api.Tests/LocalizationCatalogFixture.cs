using Anomaly.Client.Api.Localization;
using YamlDotNet.Serialization;

namespace Anomaly.Client.Api.Tests;

public sealed class LocalizationCatalogFixture : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "Anomaly.Localization.Catalog.Tests");
    private readonly string _root;

    public LocalizationCatalogFixture()
    {
        _root = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
        English = new LocalizationService(Path.Combine(_root, "en"), GetType().Assembly, "en");
        Russian = new LocalizationService(Path.Combine(_root, "ru"), GetType().Assembly, "ru");
    }

    internal LocalizationService English { get; }
    internal LocalizationService Russian { get; }

    internal static Dictionary<string, string> ReadCatalog(string locale)
    {
        using var stream = typeof(LocalizationCatalogFixture).Assembly.GetManifestResourceStream(
            "Anomaly.Client.Resources.i18n." + locale + ".yaml")!;
        using var reader = new StreamReader(stream);
        return new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, string>>(reader);
    }

    public void Dispose()
    {
        string resolved = Path.GetFullPath(_root);
        if (string.Equals(Path.GetDirectoryName(resolved), Path.GetFullPath(_parent), StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(Path.GetFileName(resolved), "N", out _) && Directory.Exists(resolved))
            Directory.Delete(resolved, recursive: true);
    }
}
