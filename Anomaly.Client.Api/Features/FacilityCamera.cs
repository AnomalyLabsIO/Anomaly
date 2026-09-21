using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppMapGeneration;
using Il2CppPlayerRoles.PlayableScps.Scp079.Cameras;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Wrapper around <see cref="Scp079Camera"/> — the in-facility CCTV cameras
/// that SCP-079 uses. Useful for SCP-079 spectator UIs, camera-feed mods,
/// minimap markers showing camera coverage, etc.
/// </summary>
/// <remarks>
/// <para>Named <c>FacilityCamera</c> rather than <c>Camera</c> to avoid the
/// inevitable collision with <see cref="UnityEngine.Camera"/> when both
/// types are in scope (which is essentially always for any rendering mod).
/// The wrapped IL2CPP type is <see cref="Scp079Camera"/>; the wrapper name
/// just steers consumers away from the ambiguous bare <c>Camera</c>.</para>
/// <para>Lifecycle is driven by the engine's static
/// <see cref="Scp079Camera.OnInstanceCreated"/> /
/// <see cref="Scp079Camera.OnInstanceRemoved"/> events; the wrapper uses
/// lazy creation via <see cref="Get"/>.</para>
/// </remarks>
public class FacilityCamera
{
    private static readonly Dictionary<Scp079Camera, FacilityCamera> _cameras = new();

    private FacilityCamera(Scp079Camera cameraBase)
    {
        Base = cameraBase;
    }

    /// <summary>The underlying <see cref="Scp079Camera"/>.</summary>
    public Scp079Camera Base { get; }

    /// <summary>True if this is the room's "main" camera (the one SCP-079 enters by default for the room).</summary>
    public bool IsMain => Base != null && Base.IsMain;

    /// <summary>Display label for the camera (e.g. "Heavy Containment Zone Camera 7").</summary>
    public string Label => Base?.Label;

    /// <summary>True if SCP-079 is currently viewing through this camera.</summary>
    public bool IsActive => Base != null && Base.IsActive;

    /// <summary>True if the local player is the SCP-079 currently using this camera.</summary>
    public bool IsUsedByLocalPlayer => Base != null && Base.IsUsedByLocalPlayer;

    /// <summary>The camera's world position (where SCP-079 sees from).</summary>
    public Vector3 CameraPosition => Base != null ? Base.CameraPosition : Vector3.zero;

    public float VerticalRotation => Base != null ? Base.VerticalRotation : 0f;
    public float HorizontalRotation => Base != null ? Base.HorizontalRotation : 0f;
    public float RollRotation => Base != null ? Base.RollRotation : 0f;

    public Transform Anchor => Base?.CameraAnchor;
    public GameObject GameObject => Base != null ? Base.gameObject : null;

    /// <summary>All cameras currently in the scene. Built fresh each call via <c>UnityEngine.Object.FindObjectsOfType&lt;Scp079Camera&gt;()</c>.</summary>
    public static ReadOnlyCollection<FacilityCamera> List
    {
        get
        {
            List<FacilityCamera> result = new();
            foreach (Scp079Camera cam in UnityEngine.Object.FindObjectsOfType<Scp079Camera>())
            {
                FacilityCamera wrapper = Get(cam);
                if (wrapper != null)
                    result.Add(wrapper);
            }
            return result.AsReadOnly();
        }
    }

    /// <summary>All cameras placed in <paramref name="room"/>.</summary>
    public static ReadOnlyCollection<FacilityCamera> GetForRoom(RoomIdentifier room)
    {
        List<FacilityCamera> result = new();
        if (room == null) return result.AsReadOnly();

        try
        {
            var cams = Scp079Camera.GetRoomCameras(room);
            if (cams != null)
            {
                foreach (Scp079Camera cam in cams)
                {
                    FacilityCamera wrapper = Get(cam);
                    if (wrapper != null)
                        result.Add(wrapper);
                }
            }
        }
        catch { /* room may not have cameras */ }

        return result.AsReadOnly();
    }

    public static FacilityCamera Get(Scp079Camera cameraBase)
    {
        if (cameraBase == null) return null;
        if (_cameras.TryGetValue(cameraBase, out FacilityCamera wrapper))
            return wrapper;
        wrapper = new FacilityCamera(cameraBase);
        _cameras[cameraBase] = wrapper;
        return wrapper;
    }

    internal static bool Unregister(Scp079Camera cameraBase) =>
        cameraBase != null && _cameras.Remove(cameraBase);

    internal static void ClearAll() => _cameras.Clear();

    public override string ToString() => $"FacilityCamera({Label}, IsActive={IsActive})";
}
