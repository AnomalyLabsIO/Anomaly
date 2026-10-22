using Anomaly.Client.Api.Localization;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void Formatting_reorders_and_repeats_arguments_without_translating_or_reparsing_them()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("formatted-mod");
        temp.WriteYaml(modDirectory, "en", "example.message: 'Hello {0}; code {1}'\nexample.code: translated code\n");
        temp.WriteYaml(modDirectory, "fr", "example.message: 'Code {1}: bonjour {0}; {1}'\nexample.second: 'Bonjour {1}'\n");
        var service = CreateService(temp.RootPath, "fr");
        Assert.True(service.RegisterMod("example", modDirectory));

        Assert.Equal("Code example.code: bonjour Player {1}; example.code",
            service.Get("example.message", "Player {1}", "example.code"));
        // Composite formatting is zero-based: {1} selects the second argument.
        Assert.Equal("Bonjour Player", service.Get("example.second", "unused", "Player"));
    }

    [Fact]
    public void Formatting_supports_escaped_braces_alignment_and_numeric_format_strings()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("format-specifiers");
        temp.WriteYaml(modDirectory, "en", "example.message: '{{code}} {0:000} [{1,6}]'\n");
        var service = CreateService(temp.RootPath, "en");
        Assert.True(service.RegisterMod("example", modDirectory));

        Assert.Equal("{code} 007 [  E-42]", service.Get("example.message", 7, "E-42"));
    }

    [Fact]
    public void Missing_active_translation_formats_english_fallback_with_original_arguments()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("fallback-format");
        temp.WriteYaml(modDirectory, "en", "example.message: 'Could not connect ({0})'\n");
        var service = CreateService(temp.RootPath, "fr");
        Assert.True(service.RegisterMod("example", modDirectory));

        Assert.Equal("Could not connect (AUTH-42)", service.Get("example.message", "AUTH-42"));
    }

    [Theory]
    [InlineData("Erreur {1}")]
    [InlineData("Erreur {0")]
    public void Invalid_translated_format_falls_back_to_english_and_keeps_error_code(string translated)
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("broken-format");
        temp.WriteYaml(modDirectory, "en", "example.message: 'Could not connect ({0})'\n");
        temp.WriteYaml(modDirectory, "fr", "example.message: '" + translated + "'\n");
        var service = CreateService(temp.RootPath, "fr");
        Assert.True(service.RegisterMod("example", modDirectory));

        Assert.Equal("Could not connect (AUTH-42)", service.Get("example.message", "AUTH-42"));
    }

    [Fact]
    public void Null_and_insufficient_arguments_do_not_crash_the_caller()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("argument-edges");
        temp.WriteYaml(modDirectory, "en", "example.plain: Ready\nexample.message: 'Hello {0}'\nexample.broken: 'Hello {'\n");
        var service = CreateService(temp.RootPath, "en");
        Assert.True(service.RegisterMod("example", modDirectory));

        Assert.Equal("Ready", service.Get("example.plain", (object[])null!));
        Assert.Equal("Hello ", service.Get("example.message", (object)null!));
        Assert.Equal("Hello {0}", service.Get("example.message", Array.Empty<object>()));
        Assert.Equal("Hello {", service.Get("example.broken", "Player"));
        Assert.Equal("example.missing", service.Get("example.missing", "AUTH-42"));
        Assert.Equal(string.Empty, service.Get(null!, "AUTH-42"));
    }

    [Fact]
    public void RegisterMod_valid_catalog_commits_active_and_fallback()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("valid-mod");
        temp.WriteYaml(
            modDirectory,
            "en",
            "example.fallback: English only\nexample.shared: English\n");
        temp.WriteYaml(
            modDirectory,
            "fr",
            "example.shared: Français\nexample.active: Français only\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.True(service.RegisterMod("example", modDirectory));
        Assert.Equal("English only", service.Get("example.fallback"));
        Assert.Equal("Français", service.Get("example.shared"));
        Assert.Equal("Français only", service.Get("example.active"));
        Assert.Equal("example.missing", service.Get("example.missing"));
    }

    [Fact]
    public void RegisterMod_out_of_namespace_failure_is_atomic_and_retryable()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("namespace-mod");
        temp.WriteYaml(
            modDirectory,
            "en",
            "example.valid: staged\nother.escape: forbidden\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.False(service.RegisterMod("example", modDirectory));
        Assert.Equal("example.valid", service.Get("example.valid"));
        Assert.Equal("other.escape", service.Get("other.escape"));

        temp.WriteYaml(modDirectory, "en", "example.valid: committed\n");

        Assert.True(service.RegisterMod("example", modDirectory));
        Assert.Equal("committed", service.Get("example.valid"));
    }

    [Fact]
    public void RegisterMod_invalid_active_yaml_rolls_back_fallback_and_namespace()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("invalid-active-mod");
        temp.WriteYaml(modDirectory, "en", "example.fallback: staged\n");
        temp.WriteYaml(modDirectory, "fr", "example.active: [unterminated\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.False(service.RegisterMod("example", modDirectory));
        Assert.Equal("example.fallback", service.Get("example.fallback"));
        Assert.Equal("example.active", service.Get("example.active"));

        temp.WriteYaml(modDirectory, "fr", "example.active: committed\n");

        Assert.True(service.RegisterMod("example", modDirectory));
        Assert.Equal("staged", service.Get("example.fallback"));
        Assert.Equal("committed", service.Get("example.active"));
    }

    [Fact]
    public void RegisterMod_invalid_fallback_yaml_rolls_back_active_and_namespace()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("invalid-fallback-mod");
        temp.WriteYaml(modDirectory, "en", "example.fallback: [unterminated\n");
        temp.WriteYaml(modDirectory, "fr", "example.active: staged\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.False(service.RegisterMod("example", modDirectory));
        Assert.Equal("example.fallback", service.Get("example.fallback"));
        Assert.Equal("example.active", service.Get("example.active"));

        temp.WriteYaml(modDirectory, "en", "example.fallback: committed\n");

        Assert.True(service.RegisterMod("example", modDirectory));
        Assert.Equal("committed", service.Get("example.fallback"));
        Assert.Equal("staged", service.Get("example.active"));
    }

    [Fact]
    public void RegisterMod_missing_english_fallback_is_rejected_and_retryable()
    {
        using var temp = new TemporaryDirectory();
        string modDirectory = temp.CreateDirectory("active-only-mod");
        temp.WriteYaml(modDirectory, "fr", "example.active: Français only\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.False(service.RegisterMod("example", modDirectory));
        Assert.Equal("example.active", service.Get("example.active"));

        temp.WriteYaml(modDirectory, "en", "example.fallback: English only\n");

        Assert.True(service.RegisterMod("example", modDirectory));
        Assert.Equal("Français only", service.Get("example.active"));
        Assert.Equal("English only", service.Get("example.fallback"));
    }

    [Fact]
    public void RegisterMod_key_collision_is_atomic_and_retryable()
    {
        using var temp = new TemporaryDirectory();
        string ownerDirectory = temp.CreateDirectory("owner-mod");
        string childDirectory = temp.CreateDirectory("child-mod");
        temp.WriteYaml(ownerDirectory, "en", "owner.child.shared: original\n");
        temp.WriteYaml(
            childDirectory,
            "en",
            "owner.child.unique: staged\nowner.child.shared: replacement\n");

        var service = CreateService(temp.RootPath, "fr");

        Assert.True(service.RegisterMod("owner", ownerDirectory));
        Assert.False(service.RegisterMod("owner.child", childDirectory));
        Assert.Equal("original", service.Get("owner.child.shared"));
        Assert.Equal("owner.child.unique", service.Get("owner.child.unique"));

        temp.WriteYaml(childDirectory, "en", "owner.child.unique: committed\n");

        Assert.True(service.RegisterMod("owner.child", childDirectory));
        Assert.Equal("original", service.Get("owner.child.shared"));
        Assert.Equal("committed", service.Get("owner.child.unique"));
    }

    private static LocalizationService CreateService(string rootPath, string activeLocale)
    {
        return new LocalizationService(rootPath, typeof(LocalizationServiceTests).Assembly, activeLocale);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "Anomaly.Client.Api.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        internal string RootPath { get; }

        internal string CreateDirectory(string name)
        {
            string path = Path.Combine(RootPath, name);
            Directory.CreateDirectory(path);
            return path;
        }

        internal void WriteYaml(string directory, string locale, string contents)
        {
            File.WriteAllText(Path.Combine(directory, locale + ".yaml"), contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
