using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class AssetOverrideClearedEventArgs : EventArgs
{
    public AssetOverrideClearedEventArgs(uint netId, string componentPath, int materialIndex)
    {
        NetId = netId;
        ComponentPath = componentPath;
        MaterialIndex = materialIndex;
    }

    [ScriptVisible]
    public uint NetId { get; }
    [ScriptVisible]
    public string ComponentPath { get; }
    [ScriptVisible]
    public int MaterialIndex { get; }
}
