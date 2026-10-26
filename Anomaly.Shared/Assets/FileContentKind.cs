namespace Anomaly.Shared.Assets;

/// <summary>Authenticated transfer payloads with distinct validation policies.</summary>
public enum FileContentKind : byte
{
    Bundle = 1,
    PackIndex = 2,
    ScriptIndex = 3,
    LuaSource = 4,
}
