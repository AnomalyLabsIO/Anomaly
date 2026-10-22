using Anomaly.Client.Api.Features;
using Anomaly.Shared.Poses;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>One model incarnation. Native bones, collider transforms and gameplay components are never posed.</summary>
internal sealed class PoseRigBinding : IDisposable
{
    internal sealed class BoneState { internal ushort Id; internal Transform Transform; internal int Depth; }
    private sealed class VisualNode { internal Transform Original; internal Transform Visual; internal int Parent; }
    private sealed class SkinState
    {
        internal SkinnedMeshRenderer Renderer;
        internal Il2CppReferenceArray<Transform> OriginalBones;
        internal Il2CppReferenceArray<Transform> VisualBones;
        internal Transform OriginalRoot;
        internal Transform VisualRoot;
        internal Bounds Bounds;
    }
    private sealed class RigidState
    {
        internal MeshRenderer Original;
        internal MeshRenderer Visual;
        internal MeshFilter OriginalFilter;
        internal MeshFilter VisualFilter;
        internal bool WasHidden;
        internal bool Applied;
    }

    private readonly BoneState[] _bones;
    internal IReadOnlyList<BoneState> NativeBones => _bones;
    private readonly Dictionary<ushort, BoneState> _byId;
    private readonly PoseBone?[] _poseById = new PoseBone?[PoseFrame.MaxBones];
    private readonly Dictionary<int, VisualNode> _nodes = new();
    private readonly Dictionary<int, SkinState> _skins = new();
    private readonly Dictionary<int, RigidState> _rigids = new();
    private MaterialPropertyBlock _properties;
    private SecondaryRigsSubcontroller _secondary;
    private WearableArmor[] _armor;
    private GameObject _visualRoot;
    private float _nextRefresh;
    private ushort _itemSerial;
    private bool _applied;

    internal readonly Player Player;
    internal readonly AnimatedCharacterModel Model;
    internal readonly PoseRigDescriptor Rig;
    internal readonly int Life;

    private PoseRigBinding(Player player, AnimatedCharacterModel model, PoseRigDescriptor rig, List<BoneState> bones)
    {
        Player = player; Model = model; Rig = rig;
        Life = player.CurrentRole.UniqueLifeIdentifier;
        _bones = bones.OrderBy(b => b.Depth).ThenBy(b => b.Id).ToArray();
        _byId = _bones.ToDictionary(b => b.Id);
    }

    internal bool IsCurrent => Player != null && !Player.IsDestroyed && Model != null &&
        !Model.Pooled && Player.CharacterModel == Model && Player.CurrentRole.UniqueLifeIdentifier == Life;

    internal static bool TryCreate(Player player, out PoseRigBinding binding)
    {
        binding = null;
        if (player == null || player.IsDestroyed || !PoseRigCatalog.TryGetForRole((sbyte)player.RoleType, out PoseRigDescriptor rig)) return false;
        var model = player.CharacterModel?.TryCast<AnimatedCharacterModel>();
        if (model == null || model.Pooled || model.Animator == null || model.name.Replace("(Clone)", string.Empty).Trim() != rig.ModelName) return false;
        try
        {
            var bones = new List<BoneState>();
            var seen = new HashSet<int>();
            foreach (PoseBoneDescriptor descriptor in rig.Bones)
            {
                Transform transform = descriptor.Path != null ? model.transform.Find(descriptor.Path) : model.Animator.GetBoneTransform((HumanBodyBones)descriptor.HumanoidBone);
                if (transform == null || !seen.Add(transform.GetInstanceID())) return false;
                int depth = 0;
                for (Transform current = transform; current != null && current != model.transform; current = current.parent) depth++;
                bones.Add(new BoneState { Id = descriptor.Id, Transform = transform, Depth = depth });
            }
            binding = new PoseRigBinding(player, model, rig, bones);
            return true;
        }
        catch { return false; }
    }

