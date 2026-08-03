namespace Anomaly.Client.Api.Modding;

public sealed class ModDependency
{
    public string Id { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public string MaximumVersionExclusive { get; set; } = string.Empty;
    public bool Optional { get; set; }
}
