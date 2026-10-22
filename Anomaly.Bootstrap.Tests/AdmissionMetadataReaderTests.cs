using Anomaly.Bootstrap;
using System.Reflection;
using MelonLoader;
using Mono.Cecil;
using Xunit;

[assembly: MelonInfo(typeof(Anomaly.Bootstrap.Tests.MetadataFixtureMod), "metadata.fixture", "1.2.3", "Test")]
[assembly: MelonInfo(typeof(Anomaly.Bootstrap.Tests.MetadataFixturePlugin), "plugin.fixture", "1.0", "Test")]
[assembly: MelonID("immutable.fixture.id")]

namespace Anomaly.Bootstrap.Tests;

public sealed class AdmissionMetadataReaderTests
{
    [Fact]
    public void RawMetadataIsReadWithoutConstructingTheModAndExcludesPlugins()
    {
        var claims = AdmissionMetadataReader.Read(typeof(AdmissionMetadataReaderTests).Assembly);
        var claim = Assert.Single(claims);
        Assert.Equal(typeof(MetadataFixtureMod), claim.Type);
        Assert.Equal("immutable.fixture.id", claim.Id);
        Assert.Equal("1.2.3", claim.Version);
        Assert.Equal(0, MetadataFixtureCounters.Initializations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMalformedDeclarationsAreDistinguishedFromGeneralReflectionFailures(bool externalType)
    {
        using var definition = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("malformed-metadata-" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "fixture", ModuleKind.Dll);
        var constructor = typeof(MelonInfoAttribute).GetConstructor(new[]
            { typeof(Type), typeof(string), typeof(string), typeof(string), typeof(string) })!;
        var attribute = new CustomAttribute(definition.MainModule.ImportReference(constructor));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(definition.MainModule.ImportReference(typeof(Type)),
            externalType ? definition.MainModule.ImportReference(typeof(MetadataFixtureMod)) : null));
        foreach (string? value in new[] { "fixture", "1.0", "Test", null })
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(definition.MainModule.TypeSystem.String, value));
        definition.CustomAttributes.Add(attribute);
        using var stream = new MemoryStream();
        definition.Write(stream);

        var assembly = Assembly.Load(stream.ToArray());
        Assert.Throws<InvalidAdmissionMetadataException>(() => AdmissionMetadataReader.Read(assembly));
        Assert.Equal(0, MetadataFixtureCounters.Initializations);
    }
}

public static class MetadataFixtureCounters
{
    public static int Initializations;
}

public sealed class MetadataFixtureMod : MelonMod
{
    static MetadataFixtureMod() => MetadataFixtureCounters.Initializations++;
    public MetadataFixtureMod() => MetadataFixtureCounters.Initializations++;
}

public sealed class MetadataFixturePlugin : MelonPlugin { }
