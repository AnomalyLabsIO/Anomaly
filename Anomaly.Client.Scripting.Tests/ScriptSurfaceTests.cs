using Anomaly.Client.Api.Features;
using Anomaly.Client.Scripting.Catalog;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptSurfaceTests
{
    [Fact]
    public void ProductionAnnotationsMatchReviewedScriptSurface()
    {
        using var runtime = new ScriptRuntime();
        runtime.DiscoverApi();
        var catalog = runtime.GetCatalog();
        var baseline = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ScriptAPI.Shipped.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(baseline, catalog.DescribeSurface());
        var docs = ScriptXmlDocumentation.Read(Path.ChangeExtension(typeof(Player).Assembly.Location, ".xml"));
        Assert.Equal(ReadExport("ScriptAPI.json"), catalog.ToJson(docs));
        Assert.Equal(ReadExport("Anomaly.Client.Api.d.lua"), catalog.ToLuaDefinitions(docs));
    }

    [Fact]
    public void ApiHasNoInterpreterOrScriptingImplementationDependency()
    {
        var references = typeof(Player).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.Contains("MoonSharp", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Name == "Anomaly.Client.Scripting" || reference.Name == "Anomaly.Client");
    }

    private static string ReadExport(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
}
