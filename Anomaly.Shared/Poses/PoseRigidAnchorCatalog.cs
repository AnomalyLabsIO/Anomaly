namespace Anomaly.Shared.Poses;

/// <summary>An authored model-space transform; unlike transmitted poses it is not limited by wire quantization.</summary>
public readonly struct PoseRigidAnchor
{
    internal PoseRigidAnchor(float x, float y, float z, float qx, float qy, float qz, float qw)
    { X = x; Y = y; Z = z; Qx = qx; Qy = qy; Qz = qz; Qw = qw; }
    /// <summary>Authored model-space position.</summary>
    public float X { get; }
    /// <summary>Authored model-space position.</summary>
    public float Y { get; }
    /// <summary>Authored model-space position.</summary>
    public float Z { get; }
    /// <summary>Authored model-space rotation.</summary>
    public float Qx { get; }
    /// <summary>Authored model-space rotation.</summary>
    public float Qy { get; }
    /// <summary>Authored model-space rotation.</summary>
    public float Qz { get; }
    /// <summary>Authored model-space rotation.</summary>
    public float Qw { get; }
}

/// <summary>Authored SCP:SL 14.2.7 fallback anchors for the stock root-attached primary hitboxes.</summary>
public static class PoseRigidAnchorCatalog
{
    /// <summary>Returns a stock neutral pelvis when mesh bind poses are unavailable. Never use an animated snapshot.</summary>
    public static bool TryGetHips(string rigId, out PoseRigidAnchor anchor)
    {
        switch (rigId)
        {
            case "anomaly.scp049":
                anchor = new PoseRigidAnchor(.000005f, 38.566914f, .5336095f, -.00000014142f, 0, 0, 1);
                return true;
            case "anomaly.scp096":
                anchor = new PoseRigidAnchor(0, 1.2038159f, -.0000003302f, -.065006584f, 0, 0, .99788486f);
                return true;
            default:
                anchor = default;
                return false;
        }
    }
}
