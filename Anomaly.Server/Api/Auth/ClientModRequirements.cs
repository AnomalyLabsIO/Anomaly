using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Anomaly.Server.Auth;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Api.Auth;

/// <summary>
/// Declares client mods needed by ordinary LabAPI plugins before a player may join.
/// Keep the returned handle for the plugin's lifetime and dispose it from Disable().
/// </summary>
public static class ClientModRequirements
{
    private sealed class Registration : IDisposable
    {
        private readonly ClientModRequirement _requirement;
        private int _disposed;

        internal Registration(ClientModRequirement requirement) => _requirement = requirement;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (Sync)
            {
                Requirements.Remove(_requirement);
                PublishSnapshot();
            }
        }
    }

    private static readonly object Sync = new object();
    private static readonly List<ClientModRequirement> Requirements = new List<ClientModRequirement>();
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static ClientModRequirement[] _snapshot = Array.Empty<ClientModRequirement>();

    /// <summary>
    /// Requires a reported mod in the range [minimumVersion, maximumVersionExclusive).
    /// A null maximum has no upper bound. Versions must have two to four numeric
    /// components; prerelease and build suffixes are not accepted as bounds.
    /// Multiple plugins may register the same ID and each active rule is enforced.
    /// </summary>
    public static IDisposable Register(
        string modId,
        string minimumVersion,
        string maximumVersionExclusive = null)
    {
        if (!IsWireValidId(modId))
            throw new ArgumentException("A bounded, printable client mod ID is required.", nameof(modId));
        if (!TryParseStableVersion(minimumVersion, out var minimum))
            throw new ArgumentException("Minimum version must have two to four numeric components.", nameof(minimumVersion));

        Version maximum = null;
        if (maximumVersionExclusive != null &&
            !TryParseStableVersion(maximumVersionExclusive, out maximum))
            throw new ArgumentException("Maximum version must have two to four numeric components.", nameof(maximumVersionExclusive));
        if (maximum != null && maximum <= minimum)
            throw new ArgumentException("Maximum version must be greater than minimum version.", nameof(maximumVersionExclusive));

        var requirement = new ClientModRequirement(
            modId, minimumVersion, minimum, maximumVersionExclusive, maximum);
        lock (Sync)
        {
            Requirements.Add(requirement);
            PublishSnapshot();
        }
        ClientModAdmissionPolicy.WarnIfBlocked(modId);
        return new Registration(requirement);
    }

    internal static ClientModRequirement[] Snapshot() => Volatile.Read(ref _snapshot);

    internal static bool IsWireValidId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        foreach (char character in id)
        {
            if (char.IsControl(character))
                return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(id) <= ClientModInventory.MaxIdBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool TryParseStableVersion(string value, out Version version)
    {
        version = null;
        if (string.IsNullOrEmpty(value))
            return false;

        string[] parts = value.Split('.');
        if (parts.Length < 2 || parts.Length > 4)
            return false;

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private static void PublishSnapshot() =>
        Volatile.Write(ref _snapshot, Requirements.ToArray());
}

internal sealed class ClientModRequirement
{
    internal ClientModRequirement(
        string id,
        string minimumText,
        Version minimum,
        string maximumText,
        Version maximum)
    {
        Id = id;
        MinimumText = minimumText;
        Minimum = minimum;
        MaximumText = maximumText;
        Maximum = maximum;
    }

    internal string Id { get; }
    internal string MinimumText { get; }
    internal Version Minimum { get; }
    internal string MaximumText { get; }
    internal Version Maximum { get; }
    internal string Description => Maximum == null
        ? ">=" + MinimumText
        : "[" + MinimumText + ", " + MaximumText + ")";
}
