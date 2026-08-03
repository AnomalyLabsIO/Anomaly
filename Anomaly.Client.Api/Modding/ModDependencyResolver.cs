using System;
using System.Collections.Generic;
using System.Linq;

namespace Anomaly.Client.Api.Modding;

/// <summary>Deterministic dependency/conflict resolver for module manifests.</summary>
public static class ModDependencyResolver
{
    public static ModResolutionResult Resolve(IEnumerable<ModManifest> manifests)
    {
        var errors = new List<string>();
        var byId = new Dictionary<string, ModManifest>(StringComparer.Ordinal);

        foreach (var manifest in manifests ?? Array.Empty<ModManifest>())
        {
            var validation = ModManifestValidator.Validate(manifest);
            foreach (var error in validation)
                errors.Add($"{manifest?.Id ?? "<unknown>"}: {error}");
            if (manifest == null || !ModManifestValidator.IsValidId(manifest.Id)) continue;
            if (!byId.TryAdd(manifest.Id, manifest))
                errors.Add($"Duplicate mod id '{manifest.Id}'.");
        }

        foreach (var manifest in byId.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            foreach (var conflict in manifest.Conflicts ?? new List<string>())
                if (byId.ContainsKey(conflict))
                    errors.Add($"'{manifest.Id}' conflicts with installed mod '{conflict}'.");

            foreach (var dependency in manifest.Dependencies ?? new List<ModDependency>())
            {
                if (!byId.TryGetValue(dependency.Id, out var installed))
                {
                    if (!dependency.Optional)
                        errors.Add($"'{manifest.Id}' requires missing mod '{dependency.Id}'.");
                    continue;
                }

                if (!Satisfies(installed.Version, dependency))
                    errors.Add($"'{manifest.Id}' requires '{dependency.Id}' in range " +
                               $"[{dependency.MinimumVersion}, {dependency.MaximumVersionExclusive}), " +
                               $"but {installed.Version} is installed.");
            }
        }

        if (errors.Count > 0)
            return new ModResolutionResult(new List<ModManifest>(), errors);

        var ordered = new List<ModManifest>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();

        foreach (var id in byId.Keys.OrderBy(x => x, StringComparer.Ordinal))
            Visit(id, byId, state, stack, ordered, errors);

        return errors.Count == 0
            ? new ModResolutionResult(ordered, errors)
            : new ModResolutionResult(new List<ModManifest>(), errors);
    }

    private static void Visit(
        string id,
        IReadOnlyDictionary<string, ModManifest> byId,
        IDictionary<string, int> state,
        Stack<string> stack,
        ICollection<ModManifest> ordered,
        ICollection<string> errors)
    {
        if (state.TryGetValue(id, out var current))
        {
            if (current == 2) return;
            if (current == 1)
            {
                errors.Add("Dependency cycle: " + string.Join(" -> ", stack.Reverse()) + " -> " + id);
                return;
            }
        }

        state[id] = 1;
        stack.Push(id);
        var dependencies = (byId[id].Dependencies ?? new List<ModDependency>())
            .Where(x => byId.ContainsKey(x.Id))
            .OrderBy(x => x.Id, StringComparer.Ordinal);
        foreach (var dependency in dependencies)
            Visit(dependency.Id, byId, state, stack, ordered, errors);
        stack.Pop();
        state[id] = 2;
        ordered.Add(byId[id]);
    }

    private static bool Satisfies(string installedVersion, ModDependency dependency)
    {
        if (!ModManifestValidator.TryParseVersion(installedVersion, out var installed)) return false;
        if (ModManifestValidator.TryParseVersion(dependency.MinimumVersion, out var minimum) &&
            installed < minimum) return false;
        if (ModManifestValidator.TryParseVersion(dependency.MaximumVersionExclusive, out var maximum) &&
            installed >= maximum) return false;
        return true;
    }
}
