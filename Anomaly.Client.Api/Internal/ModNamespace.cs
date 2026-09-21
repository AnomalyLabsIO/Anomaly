using System;
using System.Text.RegularExpressions;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Format validator for mod-supplied namespace strings used as prefixes on
/// commands, input bindings, cooldown keys, config/persistence store ids, and
/// translation namespaces.
/// </summary>
/// <remarks>
/// Anomaly does not maintain a global registry of mods; it enforces only that
/// caller-supplied ids look like a namespace so two mods using distinct ids
/// can't collide by accident. The <c>anomaly</c> / <c>anomaly.</c> prefix is
/// reserved for the framework's own built-ins. Public registrations reject it;
/// framework commands use a separate internal registration boundary.
/// </remarks>
internal static class ModNamespace
{
    // Lowercase dotted segments, each alphanumeric or underscore, 1-40 segments,
    // each segment 1-32 chars, starting with a letter.
    private static readonly Regex Pattern = new Regex(
        @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns true if <paramref name="id"/> is a syntactically valid namespace
    /// or dotted key (e.g. <c>mymod</c>, <c>mymod.feature.thing</c>).
    /// </summary>
    internal static bool IsValid(string id) =>
        !string.IsNullOrEmpty(id) && Pattern.IsMatch(id);

    /// <summary>
    /// Returns true if <paramref name="id"/> is in the reserved <c>anomaly</c> /
    /// <c>anomaly.*</c> namespace used by the framework's own registrations.
    /// </summary>
    internal static bool IsReserved(string id) =>
        id == "anomaly" || (id != null && id.StartsWith("anomaly.", StringComparison.Ordinal));
}
