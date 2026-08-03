using System.Collections.Generic;

namespace Anomaly.Client.Api.Modding;

public sealed class ModResolutionResult
{
    internal ModResolutionResult(List<ModManifest> ordered, List<string> errors)
    {
        Ordered = ordered;
        Errors = errors;
    }

    public IReadOnlyList<ModManifest> Ordered { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool Success => Errors.Count == 0;
}
