namespace Anomaly.Client.Api.Compatibility;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SupportedGameVersionAttribute(string version) : Attribute
{
    public string Version { get; } = version;
}