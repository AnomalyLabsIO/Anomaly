namespace Anomaly.Client.Api.Content;

public sealed class PassiveAssetReference
{
    public string Key { get; set; } = string.Empty;
    public PassiveAssetKind Kind { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
