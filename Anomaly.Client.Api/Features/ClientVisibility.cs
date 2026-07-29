using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// A camera-backed culling viewpoint. The camera may remain disabled; only its
/// transform and projection data are sampled.
/// </summary>
public readonly struct VisibilityViewpoint
{
    public VisibilityViewpoint(Camera camera, float maxDistance = 0f)
    {
        Camera = camera;
        MaxDistance = maxDistance;
    }

    public Camera Camera { get; }
    public float MaxDistance { get; }
    public bool IsValid => Camera != null;
}

/// <summary>
/// Adds auxiliary frusta to SCP:SL's normal observer for systems such as
/// mirrors, security displays, and portals. A lease never creates or attaches
/// another stock <c>CullingCamera</c>.
/// </summary>
public static class ClientVisibility
{
    private sealed class Lease : IDisposable
    {
        private readonly PlaneData[] _planes = new PlaneData[6];
        private readonly Il2CppStructArray<Plane> _unityPlanes = new(6);
        private int _cachedFrame = -1;
        private bool _valid;
        private Vector3 _origin;
        private float _maxDistanceSqr;
        private bool _disposed;

        internal Lease(int id, string ownerId, Func<VisibilityViewpoint> provider)
        {
            Id = id;
            OwnerId = ownerId;
            Provider = provider;
        }

        internal int Id { get; }
        internal string OwnerId { get; }
        internal Func<VisibilityViewpoint> Provider { get; }

        internal bool IsVisible(Bounds bounds)
        {
            Refresh();
            if (!_valid) return false;
            if (bounds.SqrDistance(_origin) > _maxDistanceSqr) return false;

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int i = 0; i < _planes.Length; i++)
            {
                PlaneData plane = _planes[i];
                float radius =
                    extents.x * Mathf.Abs(plane.Normal.x) +
                    extents.y * Mathf.Abs(plane.Normal.y) +
                    extents.z * Mathf.Abs(plane.Normal.z);
                float distance = Vector3.Dot(plane.Normal, center) + plane.Distance;
                if (distance + radius < 0f)
                    return false;
            }

            return true;
        }

        private void Refresh()
        {
            int frame = Time.frameCount;
            if (_cachedFrame == frame) return;
            _cachedFrame = frame;
            _valid = false;

            VisibilityViewpoint viewpoint;
            try
            {
                viewpoint = Provider();
            }
            catch
            {
                return;
            }

            Camera camera = viewpoint.Camera;
            if (camera == null) return;

            Vector3 origin;
            float far;
            try
            {
                origin = camera.transform.position;
                far = viewpoint.MaxDistance > 0f ? viewpoint.MaxDistance : camera.farClipPlane;
                GeometryUtility.CalculateFrustumPlanes(camera, _unityPlanes);
            }
            catch
            {
                return;
            }

            if (!IsFinite(origin) || !float.IsFinite(far) || far <= 0f)
                return;

            for (int i = 0; i < _planes.Length; i++)
            {
                Plane plane = _unityPlanes[i];
                Vector3 normal = plane.normal;
                if (!SetPlane(i, normal.x, normal.y, normal.z, plane.distance))
                    return;
            }

            _origin = origin;
            _maxDistanceSqr = far * far;
            _valid = true;
        }

        private bool SetPlane(int index, float x, float y, float z, float distance)
        {
            float length = Mathf.Sqrt(x * x + y * y + z * z);
            if (length <= 0.000001f || !float.IsFinite(length))
            {
                _planes[index] = default;
                return false;
            }

            float inverse = 1f / length;
            _planes[index] = new PlaneData(
                new Vector3(x * inverse, y * inverse, z * inverse),
                distance * inverse);
            return true;
        }

        internal void RefreshForCullingCycle()
        {
            Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(Id);
        }
    }

    private readonly struct PlaneData
    {
        internal PlaneData(Vector3 normal, float distance)
        {
            Normal = normal;
            Distance = distance;
        }

        internal Vector3 Normal { get; }
        internal float Distance { get; }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<int, Lease> Leases = new();
    private static volatile Lease[] _snapshot = Array.Empty<Lease>();
    private static int _nextId;

    public static IDisposable AcquireViewpoint(string ownerId, Func<VisibilityViewpoint> provider)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("A non-empty visibility lease owner id is required.", nameof(ownerId));
        if (ownerId.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(ownerId), "Visibility lease owner id cannot exceed 128 characters.");
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        lock (Sync)
        {
            int id = unchecked(++_nextId);
            if (id == 0) id = unchecked(++_nextId);
            var lease = new Lease(id, ownerId, provider);
            Leases.Add(id, lease);
            RebuildSnapshotLocked();
            return lease;
        }
    }

    public static void ReleaseOwner(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId)) return;

        lock (Sync)
        {
            var ids = new List<int>();
            foreach (KeyValuePair<int, Lease> pair in Leases)
            {
                if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.Ordinal))
                    ids.Add(pair.Key);
            }

            for (int i = 0; i < ids.Count; i++)
                Leases.Remove(ids[i]);
            if (ids.Count > 0)
                RebuildSnapshotLocked();
        }
    }

    internal static bool IsVisible(Bounds bounds)
    {
        Lease[] snapshot = _snapshot;
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].IsVisible(bounds))
                return true;
        }

        return false;
    }

    internal static void RefreshForCullingCycle()
    {
        Lease[] snapshot = _snapshot;
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i].RefreshForCullingCycle();
    }

    internal static void ReleaseAll()
    {
        lock (Sync)
        {
            Leases.Clear();
            _snapshot = Array.Empty<Lease>();
        }
    }

    private static void Release(int id)
    {
        lock (Sync)
        {
            if (Leases.Remove(id))
                RebuildSnapshotLocked();
        }
    }

    private static void RebuildSnapshotLocked()
    {
        var snapshot = new Lease[Leases.Count];
        Leases.Values.CopyTo(snapshot, 0);
        _snapshot = snapshot;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }
}
