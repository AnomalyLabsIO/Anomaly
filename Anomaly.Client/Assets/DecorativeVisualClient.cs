using Anomaly.Client.Api.Networking;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anomaly.Client.Assets;

/// <summary>Creates only trusted Transform/MeshFilter/MeshRenderer objects from validated descriptions.</summary>
internal static class DecorativeVisualClient
{
    private sealed class Visual
    {
        internal DecorativeVisualInstance State;
        internal GameObject Root;
        internal readonly List<MeshFilter> Filters = new();
        internal readonly List<MeshRenderer> Renderers = new();
        internal readonly List<Material> Materials = new();
        internal long Triangles;
    }

    private static Dictionary<uint, Visual> _instances = new();
    private static readonly HashSet<Visual> PendingCleanup = new();
    private static bool _initialized, _hasSnapshot;
    private static ulong _generation, _revision;

    internal static void Init()
    {
        if (_initialized) return;
        AnomalyMessaging.MessageReceived += OnMessage;
        _initialized = true;
    }

    private static void OnMessage(IAnomalyMessage message)
    {
        if (message is not DecorativeVisualSnapshotMessage && message is not DecorativeVisualUpdateMessage) return;
        try
        {
            if (!ContentCatalogClient.IsActivated) throw new InvalidDataException("Decorative state arrived before content activation.");
            if (message is DecorativeVisualSnapshotMessage snapshot) ApplySnapshot(snapshot);
            else ApplyUpdate((DecorativeVisualUpdateMessage)message);
        }
        catch (Exception exception)
        {
            try { Clear(); }
            catch (Exception cleanupError) { MelonLogger.Warning("Decorative cleanup remains pending: " + cleanupError.Message); }
            finally { AnomalyMessaging.FailClosedTransport("Invalid decorative visual state: " + exception.Message); }
            MelonLogger.Warning("Decorative visual state rejected: " + exception.Message);
        }
    }

    private static void ApplySnapshot(DecorativeVisualSnapshotMessage message)
    {
        if (PendingCleanup.Count != 0) throw new InvalidOperationException("Decorative cleanup must complete before accepting another snapshot.");
        if (_hasSnapshot && (message.Generation < _generation ||
            (message.Generation == _generation && message.Revision <= _revision))) return;
        if (message.Generation == 0 || message.Instances == null || message.Instances.Count > AssetPackIndexCodec.MaxSceneInstances)
            throw new InvalidDataException("Invalid visual snapshot bounds.");
        var staged = new Dictionary<uint, Visual>();
        long triangles = 0;
        try
        {
            foreach (var state in message.Instances)
            {
                state.Validate();
                if (staged.ContainsKey(state.Id)) throw new InvalidDataException("Duplicate visual id.");
                long cost = GetCost(state);
                triangles = checked(triangles + cost);
                if (triangles > AssetPackIndexCodec.MaxSceneTriangles) throw new InvalidDataException("Decorative triangle budget exceeded.");
                staged.Add(state.Id, Create(state, cost));
            }
        }
        catch { Retire(staged.Values); throw; }
        var previous = _instances;
        _instances = staged;
        Retire(previous.Values);
        foreach (var visual in staged.Values) visual.Root.SetActive(true);
        _generation = message.Generation;
        _revision = message.Revision;
        _hasSnapshot = true;
    }

    private static void ApplyUpdate(DecorativeVisualUpdateMessage message)
    {
        if (PendingCleanup.Count != 0) throw new InvalidOperationException("Decorative cleanup must complete before accepting another update.");
        if (_hasSnapshot && (message.Generation < _generation ||
            (message.Generation == _generation && message.Revision <= _revision))) return;
        if (!_hasSnapshot || message.Generation != _generation || _revision == ulong.MaxValue || message.Revision != _revision + 1)
            throw new InvalidDataException("A visual update lacked a matching contiguous snapshot.");
        if (message.Instance == null || message.Instance.Id == 0) throw new InvalidDataException("Missing visual update identity.");
        _instances.TryGetValue(message.Instance.Id, out var previous);
        if (message.Operation == DecorativeVisualOperation.Remove)
        {
            if (previous != null) { _instances.Remove(message.Instance.Id); Retire(new[] { previous }); }
        }
        else if (message.Operation == DecorativeVisualOperation.Upsert)
        {
            message.Instance.Validate();
            if (previous != null && previous.State.Address == message.Instance.Address)
            {
                ApplyTransform(previous.Root.transform, message.Instance.Position, message.Instance.Rotation, message.Instance.Scale);
                previous.State = message.Instance.Clone();
            }
            else
            {
                if (previous == null && _instances.Count >= AssetPackIndexCodec.MaxSceneInstances)
                    throw new InvalidDataException("Decorative instance budget exceeded.");
                long cost = GetCost(message.Instance);
                if (_instances.Values.Sum(visual => visual.Triangles) - (previous?.Triangles ?? 0) + cost > AssetPackIndexCodec.MaxSceneTriangles)
                    throw new InvalidDataException("Decorative triangle budget exceeded.");
                var replacement = Create(message.Instance, cost);
                _instances[message.Instance.Id] = replacement;
                if (previous != null) Retire(new[] { previous });
                replacement.Root.SetActive(true);
            }
        }
        else throw new InvalidDataException("Unknown visual update operation.");
        _revision = message.Revision;
    }