    internal bool TryCapture(IEnumerable<ushort> ids, out PoseFrame frame)
    {
        frame = null;
        if (!IsCurrent) return false;
        var selected = ids == null ? null : new HashSet<ushort>(ids);
        if (selected != null && selected.Any(id => !_byId.ContainsKey(id))) return false;
        var output = new List<PoseBone>();
        Transform root = Model.transform;
        Quaternion inverse = Quaternion.Inverse(root.rotation);
        foreach (BoneState bone in _bones)
        {
            if (selected != null && !selected.Contains(bone.Id)) continue;
            Vector3 position = root.InverseTransformPoint(bone.Transform.position);
            Quaternion rotation = inverse * bone.Transform.rotation;
            output.Add(new PoseBone(bone.Id, PoseComponents.Position | PoseComponents.Rotation, position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w));
        }
        if (output.Count == 0) return false;
        frame = new PoseFrame(Rig.Id, Rig.Version, output);
        return true;
    }

    internal void Apply(PoseFrame frame)
    {
        Restore();
        if (!IsCurrent || frame.RigId != Rig.Id || frame.SchemaVersion != Rig.Version || !frame.Bones.Any(p => p.Weight > 0 && (!Player.IsLocal || p.IncludeLocalPlayer))) return;
        PrepareVisuals();
        // Native animation is the baseline every frame, including frames where the animator was culled.
        foreach (VisualNode node in _nodes.Values)
        {
            if (node.Original == null || node.Visual == null) continue;
            node.Visual.localPosition = node.Original.localPosition;
            node.Visual.localRotation = node.Original.localRotation;
            node.Visual.localScale = node.Original.localScale;
        }
        _visualRoot.transform.localPosition = Vector3.zero;
        _visualRoot.transform.localRotation = Quaternion.identity;
        _visualRoot.transform.localScale = Vector3.one;
        _applied = true;
        try
        {
            Transform root = Model.transform;
            Array.Clear(_poseById, 0, _poseById.Length);
            foreach (PoseBone pose in frame.Bones) _poseById[pose.BoneId] = pose;
            foreach (BoneState bone in _bones)
            {
                if (!_poseById[bone.Id].HasValue) continue;
                PoseBone pose = _poseById[bone.Id].Value;
                if (Player.IsLocal && !pose.IncludeLocalPlayer) continue;
                Transform visual = EnsureClone(bone.Transform);
                float weight = Mathf.Clamp01(pose.Weight);
                Vector3 position = (pose.Components & PoseComponents.Position) != 0 ? Vector3.Lerp(bone.Transform.position, root.TransformPoint(new Vector3(pose.X, pose.Y, pose.Z)), weight) : visual.position;
                Quaternion rotation = (pose.Components & PoseComponents.Rotation) != 0 ? Quaternion.Slerp(bone.Transform.rotation, root.rotation * new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw), weight) : visual.rotation;
                visual.SetPositionAndRotation(position, rotation);
            }
            SyncVisualAttachments();
            foreach (SkinState skin in _skins.Values)
            {
                if (skin.Renderer == null || !skin.Renderer.transform.IsChildOf(Model.transform)) continue;
                skin.OriginalBones = skin.Renderer.bones;
                skin.OriginalRoot = skin.Renderer.rootBone;
                skin.Bounds = skin.Renderer.localBounds;
                skin.Renderer.bones = skin.VisualBones;
                skin.Renderer.rootBone = skin.VisualRoot;
                Bounds bounds = skin.Bounds;
                foreach (var bone in _bones) bounds.Encapsulate(skin.Renderer.transform.InverseTransformPoint(EnsureClone(bone.Transform).position));
                skin.Renderer.localBounds = bounds;
            }
            foreach (RigidState rigid in _rigids.Values)
            {
                if (rigid.Visual == null) continue;
                if (rigid.Original == null || rigid.OriginalFilter == null || !rigid.Original.transform.IsChildOf(Model.transform))
                {
                    rigid.Visual.enabled = false;
                    continue;
                }
                rigid.WasHidden = rigid.Original.forceRenderingOff;
                rigid.Applied = true;
                rigid.VisualFilter.sharedMesh = rigid.OriginalFilter.sharedMesh;
                rigid.Visual.sharedMaterials = rigid.Original.sharedMaterials;
                rigid.Original.GetPropertyBlock(_properties);
                rigid.Visual.SetPropertyBlock(_properties);
                rigid.Visual.shadowCastingMode = rigid.Original.shadowCastingMode;
                rigid.Visual.receiveShadows = rigid.Original.receiveShadows;
                rigid.Visual.enabled = rigid.Original.enabled && rigid.Original.gameObject.activeInHierarchy && !rigid.WasHidden
                    && IsHighestDetail(rigid.Original.name) && IsHighestDetail(rigid.OriginalFilter.sharedMesh?.name);
                rigid.Original.forceRenderingOff = true;
            }
            _visualRoot.SetActive(true);
        }
        catch { Restore(); throw; }
    }

    private void PrepareVisuals()
    {
        ushort serial = Player.InventoryBase?.CurItem.SerialNumber ?? 0;
        if (_visualRoot != null && (serial != _itemSerial || Time.unscaledTime >= _nextRefresh &&
            _nodes.Values.Any(node => node.Original == null || !node.Original.IsChildOf(Model.transform) ||
                node.Original.parent == null || node.Original.parent.GetInstanceID() != node.Parent))) DestroyVisuals();
        if (_visualRoot == null)
        {
            _visualRoot = new GameObject("Anomaly pose visuals") { hideFlags = HideFlags.HideAndDontSave };
            _visualRoot.transform.SetParent(Model.transform, false);
            _visualRoot.SetActive(false);
            _properties = new MaterialPropertyBlock();
            foreach (BoneState bone in _bones) EnsureClone(bone.Transform);
        }
        if (_skins.Count != 0 && serial == _itemSerial && Time.unscaledTime < _nextRefresh) return;
        _itemSerial = serial;
        _nextRefresh = Time.unscaledTime + 0.25f;
        foreach (var entry in _rigids.ToArray())
            if (entry.Value.Original == null || !entry.Value.Original.transform.IsChildOf(Model.transform))
            {
                if (entry.Value.Visual != null) UnityEngine.Object.Destroy(entry.Value.Visual.gameObject);
                _rigids.Remove(entry.Key);
            }
        foreach (var entry in _skins.ToArray())
            if (entry.Value.Renderer == null || !entry.Value.Renderer.transform.IsChildOf(Model.transform)) _skins.Remove(entry.Key);
        foreach (SkinnedMeshRenderer renderer in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.transform.IsChildOf(_visualRoot.transform)) continue;
            var originals = renderer.bones;
            var visuals = new Transform[originals.Length];
            for (int i = 0; i < originals.Length; i++) visuals[i] = originals[i] == null ? null : EnsureClone(originals[i]);
            _skins[renderer.GetInstanceID()] = new SkinState { Renderer = renderer, VisualBones = visuals, VisualRoot = renderer.rootBone == null ? null : EnsureClone(renderer.rootBone) };
        }
        foreach (MeshRenderer renderer in Model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.transform.IsChildOf(_visualRoot.transform) || _rigids.ContainsKey(renderer.GetInstanceID())) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null) continue;
            // Only visual components are copied, never item scripts, colliders, lights, or ownership state.
            var visual = new GameObject("Pose mesh") { hideFlags = HideFlags.HideAndDontSave, layer = renderer.gameObject.layer };
            visual.transform.SetParent(EnsureClone(renderer.transform), false);
            var mesh = visual.AddComponent<MeshFilter>();
            var clone = visual.AddComponent<MeshRenderer>();
            clone.enabled = false;
            _rigids.Add(renderer.GetInstanceID(), new RigidState { Original = renderer, OriginalFilter = filter, Visual = clone, VisualFilter = mesh });
        }
        Model.TryGetSubcontroller(out _secondary);
        _armor = Model.GetComponentsInChildren<WearableArmor>(true).ToArray();
        if (_secondary?._secondaryBones != null)
            foreach (var pair in _secondary._secondaryBones) { EnsureClone(pair.Original); EnsureClone(pair.Target); }
        foreach (var armor in _armor)
            if (armor != null && armor._trackedBones != null)
                foreach (var pair in armor._trackedBones)
                    if (pair != null) { EnsureClone(pair._boneTr); EnsureClone(pair._target); }
    }

    // The stripped game proxy has no LOD/GetLODs/SetLODs API. Stock rigid copies use
    // their authored LOD0 mesh instead of accidentally drawing all LOD variants.
    // Skinned renderers retain their native LOD groups and visibility unchanged.
    private static bool IsHighestDetail(string name)
    {
        if (name == null) return true;
        int marker = name.IndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker + 4 >= name.Length) return true;
        char level = name[marker + 4];
        return level < '1' || level > '9';
    }

    private Transform EnsureClone(Transform original)
    {
        if (original == null) return null;
        if (original == Model.transform) return _visualRoot.transform;
        if (!original.IsChildOf(Model.transform)) throw new InvalidOperationException("Pose skin references a bone outside its model.");
        int id = original.GetInstanceID();
        if (_nodes.TryGetValue(id, out var existing) && existing.Original == original && existing.Visual != null) return existing.Visual;
        Transform parent = EnsureClone(original.parent);
        var clone = new GameObject(original.name) { hideFlags = HideFlags.HideAndDontSave };
        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = original.localPosition;
        clone.transform.localRotation = original.localRotation;
        clone.transform.localScale = original.localScale;
        _nodes[id] = new VisualNode { Original = original, Visual = clone.transform, Parent = original.parent.GetInstanceID() };
        return clone.transform;
    }

    private void SyncVisualAttachments()
    {
        if (_secondary?._secondaryBones != null)
            foreach (var pair in _secondary._secondaryBones) Match(pair.Original, pair.Target);
        if (_armor != null)
            foreach (var armor in _armor)
                if (armor != null && armor._trackedBones != null)
                    foreach (var pair in armor._trackedBones)
                        if (pair != null) Match(pair._boneTr, pair._target);
    }

    private void Match(Transform original, Transform target)
    {
        if (original == null || target == null) return;
        Transform from = EnsureClone(original);
        EnsureClone(target).SetPositionAndRotation(from.position, from.rotation);
    }

    internal void Restore()
    {
        if (!_applied) return;
        Exception failure = null;
        try { if (_visualRoot != null) _visualRoot.SetActive(false); }
        catch (Exception ex) { failure = ex; }
        foreach (SkinState skin in _skins.Values)
        {
            try
            {
                if (skin.Renderer == null || skin.OriginalBones == null) continue;
                skin.Renderer.bones = skin.OriginalBones;
                skin.Renderer.rootBone = skin.OriginalRoot;
                skin.Renderer.localBounds = skin.Bounds;
                skin.OriginalBones = null;
            }
            catch (Exception ex) { failure ??= ex; }
        }
        foreach (RigidState rigid in _rigids.Values)
        {
            try
            {
                if (rigid.Applied && rigid.Original != null) rigid.Original.forceRenderingOff = rigid.WasHidden;
                rigid.Applied = false;
            }
            catch (Exception ex) { failure ??= ex; }
        }
        _applied = failure != null;
        if (failure != null) throw new InvalidOperationException("Could not restore every pose renderer; failed restorations will be retried.", failure);
    }

    public void Dispose()
    {
        Restore();
        DestroyVisuals();
    }

    private void DestroyVisuals()
    {
        if (_visualRoot != null)
        {
            _visualRoot.SetActive(false);
            // Destroy is deferred. Remove our tree from model discovery immediately.
            _visualRoot.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(_visualRoot);
        }
        _visualRoot = null;
        _nodes.Clear(); _skins.Clear(); _rigids.Clear();
        _nextRefresh = 0;
    }
}
