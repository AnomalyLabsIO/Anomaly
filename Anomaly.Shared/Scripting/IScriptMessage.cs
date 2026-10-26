namespace Anomaly.Shared.Scripting;

/// <summary>Data envelope scoped to the exact connection activation and pack index.</summary>
public interface IScriptMessage
{
    string PackId { get; }
    string Name { get; }
    string IndexSha256 { get; }
    ulong ActivationId { get; }
    ScriptMessagePayload Payload { get; }
}
