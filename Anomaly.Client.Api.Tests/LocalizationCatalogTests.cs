using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.Client.Api.Internal.Localization;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class LocalizationCatalogTests : IClassFixture<LocalizationCatalogFixture>
{
    private readonly LocalizationCatalogFixture _catalogs;

    public LocalizationCatalogTests(LocalizationCatalogFixture catalogs) => _catalogs = catalogs;

    [Fact]
    public void Shipped_catalogs_cover_every_key_with_matching_valid_placeholders()
    {
        var english = LocalizationCatalogFixture.ReadCatalog("en");
        var russian = LocalizationCatalogFixture.ReadCatalog("ru");
        var keys = typeof(TrKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!).OrderBy(key => key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(keys, english.Keys.OrderBy(key => key));
        Assert.Equal(keys, russian.Keys.OrderBy(key => key));
        foreach (string key in keys)
        {
            var source = CompositeFormat.Parse(english[key]);
            var translated = CompositeFormat.Parse(russian[key]);
            Assert.Equal(source.MinimumArgumentCount, translated.MinimumArgumentCount);
            Assert.Equal(PlaceholderIndexes(english[key]), PlaceholderIndexes(russian[key]));
            object[] args = Enumerable.Repeat<object>(1234.5m, source.MinimumArgumentCount).ToArray();
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, english[key], args), _catalogs.English.Get(key, args));
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, russian[key], args), _catalogs.Russian.Get(key, args));
        }
    }

    [Theory]
    [InlineData("en", "Diagnostic code: ")]
    [InlineData("ru", "Код диагностики: ")]
    public void Connection_notice_translates_explanations_but_preserves_codes_and_commands(string locale, string label)
    {
        var service = locale == "en" ? _catalogs.English : _catalogs.Russian;
        const string code = "AUTH-REJECT-{1}";
        const string command = "anomaly.status";
        string notice = service.Get(TrKeys.Connection_FailureDetails,
            service.Get(TrKeys.Auth_Rejected), service.Get(TrKeys.Auth_ReviewRequirements), code, command);

        Assert.StartsWith(service.Get(TrKeys.Auth_Rejected), notice);
        Assert.Contains(label + code, notice);
        Assert.Contains(command, notice);
        Assert.DoesNotContain("anomaly.auth.", notice);
    }

    [Fact]
    public void Content_consent_preserves_fingerprint_and_formats_multiple_numeric_arguments()
    {
        string text = _catalogs.Russian.Get(TrKeys.Content_ApprovalDetails, "ABCD 0012 {0}", 3, 7, 1.5d, 1572864L);
        Assert.Contains("ABCD 0012 {0}", text);
        Assert.Contains("Предметов: " + 3.ToString("N0"), text);
        Assert.Contains("Файлов: " + 7.ToString("N0"), text);
        Assert.Contains(1.5d.ToString("N2"), text);
        Assert.Contains(1572864L.ToString("N0"), text);
    }

    private static int[] PlaceholderIndexes(string template) =>
        Regex.Matches(template, @"(?<!\{)\{(\d+)(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(index => index).ToArray();
}
