using System.Text;
using Anomaly.Client.Content;
using Anomaly.Shared.Scripting;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptDirectoryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Anomaly.ScriptDirectoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OwnerDirectoryGeneratesCanonicalAddressesHashesAndMessageContracts()
    {
        Write("Example/Panel/Nested/Client.lua", "function Value() return 1 end");
        Write("Example/Panel/messages.json", "{\"Messages\":[{\"Name\":\"State\",\"Direction\":\"ServerToClient\",\"Fields\":[]}]}");
        var source = Assert.Single(ScriptDirectoryReader.Read(_root));
        Assert.Equal("example:panel", source.PackId);
        Assert.Equal("mods/example/panel/nested/client.lua", source.Address);
        Assert.Equal(ScriptSourceValidator.HashHex(Encoding.UTF8.GetBytes(source.Source)), source.SourceSha256);
        Assert.Equal("State", Assert.Single(source.MessageSchemas!).Name);
        Write("Example/Panel/Nested/Client.lua", "return 2");
        Assert.Equal("function Value() return 1 end", source.Source);
    }

    [Fact]
    public void MissingOrEmptyDirectoryIsAnEmptySourceSet()
    {
        Assert.Empty(ScriptDirectoryReader.Read(_root));
        Directory.CreateDirectory(_root);
        Assert.Empty(ScriptDirectoryReader.Read(_root));
    }

    [Fact]
    public void CaseFoldedOwnerNamesWithDistinctPacksRemainDistinct()
    {
        Write("example/panel/Client.lua", "return 1");
        Write("EXAMPLE/other/client.lua", "return 2"); // Distinct pack; canonical identities remain distinct.
        Assert.Equal(2, ScriptDirectoryReader.Read(_root).Count);
    }

    [Theory]
    [InlineData("\ufeffreturn 1")]
    [InlineData("return '\0'")]
    public void InvalidTextRejectsTheWholeDirectory(string text)
    {
        Write("example/panel/main.lua", text);
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.Read(_root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceCountAndIndividualSizeAreBounded(bool count)
    {
        if (count) for (int number = 0; number <= ScriptPackLimits.MaxScripts; number++) Write("example/panel/" + number + ".lua", "");
        else Write("example/panel/main.lua", new string(' ', ScriptPackLimits.MaxScriptBytesPerFile + 1));
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.Read(_root));
    }

    [Fact]
    public void NonScriptFilesCannotCreateAnUnboundedDirectoryScan()
    {
        for (int number = 0; number < 257; number++) Write("example/panel/" + number + ".txt", "");
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.Read(_root));
    }

    [Fact]
    public void SourceReadRejectsAnExhaustedAggregateBudgetBeforeAllocatingTheFile()
    {
        Write("example/panel/main.lua", new string(' ', ScriptPackLimits.MaxScriptBytesPerFile));
        string path = Path.Combine(_root, "example/panel/main.lua");
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.ReadBounded(path, maximum: 0));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.ReadBounded(path, maximum: 0));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 * 1024, "Rejected source read allocated " + allocated + " bytes.");
    }

    [Fact]
    public void AggregateSourceBytesAreBoundedAcrossPacks()
    {
        for (int pack = 0; pack < 5; pack++)
            Write("example/pack" + pack + "/main.lua", new string(' ', ScriptPackLimits.MaxScriptBytesPerFile));
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.Read(_root));
    }

    [Fact]
    public void DescriptorCannotUseUnknownProperties()
    {
        Write("example/panel/main.lua", "");
        Write("example/panel/messages.json", "{\"Messages\":[],\"Other\":true}");
        Assert.Throws<InvalidDataException>(() => ScriptDirectoryReader.Read(_root));
    }

    private void Write(string path, string text)
    {
        string target = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text, new UTF8Encoding(false, true));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
