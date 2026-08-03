using Anomaly.Client.Api.Internal.Diagnostics;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class DiagnosticTextRedactorTests
{
    [Fact]
    public void Redacts_bearer_credentials_without_leaking_the_token_tail()
    {
        string result = DiagnosticTextRedactor.Redact(
            "Authorization: Bearer abc.def.ghi\nnext line",
            1024);

        Assert.Contains("authorization=[redacted]", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc.def.ghi", result, StringComparison.Ordinal);
        Assert.Contains("next line", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"failed in E:\Projects\Private Game\Secrets\Auth.cs:line 42")]
    [InlineData("connect private.example.test:7777 failed")]
    [InlineData("read /home/alice/private/config.json failed")]
    public void Redacts_non_profile_paths_and_hostname_endpoints(string input)
    {
        string result = DiagnosticTextRedactor.Redact(input, 1024);

        Assert.DoesNotContain("Private Game", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private.example.test", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/alice", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bounds_input_before_regex_processing()
    {
        string input = new string('x', 2_000_000) + " Authorization: Bearer should-not-be-read";

        string result = DiagnosticTextRedactor.Redact(input, 128);

        Assert.True(result.Length < 256);
        Assert.EndsWith("[truncated]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-be-read", result, StringComparison.Ordinal);
    }
}
