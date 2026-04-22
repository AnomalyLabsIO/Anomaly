using System.Globalization;

namespace Anomaly.InstallerCore.Patching;

/// <summary>
/// Game version in the form Major.Minor.Revision. Ordering is lexical by component.
/// The literal string "Current" (case-insensitive) is treated as the maximum version for use
/// as an open-ended upper bound in <see cref="VersionRange.versionTo"/>.
/// </summary>
public readonly record struct GameVersion(int Major, int Minor, int Revision) : IComparable<GameVersion>
{
    public static readonly GameVersion Max = new(int.MaxValue, int.MaxValue, int.MaxValue);

    public int CompareTo(GameVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Revision.CompareTo(other.Revision);
    }

    public override string ToString() => $"{Major}.{Minor}.{Revision}";

    public static bool TryParse(string? s, out GameVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        if (string.Equals(s.Trim(), "Current", StringComparison.OrdinalIgnoreCase))
        {
            version = Max;
            return true;
        }

        var parts = s.Trim().Split('.');
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maj))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
            return false;

        int rev = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out rev))
            return false;

        version = new GameVersion(maj, min, rev);
        return true;
    }

    public static GameVersion Parse(string s)
    {
        if (!TryParse(s, out var v))
            throw new FormatException($"Invalid game version '{s}'");
        return v;
    }
}

public static class VersionRangeExtensions
{
    /// <summary>
    /// Returns true if <paramref name="version"/> falls inside <paramref name="range"/>'s inclusive
    /// [versionFrom, versionTo] bracket. A null or "Current" versionTo is treated as open-ended.
    /// </summary>
    public static bool Contains(this VersionRange range, GameVersion version)
    {
        if (!GameVersion.TryParse(range.versionFrom, out var from))
            return false;

        GameVersion to;
        if (string.IsNullOrWhiteSpace(range.versionTo))
            to = GameVersion.Max;
        else if (!GameVersion.TryParse(range.versionTo, out to))
            return false;

        return version.CompareTo(from) >= 0 && version.CompareTo(to) <= 0;
    }
}
