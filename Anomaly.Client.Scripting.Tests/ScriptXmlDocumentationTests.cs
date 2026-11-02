using Anomaly.Client.Scripting.Catalog;
using Xunit;

namespace Anomaly.Client.Scripting.Tests;

public sealed class ScriptXmlDocumentationTests
{
    [Fact]
    public void Empty_reference_elements_preserve_symbols_keywords_and_parameter_names()
    {
        var summary = ReadSummary("Uses <see cref='F:Il2CppPlayerRoles.RoleTypeId.None'/> or " +
            "<see langword='null'/> for <paramref name='playerId'/> and <typeparamref name='TValue'/>.");

        Assert.Equal("Uses Il2CppPlayerRoles.RoleTypeId.None or null for playerId and TValue.", summary);
    }

    [Theory]
    [InlineData("T:Example.Player", "Example.Player")]
    [InlineData("P:Example.Player.IsDestroyed", "Example.Player.IsDestroyed")]
    [InlineData("M:Example.Player.Get(System.Int32)", "Example.Player.Get(System.Int32)")]
    [InlineData("!:RoleTypeId.None", "RoleTypeId.None")]
    [InlineData("RoleTypeId.None", "RoleTypeId.None")]
    public void References_omit_documentation_identity_prefixes(string reference, string expected)
    {
        Assert.Equal(expected, ReadSummary("<see cref='" + reference + "'/>"));
    }

    [Fact]
    public void Explicit_labels_and_nested_summary_text_are_preserved_with_readable_spacing()
    {
        var summary = ReadSummary("<para>First <c>inline</c> text.</para><para>Second " +
            "<see cref='T:Example.Player'><c>player</c> label</see>.</para>" +
            "<list><item><term>Key</term><description>Value &amp; more.</description></item></list>" +
            "<br/><seealso cref='T:Example.Other'/>.");

        Assert.Equal("First inline text. Second player label. Key Value & more. Example.Other.", summary);
    }

    [Fact]
    public void Link_labels_take_precedence_and_unlabelled_links_retain_their_destination()
    {
        var summary = ReadSummary("<see href='https://example.invalid/guide'>the guide</see> and " +
            "<see href='https://example.invalid/reference'/>.");

        Assert.Equal("the guide and https://example.invalid/reference.", summary);
    }

    private static string ReadSummary(string summary)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<doc><members><member name='T:Example.Target'><summary>" +
                summary + "</summary></member></members></doc>");
            return ScriptXmlDocumentation.Read(path)["T:Example.Target"];
        }
        finally { File.Delete(path); }
    }
}
