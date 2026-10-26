using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Detached coordinates; never a Unity Transform or writable native vector.</summary>
[ScriptType("Position")]
internal sealed class ScriptPosition
{
    internal ScriptPosition(float x, float y, float z) { X = x; Y = y; Z = z; }
    [ScriptVisible] public float X { get; }
    [ScriptVisible] public float Y { get; }
    [ScriptVisible] public float Z { get; }
}
