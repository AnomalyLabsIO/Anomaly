namespace Anomaly.Client.Api.Modding;

/// <summary>Versioned contract exposed to Anomaly-managed modules.</summary>
public static class ApiContract
{
    public const int ManifestSchemaVersion = 1;
    public const string CurrentVersion = "1.0.0";
    public const string MinimumSupportedVersion = "1.0.0";
    public const string MaximumSupportedVersionExclusive = "2.0.0";
}