    private static long GetCost(DecorativeVisualInstance state)
    {
        var address = AssetAddress.Parse(state.Address);
        if (!AssetBundleManager.TryGetRemoteVisual(address, out _)) throw new InvalidDataException("Visual address is outside the activated packs.");
        long cost = AssetBundleManager.GetRemoteVisualTriangles(address);
        if (cost < 0 || cost > AssetPackIndexCodec.MaxSceneTriangles) throw new InvalidDataException("Invalid visual triangle cost.");
        return cost;
    }

    private static Visual Create(DecorativeVisualInstance state, long triangles)
    {
        var address = AssetAddress.Parse(state.Address);
        if (!AssetBundleManager.TryGetRemoteVisual(address, out var definition)) throw new InvalidDataException("Visual definition is unavailable.");
        var visual = new Visual { State = state.Clone(), Triangles = triangles };
        try
        {
            visual.Root = new GameObject("Anomaly decorative " + state.Id);
            visual.Root.SetActive(false);
            var transforms = new List<Transform>();
            foreach (var node in definition.Nodes)
            {
                var child = new GameObject(node.Name);
                child.transform.SetParent(node.Parent < 0 ? visual.Root.transform : transforms[node.Parent], false);
                ApplyTransform(child.transform, node.Position, node.Rotation, node.Scale);
                transforms.Add(child.transform);
                if (string.IsNullOrEmpty(node.MeshAddress)) continue;
                var mesh = AssetBundleManager.ResolveRemote(AssetAddress.Parse(node.MeshAddress), AssetKind.Mesh)?.TryCast<Mesh>()
                    ?? throw new InvalidDataException("Decorative mesh is unavailable.");
                var filter = child.AddComponent<MeshFilter>();
                visual.Filters.Add(filter);
                filter.sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                visual.Renderers.Add(renderer);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var material = CreateMaterial(node, visual);
                renderer.sharedMaterial = material;
            }
            ApplyTransform(visual.Root.transform, state.Position, state.Rotation, state.Scale);
            return visual;
        }
        catch { Retire(new[] { visual }); throw; }
    }

    private static Material CreateMaterial(DecorativeVisualNode node, Visual owner)
    {
        string shaderName = node.MaterialPreset switch {
            "opaque-unlit" => "HDRP/Unlit", "opaque-lit" => "HDRP/Lit",
            _ => throw new InvalidDataException("Unknown trusted material preset."),
        };
        var shader = Shader.Find(shaderName);
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Trusted material shader is unavailable: " + shaderName);
        var material = new Material(shader);
        owner.Materials.Add(material); // Own partial configuration if a native setter fails.
        material.hideFlags = HideFlags.DontUnloadUnusedAsset;
        material.renderQueue = 2000;
        if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 0);
        string colorProperty = node.MaterialPreset == "opaque-lit" ? "_BaseColor" : "_UnlitColor";
        string textureProperty = node.MaterialPreset == "opaque-lit" ? "_BaseColorMap" : "_UnlitColorMap";
        if (!material.HasProperty(colorProperty) || !material.HasProperty(textureProperty))
            throw new InvalidOperationException("Trusted shader properties do not match the supported profile.");
        material.SetColor(colorProperty, new Color(node.Color[0], node.Color[1], node.Color[2], 1));
        if (!string.IsNullOrEmpty(node.TextureAddress))
        {
            var texture = AssetBundleManager.ResolveRemote(AssetAddress.Parse(node.TextureAddress), AssetKind.Texture2D)?.TryCast<Texture2D>()
                ?? throw new InvalidDataException("Decorative texture is unavailable.");
            material.SetTexture(textureProperty, texture);
        }
        return material;
    }

    private static void ApplyTransform(Transform target, float[] position, float[] rotation, float[] scale)
    {
        target.localPosition = new Vector3(position[0], position[1], position[2]);
        target.localRotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        target.localScale = new Vector3(scale[0], scale[1], scale[2]);
    }

    internal static void Clear()
    {
        var previous = _instances.Values.Concat(PendingCleanup).Distinct().ToArray();
        _instances = new Dictionary<uint, Visual>();
        _hasSnapshot = false; _generation = 0; _revision = 0;
        Retire(previous);
    }

    private static void Retire(IEnumerable<Visual> visuals)
    {
        var failures = new List<Exception>();
        foreach (var visual in visuals.ToArray())
        {
            PendingCleanup.Add(visual);
            try { Destroy(visual); PendingCleanup.Remove(visual); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count != 0) throw new AggregateException("Decorative bindings remain pending cleanup; native packs must remain loaded.", failures);
    }

    private static void Destroy(Visual visual)
    {
        var failures = new List<Exception>();
        void Try(Action action) { try { action(); } catch (Exception exception) { failures.Add(exception); } }
        Try(() => { if (visual.Root != null) visual.Root.SetActive(false); });
        foreach (var filter in visual.Filters) Try(() => { if (filter != null) filter.sharedMesh = null; });
        foreach (var renderer in visual.Renderers) Try(() => { if (renderer != null) renderer.sharedMaterial = null; });
        foreach (var material in visual.Materials) Try(() => {
            if (material == null) return;
            // Destroy is deferred; detach each owned bundle texture immediately.
            if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", null);
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
            UnityEngine.Object.Destroy(material);
        });
        Try(() => { if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root); });
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}
