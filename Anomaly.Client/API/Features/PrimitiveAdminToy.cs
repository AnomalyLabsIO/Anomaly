using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class PrimitiveAdminToy : AdminToy
{
    internal PrimitiveAdminToy(PrimitiveObjectToy toyBase) : base(toyBase)
    {
        PrimitiveBase = toyBase;
    }

    public PrimitiveObjectToy PrimitiveBase { get; }

    public PrimitiveType PrimitiveType => PrimitiveBase.PrimitiveType;

    public Color Color => PrimitiveBase.MaterialColor;

    public PrimitiveFlags Flags => PrimitiveBase.PrimitiveFlags;
}
