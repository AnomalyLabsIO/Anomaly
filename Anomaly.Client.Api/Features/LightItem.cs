using Il2CppInventorySystem.Items.ToggleableLights;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>A toggleable held light, including flashlights and lanterns.</summary>
[ScriptType]
public class LightItem : Item
{
    internal LightItem(ToggleableLightItemBase lightBase) : base(lightBase)
    {
        LightBase = lightBase;
    }

    public ToggleableLightItemBase LightBase { get; }

    /// <summary>Current client-observed light state. This does not change the server's light state.</summary>
    public bool IsEmittingLight => LightBase.IsEmittingLight;
}
