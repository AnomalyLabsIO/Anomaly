using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class LightAdminToy : AdminToy
{
    internal LightAdminToy(LightSourceToy toyBase) : base(toyBase)
    {
        LightBase = toyBase;
    }

    public LightSourceToy LightBase { get; }

    public Light Light => LightBase._light;

    public float Intensity => LightBase.LightIntensity;

    public float Range => LightBase.LightRange;

    public Color Color => LightBase.LightColor;

    public LightShadows Shadows => LightBase.ShadowType;

    public float ShadowStrength => LightBase.ShadowStrength;

    public LightType LightType => LightBase.LightType;

    public float SpotAngle => LightBase.SpotAngle;

    public float InnerSpotAngle => LightBase.InnerSpotAngle;
}
