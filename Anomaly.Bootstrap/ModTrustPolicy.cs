namespace Anomaly.Bootstrap;

public sealed class ModTrustPolicy
{
    public int SchemaVersion { get; set; } = 1;
    public string Mode { get; set; } = "enforce";
    public List<string> ApprovedSha256 { get; set; } = new();
}

public sealed class ModTrustArtifact
{
    internal ModTrustArtifact(string fileName, string sha256, string originalPath, string? quarantinePath = null)
    {
        FileName = fileName;
        Sha256 = sha256;
        OriginalPath = originalPath;
        QuarantinePath = quarantinePath;
    }

    public string FileName { get; }
    public string Sha256 { get; }
    public string OriginalPath { get; }
    public string? QuarantinePath { get; }
}

public sealed class ModTrustResult
{
    public List<ModTrustArtifact> Approved { get; } = new();
    public List<ModTrustArtifact> Quarantined { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Failures { get; } = new();
}
