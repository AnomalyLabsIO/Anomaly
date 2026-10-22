using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Shared.Poses;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Main-thread capture-only bindings. Never constructs or shares a posing binding.</summary>
internal static class PoseCaptureCache
{
    private sealed class Binding
    {
        internal Player Player;
        internal AnimatedCharacterModel Model;
        internal PoseRigDescriptor Rig;
        internal int Life;
        internal Transform[] Bones;
        internal ushort[] Ids;
        internal HashSet<ushort> KnownIds;
        internal PoseBone[] Output;

        internal bool IsCurrent
        {
            get
            {
                if (Player == null || Player.IsDestroyed || Model == null || Model.Pooled || Model.Animator == null ||
                    Player.CharacterModel != Model || Player.CurrentRole.UniqueLifeIdentifier != Life ||
                    !PoseRigCatalog.TryGetForRole((sbyte)Player.RoleType, out var rig) ||
                    !ReferenceEquals(rig, Rig) || rig.Id != Rig.Id || rig.Version != Rig.Version ||
                    Model.name.Replace("(Clone)", string.Empty).Trim() != Rig.ModelName) return false;
                foreach (var bone in Bones) if (bone == null) return false;
                return true;
            }
        }

        internal static Binding Create(Player player)
        {
            if (player == null || player.IsDestroyed ||
                !PoseRigCatalog.TryGetForRole((sbyte)player.RoleType, out var rig)) return null;
            var model = player.CharacterModel?.TryCast<AnimatedCharacterModel>();
            if (model == null || model.Pooled || model.Animator == null ||
                model.name.Replace("(Clone)", string.Empty).Trim() != rig.ModelName) return null;
            var mapped = new List<(ushort Id, Transform Bone, int Depth)>();
            var seen = new HashSet<int>();
            foreach (var descriptor in rig.Bones)
            {
                var bone = descriptor.Path != null ? model.transform.Find(descriptor.Path) :
                    model.Animator.GetBoneTransform((HumanBodyBones)descriptor.HumanoidBone);
                if (bone == null || !seen.Add(bone.GetInstanceID())) return null;
                int depth = 0;
                for (var current = bone; current != null && current != model.transform; current = current.parent) depth++;
                mapped.Add((descriptor.Id, bone, depth));
            }
            // Retain the old depth/ID traversal; PoseFrame itself sorts the detached result by ID.
            mapped.Sort((a, b) => { int result = a.Depth.CompareTo(b.Depth); return result != 0 ? result : a.Id.CompareTo(b.Id); });
            return new Binding
            {
                Player = player, Model = model, Rig = rig, Life = player.CurrentRole.UniqueLifeIdentifier,
                Bones = mapped.Select(b => b.Bone).ToArray(), Ids = mapped.Select(b => b.Id).ToArray(),
                KnownIds = mapped.Select(b => b.Id).ToHashSet(), Output = new PoseBone[mapped.Count]
            };
        }

        internal bool Capture(IEnumerable<ushort> ids, out PoseFrame frame)
        {
            frame = null;
            var selected = ids == null ? null : new HashSet<ushort>(ids);
            if (selected != null && selected.Any(id => !KnownIds.Contains(id))) return false;
            var root = Model.transform;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            int count = 0;
            for (int i = 0; i < Bones.Length; i++)
            {
                if (selected != null && !selected.Contains(Ids[i])) continue;
                Bones[i].GetPositionAndRotation(out var worldPosition, out var worldRotation);
                Vector3 position = root.InverseTransformPoint(worldPosition);
                Quaternion rotation = inverse * worldRotation;
                Output[count++] = new PoseBone(Ids[i], PoseComponents.Position | PoseComponents.Rotation,
                    position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w);
            }
            if (count == 0) return false;
            // PoseFrame copies and validates immediately; the reusable buffer cannot escape.
            frame = new PoseFrame(Rig.Id, Rig.Version, Output.Take(count));
            return true;
        }
    }

    private static readonly CaptureBindingCache<Player, Binding> Bindings = new(binding => binding.IsCurrent);
    private static readonly Func<Player, Binding> CreateBinding = Binding.Create;
    private static int _thread;
    private static float _nextPrune;

    internal static void Initialize()
    {
        Shutdown();
        _thread = Environment.CurrentManagedThreadId;
        ClientEvents.SceneLoaded += OnSceneLoaded;
        ClientEvents.Disconnected += OnDisconnected;
    }

    internal static void Shutdown()
    {
        ClientEvents.SceneLoaded -= OnSceneLoaded;
        ClientEvents.Disconnected -= OnDisconnected;
        Bindings.Clear();
        _thread = 0;
        _nextPrune = 0;
    }

    internal static void Tick()
    {
        if (_thread != Environment.CurrentManagedThreadId || Time.unscaledTime < _nextPrune) return;
        _nextPrune = Time.unscaledTime + 1f;
        try { Bindings.Prune(); }
        catch { Bindings.Clear(); }
    }

    internal static bool TryCapture(Player player, IEnumerable<ushort> ids, out PoseFrame frame)
    {
        frame = null;
        if (_thread == 0 || _thread != Environment.CurrentManagedThreadId || player == null) return false;
        try
        {
            if (!Bindings.TryGetOrCreate(player, CreateBinding, out var binding)) return false;
            return binding.Capture(ids, out frame);
        }
        catch { Bindings.Remove(player); return false; }
    }

    private static void OnSceneLoaded(SceneLoadedEventArgs _) => Bindings.Clear();
    private static void OnDisconnected(ClientDisconnectedEventArgs _) => Bindings.Clear();
}
