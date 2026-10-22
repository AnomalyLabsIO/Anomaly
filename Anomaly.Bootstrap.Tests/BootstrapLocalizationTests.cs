using System.Globalization;
using System.Reflection;
using System.Text;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class BootstrapLocalizationTests
{
    private static readonly Assembly Resources = typeof(BootstrapFailureTranslations).Assembly;

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Early_catalogs_cover_all_failure_keys_with_valid_formats(string locale)
    {
        string resource = Resources.GetManifestResourceNames().Single(name => name.EndsWith(".i18n." + locale + ".yaml"));
        using var stream = Resources.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var catalog = BootstrapFailureTranslations.ReadCatalog(reader);
        string[] keys = typeof(TrKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(key => key.StartsWith(BootstrapFailureTranslations.KeyPrefix, StringComparison.Ordinal))
            .OrderBy(key => key).ToArray();
        Assert.NotEmpty(keys);
        Assert.Equal(keys, catalog.Keys.OrderBy(key => key));
        var translations = new BootstrapFailureTranslations(Resources, locale);
        foreach (string key in keys)
        {
            var format = CompositeFormat.Parse(catalog[key]);
            object[] arguments = Enumerable.Repeat<object>("AUTH-42 {0}", format.MinimumArgumentCount).ToArray();
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, catalog[key], arguments), translations.Get(key, arguments));
        }
    }

    [Theory]
    [InlineData("en", "Anomaly cannot safely continue.")]
    [InlineData("ru", "Anomaly не может безопасно продолжить работу.")]
    [InlineData("ru_RU", "Anomaly не может безопасно продолжить работу.")]
    [InlineData("ru-RU", "Anomaly не может безопасно продолжить работу.")]
    [InlineData("zz", "Anomaly cannot safely continue.")]
    public void Dialog_works_before_client_localization_and_preserves_literal_details(string locale, string expected)
    {
        var translations = new BootstrapFailureTranslations(Resources, locale);
        const string method = "Anomaly.Client.Core::EnsureOfflineAuthenticationMode {0}";
        const string log = "MelonLoader/Latest.log";
        string reason = translations.Get(TrKeys.Bootstrap_InterceptorMissing, method, "GuardHook");
        string dialog = translations.Get(TrKeys.Bootstrap_FailureDialog, reason, log);
        Assert.StartsWith(expected, dialog);
        Assert.Contains(method + " <- GuardHook", dialog);
        Assert.Contains(log, dialog);
        Assert.Contains("\r\n\r\n", dialog);
        Assert.DoesNotContain(BootstrapFailureTranslations.KeyPrefix, dialog);
    }

    [Fact]
    public void Incomplete_or_broken_disk_locale_keeps_embedded_fallbacks()
    {
        string root = Path.Combine(Path.GetTempPath(), "Anomaly.Bootstrap.Localization.Tests", Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "i18n", "anomaly");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "ru.yaml"),
                "\"" + TrKeys.Bootstrap_FailureTitle + "\": \"Пользовательский заголовок\"\n" +
                "\"" + TrKeys.Bootstrap_InterceptorMissing + "\": \"Неверный формат {9}\"\n" +
                "\"" + TrKeys.Bootstrap_FailureDialog + "\": \"broken \\q\"\n");
            var translations = new BootstrapFailureTranslations(Resources, "ru_RU", root);
            Assert.Equal("Пользовательский заголовок", translations.Get(TrKeys.Bootstrap_FailureTitle));
            Assert.Equal("Required interceptor missing: AUTH-42 {0} <- GuardHook",
                translations.Get(TrKeys.Bootstrap_InterceptorMissing, "AUTH-42 {0}", "GuardHook"));
            Assert.StartsWith("Anomaly не может безопасно продолжить работу.",
                translations.Get(TrKeys.Bootstrap_FailureDialog, "Details", "Latest.log"));
            Assert.Equal("Необходимая защита Bootstrap недоступна.", translations.Get(TrKeys.Bootstrap_ProtectionUnavailable));
            Assert.StartsWith("Anomaly cannot safely continue.",
                new BootstrapFailureTranslations(Resources, "../../ru", root)
                    .Get(TrKeys.Bootstrap_FailureDialog, "Details", "Latest.log"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
