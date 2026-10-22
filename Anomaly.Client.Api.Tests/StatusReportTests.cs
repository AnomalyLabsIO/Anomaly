using Anomaly.Client.Api.Internal.Diagnostics;
using Anomaly.Client.Api.Modding;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class StatusReportTests : IClassFixture<LocalizationCatalogFixture>
{
    private readonly LocalizationCatalogFixture _catalogs;

    public StatusReportTests(LocalizationCatalogFixture catalogs) => _catalogs = catalogs;

    [Fact]
    public void Shareable_report_omits_arbitrary_health_text_and_identifiers()
    {
        var record = new FeatureHealthRecord("example.mod", "bad\nAlice <b>token</b>", FeatureHealthStatus.Failed,
            "Authorization: Bearer private-token; nickname Alice; enrollment secret@anomaly",
            "private.server:7777", @"C:\Users\Alice\secret", "private-key=abcdef");
        var report = Format(false, false, new[] { record });
        Assert.Contains("example.mod:[module-feature]", report);
        Assert.Contains("Failed", report);
        Assert.Contains("<game>/MelonLoader/Latest.log", report);
        foreach (var secret in new[] { "Alice", "private-token", "secret@anomaly", "private.server", "abcdef" })
            Assert.DoesNotContain(secret, report);
    }

    [Fact]
    public void Hostname_shaped_custom_feature_ids_are_not_shared()
    {
        var record = new FeatureHealthRecord("example", "private.server", FeatureHealthStatus.Failed, "", "", "", "");
        Assert.DoesNotContain("private.server", Format(false, false, new[] { record }));
        var report = StatusReport.Format(new Version(1, 0), new Version(1, 0), new Version(14, 2, 7),
            false, false, false, false, Array.Empty<FeatureHealthRecord>(), _catalogs.English.Get);
        Assert.Contains("setup incomplete", report);
    }

    [Fact]
    public void Known_core_feature_names_remain_actionable_without_free_form_details()
    {
        var record = new FeatureHealthRecord("anomaly", "encrypted-channel-byte-send", FeatureHealthStatus.Degraded,
            "private detail", "", "", "");
        var report = Format(false, false, new[] { record });
        Assert.Contains("anomaly:encrypted-channel-byte-send", report);
        Assert.DoesNotContain("private detail", report);
    }

    [Fact]
    public void Readiness_and_connection_are_distinct_and_output_is_bounded()
    {
        var records = Enumerable.Range(0, 100).Select(i => new FeatureHealthRecord("example", "feature" + i,
            FeatureHealthStatus.Disabled, "", "", "", "")).ToArray();
        var report = Format(true, false, records);
        Assert.Contains("Profile: ready", report);
        Assert.Contains("Connection: connected, preparing", report);
        Assert.Contains("Additional feature records omitted", report);
        Assert.Equal(64, report.Split('\n').Count(line => line.StartsWith("  example:")));
    }

    [Fact]
    public void Localized_report_preserves_diagnostic_identifiers_and_relative_paths()
    {
        var record = new FeatureHealthRecord("anomaly", "authentication", FeatureHealthStatus.Failed,
            "private detail", "", "", "");
        string report = StatusReport.Format(new Version(1, 0), new Version(1, 0), new Version(14, 2, 7),
            true, false, false, false, new[] { record }, _catalogs.Russian.Get);
        Assert.Contains("Профиль: готов", report);
        Assert.Contains("Подключение: отключено", report);
        Assert.Contains("anomaly:authentication — Failed", report);
        Assert.Contains("<game>/MelonLoader/Latest.log", report);
        Assert.DoesNotContain("private detail", report);
    }

    private string Format(bool connected, bool ready, IEnumerable<FeatureHealthRecord> records) =>
        StatusReport.Format(new Version(1, 0), new Version(1, 0), new Version(14, 2, 7),
            true, false, connected, ready, records, _catalogs.English.Get);
}
