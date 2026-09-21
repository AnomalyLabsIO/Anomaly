using System;
using System.Collections.Generic;
using System.Globalization;

namespace Anomaly.Client.Api.Compatibility;

/// <summary>
/// Evaluates Anomaly launcher/module compatibility metadata. A trailing caret
/// accepts the selected minor patch line: 14.2.7^ is &gt;=14.2.7 and &lt;14.3.0.
/// Expressions without a caret are exact three-component versions.
/// </summary>
public static class GameVersionCompatibility
{
    public static bool IsSupported(
        IEnumerable<string> expressions,
        int major,
        int minor,
        int revision)
    {
        var current = new Version(major, minor, revision);
        if (expressions == null)
            return false;
        foreach (string expression in expressions)
        {
            if (TryParse(expression, out Version minimum, out Version maximumExclusive) &&
                current >= minimum && current < maximumExclusive)
                return true;
        }
        return false;
    }

    public static bool TryParse(
        string expression,
        out Version minimum,
        out Version maximumExclusive)
    {
        minimum = null;
        maximumExclusive = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        string value = expression.Trim();
        bool caret = value.EndsWith("^", StringComparison.Ordinal);
        if (caret)
            value = value.Substring(0, value.Length - 1);
        string[] parts = value.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int revision) ||
            major < 0 || minor < 0 || revision < 0 ||
            (caret && minor == int.MaxValue) ||
            (!caret && revision == int.MaxValue))
            return false;

        minimum = new Version(major, minor, revision);
        maximumExclusive = caret
            ? new Version(major, minor + 1, 0)
            : new Version(major, minor, revision + 1);
        return true;
    }
}
