using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Anomaly.Client.Api.Compatibility;

namespace Anomaly.Client.Api.Modding;

public static class ModManifestValidator
{
    private static readonly Regex IdPattern = new(
        @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HashPattern = new(
        @"^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(ModManifest manifest)
    {
        var errors = new List<string>();
        if (manifest == null)
        {
            errors.Add("Manifest is null.");
            return errors;
        }

        if (manifest.SchemaVersion != ApiContract.ManifestSchemaVersion)
            errors.Add($"Unsupported manifest schema {manifest.SchemaVersion}.");
        if (!IsValidId(manifest.Id) || IsReservedId(manifest.Id))
            errors.Add($"Invalid or reserved mod id '{manifest.Id}'.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("Name is required.");
        if (!TryParseVersion(manifest.Version, out _))
            errors.Add($"Invalid mod version '{manifest.Version}'.");
        if (!TryParseVersion(manifest.ApiVersion, out var apiVersion) || apiVersion.Major != 1)
            errors.Add($"Unsupported API version '{manifest.ApiVersion}'.");
        if (!TryParseVersion(manifest.MinimumApiVersion, out var minimumApiVersion))
            errors.Add($"Invalid minimum API version '{manifest.MinimumApiVersion}'.");
        if (!TryParseVersion(manifest.MaximumApiVersionExclusive, out var maximumApiVersion))
            errors.Add($"Invalid exclusive maximum API version '{manifest.MaximumApiVersionExclusive}'.");
        if (minimumApiVersion != null && maximumApiVersion != null && minimumApiVersion >= maximumApiVersion)
            errors.Add("The declared API compatibility range is empty.");
        if (apiVersion != null && minimumApiVersion != null && maximumApiVersion != null &&
            (apiVersion < minimumApiVersion || apiVersion >= maximumApiVersion))
            errors.Add("The build API version is outside the declared API compatibility range.");
        if (TryParseVersion(ApiContract.CurrentVersion, out var currentApiVersion) &&
            minimumApiVersion != null && maximumApiVersion != null &&
            (currentApiVersion < minimumApiVersion || currentApiVersion >= maximumApiVersion))
            errors.Add($"Framework API {ApiContract.CurrentVersion} is outside the mod's declared compatibility range.");
        if (!HashPattern.IsMatch(manifest.Sha256 ?? string.Empty))
            errors.Add("Sha256 must contain exactly 64 hexadecimal characters.");

        var gameVersions = manifest.SupportedGameVersions ?? new List<string>();
        if (gameVersions.Count == 0)
            errors.Add("At least one Anomaly SupportedGameVersion expression is required.");
        var gameVersionExpressions = new HashSet<string>(StringComparer.Ordinal);
        foreach (string expression in gameVersions)
        {
            if (!GameVersionCompatibility.TryParse(expression, out _, out _))
                errors.Add($"Invalid SupportedGameVersion expression '{expression}'.");
            else if (!gameVersionExpressions.Add(expression))
                errors.Add($"SupportedGameVersion expression '{expression}' is duplicated.");
        }

        var dependencyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in manifest.Dependencies ?? new List<ModDependency>())
        {
            if (dependency == null || !IsValidId(dependency.Id) || IsReservedId(dependency.Id))
            {
                errors.Add("A dependency has an invalid or reserved id.");
                continue;
            }
            if (!dependencyIds.Add(dependency.Id))
                errors.Add($"Dependency '{dependency.Id}' is declared more than once.");
            if (!string.IsNullOrEmpty(dependency.MinimumVersion) &&
                !TryParseVersion(dependency.MinimumVersion, out _))
                errors.Add($"Dependency '{dependency.Id}' has an invalid minimum version.");
            if (!string.IsNullOrEmpty(dependency.MaximumVersionExclusive) &&
                !TryParseVersion(dependency.MaximumVersionExclusive, out _))
                errors.Add($"Dependency '{dependency.Id}' has an invalid exclusive maximum version.");
            if (TryParseVersion(dependency.MinimumVersion, out var minimum) &&
                TryParseVersion(dependency.MaximumVersionExclusive, out var maximum) &&
                minimum >= maximum)
                errors.Add($"Dependency '{dependency.Id}' has an empty version range.");
        }

        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflict in manifest.Conflicts ?? new List<string>())
        {
            if (!IsValidId(conflict) || IsReservedId(conflict))
                errors.Add($"Conflict id '{conflict}' is invalid or reserved.");
            else if (!conflicts.Add(conflict))
                errors.Add($"Conflict '{conflict}' is declared more than once.");
            if (string.Equals(conflict, manifest.Id, StringComparison.Ordinal))
                errors.Add("A mod cannot conflict with itself.");
        }

        return errors;
    }

    public static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);

    public static bool IsReservedId(string id) =>
        id == "anomaly" || (id?.StartsWith("anomaly.", StringComparison.Ordinal) ?? false);

    internal static bool TryParseVersion(string value, out Version version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var core = value.Trim();
        var suffix = core.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0) core = core.Substring(0, suffix);
        if (!Version.TryParse(core, out version)) return false;
        return version.Major >= 0 && version.Minor >= 0 && version.Build >= 0;
    }
}
