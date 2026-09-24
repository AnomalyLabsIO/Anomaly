// Value-only engine boundaries for the linked public placement contract.
// These tests exercise authoring lifetime and validation, not Unity generation.
namespace MapGeneration
{
    public enum FacilityZone { LightContainment = 1, HeavyContainment = 2, Entrance = 3 }
    public enum RoomName { LczArmory }
    public enum RoomShape { Straight }
}

namespace UnityEngine
{
    public struct Vector2Int
    {
        public int x;
        public int y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
