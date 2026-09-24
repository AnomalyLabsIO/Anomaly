using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.Api.Assets;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.HighDefinition;

namespace Anomaly.AssetBundleSmoke;

// Local integration probe of the real manager and native consumers. Its catalog
// is an owner-pinned fixture; authenticated network transfer is a separate gate.
internal sealed class AdvancedProbe
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly string _output;
    private readonly Action<bool, string> _require;
    private readonly Action<string, object> _log;
    private readonly Type _manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
    private readonly Type _visual = Type.GetType("Anomaly.Client.Assets.DecorativeVisualClient, Anomaly.Client", true);
    private readonly List<Canvas> _hiddenCanvases = new();
    private readonly List<Camera> _hiddenCameras = new();
    private AudioClip _clip;
    private Mesh _mesh;
    private Sprite _sprite;
    private Texture2D _texture;
    private AudioSource _source;
    private GameObject _audioRoot, _spriteRoot, _cameraRoot;
    private int _stage;
    private bool _activationDone, _activationAccepted, _cancelledCallbackInvoked;
    private string _activationError;
    private double _readyAt;
    private double _outputEnergy;
    private bool _outputInspected;
    private DecorativeVisualInstance _instance;
    internal bool NeedsCapture { get; private set; }
    internal bool Complete { get; private set; }

    internal AdvancedProbe(string output, Action<bool, string> require, Action<string, object> log)
    {
        _output = output; _require = require; _log = log;
        string indexPath = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_QUALIFICATION_INDEX");
        string bundlePath = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_QUALIFICATION_BUNDLE");
        byte[] indexBytes = File.ReadAllBytes(indexPath);
        byte[] bundleBytes = File.ReadAllBytes(bundlePath);
        byte[] indexHash = SHA256.HashData(indexBytes), bundleHash = SHA256.HashData(bundleBytes);
        _require(Convert.ToHexString(bundleHash).Equals(Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_QUALIFICATION_SHA256"), StringComparison.OrdinalIgnoreCase),
            "Advanced fixture is owner-pinned");
        var index = AssetPackIndexCodec.Parse(indexBytes, remote: true);
        _require(index.BundleSha256.Equals(Convert.ToHexString(bundleHash), StringComparison.OrdinalIgnoreCase), "Advanced index matches exact bundle");
        var cache = Type.GetType("Anomaly.Client.Api.FileTransfer.FileCache, Anomaly.Client.Api", true);
        if (cache.GetField("_cacheDir", All).GetValue(null) == null) cache.GetMethod("Init", All).Invoke(null, null);
        cache.GetMethod("WriteFile", All).Invoke(null, new object[] { indexHash, indexBytes, "qualification.index.json" });
        cache.GetMethod("WriteFile", All).Invoke(null, new object[] { bundleHash, bundleBytes, index.BundleFile });
        var catalog = new ContentCatalog { Packs = new List<CatalogPack> { new() {
            PackId = index.PackId, IndexHash = indexHash, IndexSize = (uint)indexBytes.Length,
            BundleHash = bundleHash, BundleSize = (uint)bundleBytes.Length,
        } } };
        Action<bool, string> stale = (_, _) => _cancelledCallbackInvoked = true;
        _manager.GetMethod("ActivateRemote", All).Invoke(null, new object[] { catalog, stale });
        _manager.GetMethod("RevokeRemote", All).Invoke(null, null);
        Action<bool, string> completed = (accepted, error) => { _activationAccepted = accepted; _activationError = error; _activationDone = true; };
        _manager.GetMethod("ActivateRemote", All).Invoke(null, new object[] { catalog, completed });
        _require(!AnomalyResources.TryGetAudioClip(Address("audio/tone"), out _), "Remote addresses remain unpublished while validation is pending");
    }

    private static AssetAddress Address(string path) => AssetAddress.Parse("mods/fixture/" + path);

    internal void Update()
    {
        if (Complete || NeedsCapture) return;
        if (_stage == 0)
        {
            if (!_activationDone) return;
            _require(_activationAccepted, "Advanced production pack activation succeeded: " + _activationError);
            _require(!_cancelledCallbackInvoked, "Cancelled validation generation never calls its completion or publishes assets");
            AssertTypesAndAudio();
            _stage = 1;
            _readyAt = Time.realtimeSinceStartupAsDouble + 0.1;
            return;
        }
        if (_stage == 1)
        {
            if (Time.realtimeSinceStartupAsDouble < _readyAt) return;
            _require(_source.isPlaying && _source.timeSamples > 0, "Native AudioSource advances sample playback for the bundle clip");
            var output = new Il2CppStructArray<float>(1024);
            try
            {
                _source.GetOutputData(output, 0);
                bool finite = true;
                foreach (float sample in output) { finite &= !float.IsNaN(sample) && !float.IsInfinity(sample); _outputEnergy += sample * sample; }
                _require(finite, "Native AudioSource output samples are finite");
                _outputInspected = true;
            }
            catch (NotSupportedException exception) { _log("audio-output-inspection-unavailable", new { reason = exception.Message }); }
            _log("native-audio-playback", new { samples = _source.timeSamples, frequency = _clip.frequency, mutedFixture = true, outputEnergy = _outputEnergy, outputInspected = _outputInspected });
            CreateVisuals();
            _readyAt = Time.realtimeSinceStartupAsDouble + 0.5;
            _stage = 2;
            return;
        }
        if (_stage == 2 && Time.realtimeSinceStartupAsDouble >= _readyAt) NeedsCapture = true;
    }

    private void AssertTypesAndAudio()
    {
        _require(AnomalyResources.TryGetTexture(Address("textures/checker"), out _texture), "Typed Texture2D resolver returns advanced checker");
        _require(AnomalyResources.TryGetSprite(Address("sprites/checker"), out _sprite), "Typed Sprite resolver returns advanced sprite");
        _require(AnomalyResources.TryGetMesh(Address("meshes/quad"), out _mesh), "Typed Mesh resolver returns advanced quad");
        _require(AnomalyResources.TryGetAudioClip(Address("audio/tone"), out _clip), "Typed AudioClip resolver returns advanced tone");
        _require(_texture.width == 256 && _texture.height == 256 && _sprite.rect.width == 256 && _sprite.rect.height == 256,
            "Native texture and sprite dimensions match authoring fixture");
        _require(_mesh.vertexCount == 4 && _mesh.triangles.Length == 6, "Native quad topology is four vertices and two triangles");
        _require(_clip.samples == 12000 && _clip.frequency == 48000 && _clip.channels == 2, "Native audio metadata matches 12000 stereo frames at 48 kHz");
        _require(_clip.LoadAudioData(), "Unity loads native AudioClip sample data");
        _require(_clip.loadType == AudioClipLoadType.CompressedInMemory, "Native audio preserves the accepted compressed-in-memory profile");
        _log("native-types", new { vertices = _mesh.vertexCount, indices = _mesh.triangles.Length,
            frames = _clip.samples, sampleRate = _clip.frequency, channels = _clip.channels, loadType = _clip.loadType.ToString() });
        _audioRoot = new GameObject("AssetBundle muted audio proof");
        _source = _audioRoot.AddComponent<AudioSource>();
        _source.clip = _clip; _source.volume = 0; _source.loop = true; _source.Play();

        var remoteLayer = _manager.GetField("_remote", All).GetValue(null);
        foreach (var pack in (IEnumerable)remoteLayer.GetType().GetField("Packs", All).GetValue(remoteLayer))
        {
            var prepared = pack.GetType().GetField("Prepared", All).GetValue(pack);
            var copy = prepared.GetType().GetField("Copy", All).GetValue(prepared);
            string path = (string)copy.GetType().GetField("Path", All).GetValue(copy);
            bool blocked = false;
            try { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) { blocked = true; }
            _require(blocked, "Live verified native bundle rejects a writable file handle");
        }
    }

    private void CreateVisuals()
    {
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (existing != null && existing.enabled) { _hiddenCameras.Add(existing); existing.enabled = false; }
        _cameraRoot = new GameObject("AssetBundle isolated rendering proof camera");
        var camera = _cameraRoot.AddComponent<Camera>();
        camera.transform.position = new Vector3(10000, 10000, 10000);
        camera.transform.rotation = Quaternion.identity;
        camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.01f, 0.01f, 0.02f, 1);
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 10;
        camera.orthographic = true; camera.orthographicSize = 1.8f;
        var cameraData = _cameraRoot.AddComponent<HDAdditionalCameraData>();
        cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        cameraData.backgroundColorHDR = camera.backgroundColor;
        cameraData.volumeLayerMask = 0;
        cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
        var position = camera.transform.position + camera.transform.forward * 3;
        var normal = _mesh.normals.Length > 0 ? _mesh.normals[0] : Vector3.back;
        var rotation = Quaternion.FromToRotation(normal, -camera.transform.forward);
        _instance = new DecorativeVisualInstance { Id = 1, Address = Address("visuals/quad").Value,
            Position = new[] { position.x, position.y, position.z }, Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
            Scale = new[] { 1f, 1f, 1f } };
        var snapshot = new DecorativeVisualSnapshotMessage { Generation = 17, Revision = 0, Instances = new List<DecorativeVisualInstance> { _instance } };
        _visual.GetMethod("ApplySnapshot", All).Invoke(null, new object[] { snapshot });
        var instanceMap = (IDictionary)_visual.GetField("_instances", All).GetValue(null);
        _require(instanceMap.Count == 1, "Trusted visual snapshot creates exactly one instance");
        var visual = instanceMap[1u];
        var root = (GameObject)visual.GetType().GetField("Root", All).GetValue(visual);
        foreach (var transform in root.GetComponentsInChildren<Transform>()) transform.gameObject.layer = 30;
        _require(root.GetComponentsInChildren<MeshRenderer>().Length == 1 && root.GetComponentsInChildren<MeshFilter>().Length == 1,
            "Trusted renderer constructs one MeshFilter and MeshRenderer");
        _require(root.GetComponentsInChildren<Collider>().Length == 0 && root.GetComponentsInChildren<Rigidbody>().Length == 0 &&
            root.GetComponentsInChildren<MonoBehaviour>().Length == 0, "Decorative hierarchy contains no scripts or physics components");
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (canvas != null && canvas.enabled) { _hiddenCanvases.Add(canvas); canvas.enabled = false; }
        _spriteRoot = new GameObject("AssetBundle sprite proof");
        var spriteCanvas = _spriteRoot.AddComponent<Canvas>();
        spriteCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        spriteCanvas.sortingOrder = 20000;
        var spriteObject = new GameObject("Native bundle Sprite");
        spriteObject.transform.SetParent(_spriteRoot.transform, false);
        var image = spriteObject.AddComponent<Image>();
        image.sprite = _sprite; image.color = Color.white;
        var rectangle = image.GetComponent<RectTransform>();
        rectangle.sizeDelta = new Vector2(256, 256);
        rectangle.anchorMin = rectangle.anchorMax = new Vector2(0.15f, 0.5f);
        rectangle.anchoredPosition = Vector2.zero;
        _require(image.sprite != null && image.sprite.GetInstanceID() == _sprite.GetInstanceID(), "Actual Image displays the native bundle Sprite");
    }

    internal void CaptureCompleted()
    {
        NeedsCapture = false;
        var update = new DecorativeVisualUpdateMessage { Generation = 17, Revision = 1, Operation = DecorativeVisualOperation.Upsert, Instance = _instance.Clone() };
        update.Instance.Position[0] += 0.25f;
        _visual.GetMethod("ApplyUpdate", All).Invoke(null, new object[] { update });
        var instanceMap = (IDictionary)_visual.GetField("_instances", All).GetValue(null);
        var visual = instanceMap[1u];
        var root = (GameObject)visual.GetType().GetField("Root", All).GetValue(visual);
        _require(Math.Abs(root.transform.position.x - update.Instance.Position[0]) < 0.001f, "Contiguous visual update changes the real transform");
        update.Instance.Position[0] += 100;
        _visual.GetMethod("ApplyUpdate", All).Invoke(null, new object[] { update });
        _require(Math.Abs(root.transform.position.x - (update.Instance.Position[0] - 100)) < 0.001f, "Stale visual revision does not mutate the hierarchy");
        _visual.GetMethod("ApplyUpdate", All).Invoke(null, new object[] { new DecorativeVisualUpdateMessage {
            Generation = 17, Revision = 2, Operation = DecorativeVisualOperation.Remove, Instance = new DecorativeVisualInstance { Id = 1 } } });
        _require(instanceMap.Count == 0, "Visual remove restores an empty instance set");
        foreach (var canvas in _hiddenCanvases) if (canvas != null) canvas.enabled = true;
        _cameraRoot.SetActive(false);
        UnityEngine.Object.Destroy(_cameraRoot);
        foreach (var camera in _hiddenCameras) if (camera != null) camera.enabled = true;
        _spriteRoot.GetComponentInChildren<Image>().sprite = null;
        UnityEngine.Object.Destroy(_spriteRoot);
        _spriteRoot = null;
        Type.GetType("Anomaly.Client.Content.ContentCatalogClient, Anomaly.Client", true).GetMethod("RevokeMaterializedContent", All).Invoke(null, null);
        _require(_source.clip == null && !_source.isPlaying, "Remote teardown detaches active native audio before unloading");
        _require(_clip == null && _mesh == null && _sprite == null && _texture == null, "Remote manager unloads all qualified native asset types");
        _require(!AnomalyResources.TryGetAudioClip(Address("audio/tone"), out _), "Remote address lookup is revoked after teardown");
        UnityEngine.Object.Destroy(_audioRoot);
        File.WriteAllText(Path.Combine(_output, "qualification.json"), JsonSerializer.Serialize(new {
            status = "passed", texture = true, sprite = true, mesh = true, nativeAudioPcm = false, nativeAudioPlayback = true, outputEnergy = _outputEnergy, outputInspected = _outputInspected,
            trustedVisual = true, staleRevision = true, cancelledGeneration = true, lockedNativeBytes = true, remoteTeardown = true,
            authenticatedTransferQualified = false, humanAudibilityProven = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Complete = true;
    }

    internal void VerifyVisualFrame(Texture2D frame)
    {
        int stride = frame.format == TextureFormat.RGB24 ? 3 : frame.format == TextureFormat.RGBA32 ? 4 : 0;
        _require(stride != 0, "Qualified render has a supported capture pixel format");
        var pixels = frame.GetRawTextureData();
        int colored = 0;
        for (int y = frame.height / 2 - 80; y < frame.height / 2 + 80; y++)
        for (int x = frame.width / 2 - 80; x < frame.width / 2 + 80; x++)
        {
            int at = (y * frame.width + x) * stride;
            int maximum = Math.Max(pixels[at], Math.Max(pixels[at + 1], pixels[at + 2]));
            int minimum = Math.Min(pixels[at], Math.Min(pixels[at + 1], pixels[at + 2]));
            if (maximum > 40 && maximum - minimum > 20) colored++;
        }
        _log("trusted-mesh-render", new { coloredCenterPixels = colored, isolatedHarnessCamera = true });
        _require(colored > 1000, "The trusted native mesh renders its colored texture in the center of the engine frame");
    }
}
