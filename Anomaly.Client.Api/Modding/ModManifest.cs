using System.Collections.Generic;

namespace Anomaly.Client.Api.Modding;

/// <summary>Sidecar metadata for an Anomaly-managed module assembly.</summary>
public sealed class ModManifest
{
    public int SchemaVersion { get; set; } = ApiContract.ManifestSchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    /// <summary>The API version against which this artifact was built.</summary>
    public string ApiVersion { get; set; } = ApiContract.CurrentVersion;
    /// <summary>Inclusive lower bound of framework API versions accepted by this mod.</summary>
    public string MinimumApiVersion { get; set; } = ApiContract.MinimumSupportedVersion;
    /// <summary>Exclusive upper bound of framework API versions accepted by this mod.</summary>
    public string MaximumApiVersionExclusive { get; set; } = ApiContract.MaximumSupportedVersionExclusive;
    public List<string> SupportedGameVersions { get; set; } = new();
    public List<ModDependency> Dependencies { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
    public ModCapability Capabilities { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
