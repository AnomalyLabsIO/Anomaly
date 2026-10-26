using Il2CppAdminToys;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class SpeakerAdminToy : AdminToy
{
    internal SpeakerAdminToy(SpeakerToy toyBase) : base(toyBase)
    {
        SpeakerBase = toyBase;
    }

    public SpeakerToy SpeakerBase { get; }

    public byte ControllerId => SpeakerBase.ControllerId;

    public bool IsSpatial => SpeakerBase.IsSpatial;

    public float Volume => SpeakerBase.Volume;

    public float MinDistance => SpeakerBase.MinDistance;

    public float MaxDistance => SpeakerBase.MaxDistance;
}
