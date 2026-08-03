using System.Text.RegularExpressions;

namespace Anomaly.Client.Api.Internal.Diagnostics;

/// <summary>
/// Fail-closed text sanitization shared by local diagnostic writers. Input is
/// bounded before regex processing so an attacker-controlled log or exception
/// cannot turn crash handling into unbounded CPU or memory work.
/// </summary>
internal static class DiagnosticTextRedactor
{
    private const int RegexLookaheadCharacters = 4096;
    private const int MaximumRegexInputCharacters = 128 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex CredentialPattern = CreateRegex(
        @"(?im)\b(password|passwd|passphrase|token|secret|authorization|proxy-authorization|api[_-]?key|session(?:id)?|cookie|identity(?:base64)?|private[_-]?key|client[_-]?secret|user(?:name)?|displayname)\b\s*[:=]\s*[^\r\n]*");
    private static readonly Regex WindowsPathPattern = CreateRegex(
        @"(?im)(?<![A-Za-z0-9_])(?:[A-Z]:\\|\\\\)[^\r\n""'<>|]*");
    private static readonly Regex UnixPathPattern = CreateRegex(
        @"(?im)(?<![A-Za-z0-9_.])/(?:home|Users|tmp|var|opt|mnt|private|Volumes)/[^\r\n]*");
    private static readonly Regex Ipv4Pattern = CreateRegex(
        @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?![\d.])");
    private static readonly Regex Ipv6Pattern = CreateRegex(
        @"(?i)(?<![0-9a-f:])(?:[0-9a-f]{1,4}:){2,7}[0-9a-f]{0,4}(?:%[\w.-]+)?(?::\d{1,5})?(?![0-9a-f:])");
    private static readonly Regex HostEndpointPattern = CreateRegex(
        @"(?i)\b(?:localhost|(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63})(?::\d{1,5})?\b");
    private static readonly Regex PlatformUserIdPattern = CreateRegex(
        @"(?i)(?<![\w.-])[^\s()]{1,64}@(steam|northwood|discord)(?![\w.-])|\b7656119\d{10}\b");

    internal static string Redact(string value, int maximumOutputCharacters)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (maximumOutputCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters));

        int preLimit = Math.Min(
            MaximumRegexInputCharacters,
            checked(maximumOutputCharacters + RegexLookaheadCharacters));
        string bounded = value.Length <= preLimit ? value : value.Substring(0, preLimit);

        try
        {
            string redacted = ReplaceEnvironmentValues(bounded);
            redacted = CredentialPattern.Replace(redacted, "$1=[redacted]");
            redacted = WindowsPathPattern.Replace(redacted, "[redacted-path]");
            redacted = UnixPathPattern.Replace(redacted, "[redacted-path]");
            redacted = Ipv4Pattern.Replace(redacted, "[redacted-ip]");
            redacted = Ipv6Pattern.Replace(redacted, "[redacted-ip]");
            redacted = HostEndpointPattern.Replace(redacted, "[redacted-server-endpoint]");
            redacted = PlatformUserIdPattern.Replace(redacted, "[redacted-platform-user]");
            return redacted.Length <= maximumOutputCharacters
                ? redacted
                : redacted.Substring(0, maximumOutputCharacters) + "\n[truncated]";
        }
        catch (RegexMatchTimeoutException)
        {
            // Diagnostic privacy is fail-closed. If sanitization cannot finish
            // within its strict budget, retain no attacker-controlled text.
            return "[redacted-diagnostic-field: regex budget exceeded]";
        }
    }

    private static string ReplaceEnvironmentValues(string value)
    {
        string redacted = value;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            redacted = redacted.Replace(profile, "[redacted-user-profile]", StringComparison.OrdinalIgnoreCase);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            redacted = redacted.Replace(appData, "[redacted-appdata]", StringComparison.OrdinalIgnoreCase);
        string userName = Environment.UserName;
        if (!string.IsNullOrEmpty(userName))
        {
            redacted = Regex.Replace(
                redacted,
                @"(?i)(?<![A-Za-z0-9_])" + Regex.Escape(userName) + @"(?![A-Za-z0-9_])",
                "[redacted-user]",
                RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        return redacted;
    }

    private static Regex CreateRegex(string pattern) => new Regex(
        pattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
}
