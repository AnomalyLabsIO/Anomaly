using System.Collections.Generic;
using System.Linq;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Poses;

/// <summary>Preserves complete authorizations for every mapped ancestor of a gameplay hitbox.</summary>
internal static class PoseProjectionPolicy
{
    internal static PoseFrame Project(PoseFrame frame, ISet<ushort> relevant)
    {
        if (frame == null || relevant == null) return frame;
        return new PoseFrame(frame.RigId, frame.SchemaVersion, frame.Bones.Where(bone => relevant.Contains(bone.BoneId)));
    }
}
