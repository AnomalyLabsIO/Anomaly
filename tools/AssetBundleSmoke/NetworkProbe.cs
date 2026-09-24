using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppCassie;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ApiClient = Anomaly.Client.Api.Features.Client;
using ApiLocalPlayer = Anomaly.Client.Api.Features.LocalPlayer;
using Object = UnityEngine.Object;

namespace Anomaly.AssetBundleSmoke;

// An opt-in loopback integration witness. Content enters only through the real
// consent and authenticated transfer path; reflection observes private state.
internal sealed class NetworkProbe
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string Server = "127.0.0.1:27989";
    private const string Word = "ANOMALY_BUNDLE_TONE";
    private readonly string _output;
    private readonly Action<bool, string> _require;
    private readonly Action<string, object> _log;
    private readonly Type _manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
    private readonly Type _visual = Type.GetType("Anomaly.Client.Assets.DecorativeVisualClient, Anomaly.Client", true);
    private readonly Type _content = Type.GetType("Anomaly.Client.Content.ContentCatalogClient, Anomaly.Client", true);
    private readonly Type _prompt = Type.GetType("Anomaly.Client.Components.PasswordPrompt, Anomaly.Client", true);
    private readonly Type _cassiePatch = Type.GetType("Anomaly.Client.Patches.Audio.CassieLineSourceSchedulePatch, Anomaly.Client", true);
    private readonly Queue<(string Name, Func<bool> Run, double Seconds)> _steps = new();
    private readonly Dictionary<int, SmokeResponse> _responses = new();
    private readonly List<object> _cycles = new();
    private readonly List<object> _lifetimeSnapshots = new();
    private readonly List<Object> _remoteObjects = new();
    private readonly IDisposable _capability;
    private readonly bool _negative;
    private readonly long _nativeAttemptsAtStart;
    private string _stepName;
    private double _stepDeadline, _decisionAt;
    private int _token, _cycle, _chunks, _manifests, _snapshots, _visualUpdates, _consentChoices, _customItemId;
    private long _chunkBytes;
    private bool _sawConsent, _approve, _textureMessage, _roundVerified, _released;
    private uint _visualId, _lateJoinId, _previousHub;
    private ulong _previousGeneration;
    private IntPtr _previousConnection;
    private string _previousEnrollment;
    private Texture2D _texture;
    private Sprite _sprite;
    private Mesh _mesh;
    private AudioClip _tone, _localG7, _remoteG7, _stockG7;
    private CassieLine _g7Line;
    private float _stockDuration;
    private Renderer _textureTarget;
    private Texture _textureOriginal;
    private GameObject _lastVisualRoot;
    private int _updatedRootId;
    private Vector3 _beforeUpdatePosition;
    private bool _interruptArmed, _interrupted, _unexpectedPublication, _validationFailure;
    private long _manifestBytes, _interruptedBytes;
    private string _manifestHash, _firstManifestHash, _catalogHash, _interruptedState, _failureDetail;
    private string[] _packIds = Array.Empty<string>();

    internal bool Complete { get; private set; }
    internal bool NeedsCapture { get; private set; }

    internal NetworkProbe(string output, Action<bool, string> require, Action<string, object> log)
    {
        _output = output; _require = require; _log = log;
        _negative = Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_NETWORK_MODE") == "negative";
        _nativeAttemptsAtStart = NativeLoadAttempts;
        _require(Environment.GetEnvironmentVariable("ANOMALY_ASSETBUNDLE_SMOKE_NETWORK") == "1", "Network probe is explicitly opted in");
        _require(!ApiClient.IsConnected, "Network probe starts outside a connection");
        _require(AnomalyResources.TryGetAudioClip(AssetAddress.Parse("game/cassie/.g7"), out _localG7),
            "Local .g7 pack is loaded through ordinary startup");
        _require(_localG7.samples == 72000 && _localG7.frequency == 44100 && _localG7.channels == 1,
            "Local .g7 native metadata matches the qualified stock target");
        AnomalyMessageRegistry.Register(SmokeRequest.Name, () => new SmokeRequest());
        AnomalyMessageRegistry.Register(SmokeResponse.Name, () => new SmokeResponse());
        _capability = ClientCapabilities.Register(new CapabilityDescriptor("anomaly-smoke:client", 1, 0));
        AnomalyMessaging.MessageReceived += OnMessage;
        if (_negative) AddNegativeScenarios();
        else
        {
            AddCycle(1, approve: false);
            AddCycle(2, approve: true);
            AddCycle(3, approve: true);
        }
        Add("network-complete", () =>
        {
            if (_negative)
                _require(_interrupted && _validationFailure && !_unexpectedPublication && _consentChoices == 1,
                    "Interrupted transfer and hash-matched invalid bundle fail without native publication");
            else
                _require(_cycles.Count == 3 && _roundVerified && _consentChoices == 2,
                    "Three authenticated cycles and one native round used exactly decline then allow choices");
            Release();
            Complete = true;
            WriteResult("awaiting-final-shutdown", null);
        });
        _log("network-start", new { endpoint = Server, productionTransferOnly = true, localG7 = _localG7.GetInstanceID() });
        RecordLifetime("local-startup");
    }

    internal void Update()
    {
        if (Complete || NeedsCapture) return;
        try
        {
            if (_negative && (IsActivated || NativeLoadAttempts != _nativeAttemptsAtStart))
            {
                _unexpectedPublication = true;
                throw new InvalidOperationException("Negative transfer reached activation or native bundle loading.");
            }
            if (_steps.Count == 0) throw new InvalidOperationException("Network steps ended without completing.");
            var step = _steps.Peek();
            if (_stepName != step.Name)
            {
                _stepName = step.Name;
                _stepDeadline = Time.realtimeSinceStartupAsDouble + step.Seconds;
                _log("network-step", new { name = step.Name, cycle = _cycle });
            }
            if (Time.realtimeSinceStartupAsDouble > _stepDeadline)
                throw new TimeoutException("Network step timed out: " + step.Name + "; " + JsonSerializer.Serialize(Diagnostics()));
            if (step.Run()) { _steps.Dequeue(); _stepName = null; }
        }
        catch (Exception exception)
        {
            WriteResult("failed", exception.ToString());
            _log("network-failed", Diagnostics());
            Release();
            throw;
        }
    }

    internal void CaptureCompleted()
    {
        _require(NeedsCapture, "Network capture belongs to the active visual step");
        NeedsCapture = false;
    }

    // Core restores the stock menu consumers and shuts the manager down first.
    internal void VerifyFinalShutdown()
    {
        _require(Complete, "Network scenarios completed before final shutdown");
        if (!_negative)
        {
            _require(_g7Line != null && _stockG7 != null, "Network completed with a preserved stock CASSIE baseline");
            _require(_g7Line.Clip == _stockG7 && _g7Line.Duration == _stockDuration,
                "Final manager shutdown restores the true stock .g7 clip and scheduling duration");
        }
        _require(_localG7 == null && !AnomalyResources.TryGetAudioClip(AssetAddress.Parse("game/cassie/.g7"), out _),
            "Final manager shutdown releases the local .g7 bundle");
        var lifetime = RecordLifetime("final-shutdown");
        _require(lifetime["nativePacks"] == 0 && lifetime["cachedCopies"] == 0 && lifetime["cacheLeases"] == 0 && lifetime["verifiedFileLocks"] == 0,
            "Final shutdown leaves no native packs, verified cache copies, leases, or file locks");
        WriteResult("passed", null);
    }

    private void AddNegativeScenarios()
    {
        Add("negative-interrupt-connect", () =>
        {
            _cycle = 1; _approve = true; _sawConsent = false; _decisionAt = 0;
            _chunks = _manifests = 0; _chunkBytes = 0; _interruptArmed = true;
            ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(Server);
        });
        Wait("negative-first-partial-chunk", () => { PumpConnection(); return _interrupted; }, 150);
        Wait("negative-interrupt-menu", () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Add("negative-interrupt-cleanup", () =>
        {
            _require(_sawConsent && _consentChoices == 1 && _interruptedState == "Receiving" &&
                _interruptedBytes > 0 && _interruptedBytes < _manifestBytes,
                "Disconnect interrupted an explicitly approved in-progress download before completion");
            _require(_packIds.Contains("fixture:invalid") && _manifests == 1,
                "Authenticated interrupted offer contains the intended invalid-pack fixture");
            _firstManifestHash = _manifestHash;
            AssertRemoteAbsent();
            AssertNegativeOwnership("interrupted-transfer");
            _cycles.Add(new { cycle = 1, approved = true, interrupted = true, state = _interruptedState,
                receivedBytes = _interruptedBytes, offeredBytes = _manifestBytes, manifestHash = _manifestHash });
        });
        Add("negative-reconnect", () =>
        {
            _cycle = 2; _sawConsent = false; _decisionAt = 0;
            _chunks = _manifests = _snapshots = 0; _chunkBytes = 0;
            ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(Server);
        });
        Wait("negative-validator-rejection", () =>
        {
            PumpConnection();
            if (!FeatureHealth.Snapshot().TryGetValue("anomaly:content", out var health) || health.Status != FeatureHealthStatus.Failed) return false;
            _failureDetail = health.Detail;
            _validationFailure = _failureDetail.Contains("bundle validation or loading failed", StringComparison.OrdinalIgnoreCase) &&
                _failureDetail.Contains("fixture:invalid", StringComparison.Ordinal);
            _require(_validationFailure, "The intended invalid pack caused an explicit bundle-validation failure: " + _failureDetail);
            return true;
        }, 150);
        Wait("negative-rejection-menu", () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Add("negative-validation-cleanup", () =>
        {
            _require(!_sawConsent && _consentChoices == 1 && _manifestHash == _firstManifestHash && _manifests == 1 && _chunkBytes > 0,
                "Interrupted transfer reconnect reuses exact saved approval and resumes through a fresh authenticated offer");
            _require(_snapshots == 0 && !_unexpectedPublication, "Rejected content never receives a decorative snapshot or publishes native assets");
            AssertRemoteAbsent(); AssertNegativeOwnership("invalid-pack-rejected");
            _require(AnomalyResources.TryGetAudioClip(AssetAddress.Parse("game/cassie/.g7"), out var local) && local == _localG7,
                "Failed server content preserves the local replacement layer");
            _cycles.Add(new { cycle = 2, approved = true, prompted = _sawConsent, receivedChunks = _chunks, receivedBytes = _chunkBytes,
                rejectedPackId = "fixture:invalid", failure = _failureDetail, catalogHash = _catalogHash, manifestHash = _manifestHash });
            _log("network-negative-rejection", new { packId = "fixture:invalid", catalogHash = _catalogHash, manifestHash = _manifestHash,
                failure = _failureDetail, nativeLoadAttempts = NativeLoadAttempts, activation = false });
        });
    }

    private void AssertNegativeOwnership(string stage)
    {
        var lifetime = RecordLifetime(stage);
        _require(NativeLoadAttempts == _nativeAttemptsAtStart && lifetime["remotePacks"] == 0 &&
            lifetime["nativePacks"] == lifetime["localPacks"] && lifetime["cacheLeases"] == lifetime["localPacks"],
            "Failed/interrupted content creates no native bundle load and retains no remote pack lease: " + stage);
    }

    private void AddCycle(int number, bool approve)
    {
        Add("connect-" + number, () =>
        {
            _cycle = number; _approve = approve; _sawConsent = false; _decisionAt = 0;
            _chunks = _manifests = _snapshots = _visualUpdates = 0; _chunkBytes = 0;
            _remoteObjects.Clear();
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(Server);
        });
        Wait("authenticated-ready-" + number, PumpConnection, 150);
        Poll("content-state-" + number, json => json.GetProperty("readiness").GetString() == (approve ? "Activated" : "Declined"), json =>
        {
            _require(_sawConsent == (number <= 2), "Consent prompt presence matches fresh/declined versus approved cached catalog: " + number);
            _require(_manifests > 0, "Production manifest was received: " + number);
            _require(json.GetProperty("assetReady").GetBoolean(), "Loopback server pack registry is ready");
            _customItemId = json.GetProperty("itemId").GetInt32();
            _visualId = json.GetProperty("visualId").GetUInt32();
            int requested = json.GetProperty("requestedFiles").GetInt32();
            if (!approve)
            {
                _require(requested < 0 && _chunks == 0 && _chunkBytes == 0, "Declining starts no file request or download");
                AssertRemoteAbsent();
                CaptureStockAndLocalG7();
            }
            else
            {
                _require(number == 2 ? requested == 4 && _chunks > 0 && _chunkBytes > 0 : requested == 0 && _chunks == 0,
                    number == 2 ? "Fresh approval downloads both exact index/bundle pairs" : "Cached reconnect requests zero files and receives zero chunks");
                AssertAssets();
                if (number == 3) _require(_visualId == _lateJoinId, "Server retained the visual identity across the disconnected interval");
            }
            _cycles.Add(new { cycle = number, approved = approve, prompted = _sawConsent, requestedFiles = requested,
                receivedChunks = _chunks, receivedBytes = _chunkBytes, manifestCount = _manifests });
            _log("network-content-state", new { cycle = number, server = json.Clone(), receivedChunks = _chunks, receivedBytes = _chunkBytes });
        });
        if (approve)
        {
            Wait("native-visual-snapshot-" + number, () => _snapshots > 0 && _visualId != 0 && VisualRoot(_visualId) != null, 20);
            Add("native-visual-bindings-" + number, () => AssertVisual(_visualId));
            Wait("vocabulary-" + number, () => Announcer.IsValid(Word), 20);
            Add("vocabulary-duration-" + number, () =>
            {
                var line = FindLine(Word);
                _require(line != null && line.Clip == _tone && Math.Abs(line.Duration - 0.25f) < 0.00001f,
                    "Network vocabulary references the validated audio with sample-derived duration");
            });
            if (number == 2) AddConsumerScenarios();
            if (number == 3) AddRoundScenario();
        }
        Add("disconnect-" + number, () => ApiClient.Disconnect());
        Wait("menu-after-disconnect-" + number, () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
        Wait("remote-unloaded-" + number, () => _remoteObjects.All(asset => asset == null) && _lastVisualRoot == null, 20);
        Add("disconnect-cleanup-" + number, () =>
        {
            _require(ApiClient.EnrollmentId == null && !ApiClient.IsClientReady, "Disconnect clears authenticated enrollment and readiness: " + number);
            AssertRemoteAbsent();
            _require(AnomalyResources.TryGetAudioClip(AssetAddress.Parse("game/cassie/.g7"), out var local) && local == _localG7,
                "Disconnect exposes the boot-loaded local .g7 replacement: " + number);
            _require(_g7Line.Clip == _localG7 && _g7Line.Duration == _stockDuration,
                "Disconnect restores local CASSIE binding without losing true stock baseline: " + number);
            _log("network-disconnected", new { cycle = number, remoteObjectsDestroyed = true, localG7 = local.GetInstanceID() });
            var lifetime = RecordLifetime("disconnect-" + number);
            _require(lifetime["remotePacks"] == 0 && lifetime["nativePacks"] == lifetime["localPacks"] &&
                lifetime["cacheLeases"] == lifetime["localPacks"], "Disconnect releases all remote native pack ownership and cache leases: " + number);
        });
    }

    private void AddConsumerScenarios()
    {
        Request("prepare-player", () => "prepare", _ => { });
        Wait("classd-role", () => ApiLocalPlayer.Instance?.CurrentRole?.RoleTypeId == Il2CppPlayerRoles.RoleTypeId.ClassD, 20);
        Request("grant-icon-item", () => "grant", _ => { });
        Wait("custom-item-arrival", () => ApiLocalPlayer.Instance?.Inventory.Items.Any(item => item.TypeId == _customItemId) == true, 20);
        Add("custom-item-icon", () =>
        {
            var item = ApiLocalPlayer.Instance.Inventory.Items.First(item => item.TypeId == _customItemId);
            _require(item.Icon != null && item.Icon == _sprite.texture, "Granted item icon uses the native bundle Sprite's complete texture");
            _log("network-item-icon", new { typeId = item.TypeId, iconId = item.Icon.GetInstanceID(), spriteId = _sprite.GetInstanceID() });
        });
        Request("texture-override", TextureCommand, _ => { });
        Wait("texture-material-binding", () => _textureMessage && _textureTarget != null && _textureTarget.materials[0].GetTexture("_BaseColorMap") == _texture, 20);
        Add("texture-confirmed", () => _require(_textureOriginal != _texture, "Authenticated texture command changes the original player material binding"));
        Request("texture-clear", () => "clear " + ApiLocalPlayer.Instance.ReferenceHub.netId, _ => { });
        Wait("texture-restored", () => _textureTarget != null && _textureTarget.materials[0].GetTexture("_BaseColorMap") == _textureOriginal, 20);
        Request("announcer-playtone", () => "playtone", _ => { });
        Request("visual-spawn", () => "spawn", json => _visualId = json.GetProperty("visualId").GetUInt32());
        Wait("visual-spawn-arrival", () => VisualRoot(_visualId) != null, 20);
        Add("visual-before-update", () =>
        {
            AssertVisual(_visualId); _lastVisualRoot = VisualRoot(_visualId);
            _updatedRootId = _lastVisualRoot.GetInstanceID(); _beforeUpdatePosition = _lastVisualRoot.transform.position;
        });
        Request("visual-update", () => "update", _ => { });
        Wait("visual-update-arrival", () =>
        {
            var root = VisualRoot(_visualId);
            return root != null && Vector3.Distance(root.transform.localScale, Vector3.one * 1.5f) < 0.0001f &&
                Vector3.Distance(root.transform.position, _beforeUpdatePosition) > 0.1f;
        }, 20);
        Add("visual-update-bindings", () =>
        {
            _require(VisualRoot(_visualId).GetInstanceID() == _updatedRootId && _visualUpdates >= 2,
                "Authenticated visual update changes the existing trusted hierarchy");
            AssertVisual(_visualId); NeedsCapture = true;
        });
        Request("visual-despawn", () => "despawn", json => _require(json.GetProperty("visualId").GetUInt32() == 0, "Server despawn relinquishes the visual id"));
        Wait("visual-despawn-arrival", () => Instances.Count == 0 && _lastVisualRoot == null, 20);
        Request("visual-respawn", () => "respawn", json => _visualId = json.GetProperty("visualId").GetUInt32());
        Wait("visual-respawn-arrival", () => VisualRoot(_visualId) != null, 20);
        Add("visual-before-scope-dispose", () => _lastVisualRoot = VisualRoot(_visualId));
        Request("visual-scope-dispose", () => "dispose", json => _require(json.GetProperty("scopeDisposed").GetBoolean(), "Server owner scope disposed"));
        Wait("visual-scope-dispose-arrival", () => Instances.Count == 0 && _lastVisualRoot == null, 20);
        Request("visual-latejoin-resident", () => "respawn", json => _lateJoinId = _visualId = json.GetProperty("visualId").GetUInt32());
        Wait("visual-latejoin-resident-arrival", () => VisualRoot(_lateJoinId) != null, 20);
        Add("visual-latejoin-retained", () => _lastVisualRoot = VisualRoot(_lateJoinId));
    }

    private void AddRoundScenario()
    {
        Add("round-baseline", () =>
        {
            _previousConnection = Il2CppMirror.NetworkClient.connection.Pointer;
            _previousHub = ApiLocalPlayer.Instance.ReferenceHub.netId;
            _previousEnrollment = ApiClient.EnrollmentId;
            _previousGeneration = (ulong)_visual.GetField("_generation", All).GetValue(null);
            _sawConsent = false; _decisionAt = 0; _chunks = _snapshots = 0; _chunkBytes = 0;
            _lastVisualRoot = VisualRoot(_visualId);
        });
        Request("native-round-restart", () => "round", _ => { });
        Wait("round-reactivation", () => PumpConnection() && ApiLocalPlayer.Instance?.ReferenceHub?.netId != _previousHub &&
            IsActivated && _snapshots > 0 && (ulong)_visual.GetField("_generation", All).GetValue(null) > _previousGeneration, 150);
        Poll("round-server-activated", json => json.GetProperty("readiness").GetString() == "Activated", json =>
        {
            _require(Il2CppMirror.NetworkClient.connection.Pointer == _previousConnection && ApiClient.EnrollmentId == _previousEnrollment,
                "Native round restart retains transport and authenticated enrollment");
            _require(!_sawConsent && _chunks == 0 && json.GetProperty("requestedFiles").GetInt32() == 0,
                "Round reactivation reuses approved cached pairs without another prompt or download");
            _require(json.GetProperty("visualId").GetUInt32() == 0 && Instances.Count == 0 && _lastVisualRoot == null,
                "Round generation removes the previous session's decorative instances");
            AssertAssets(); _roundVerified = true;
            _log("network-round", new { oldHub = _previousHub, newHub = ApiLocalPlayer.Instance.ReferenceHub.netId,
                previousGeneration = _previousGeneration, generation = (ulong)_visual.GetField("_generation", All).GetValue(null),
                requestedFiles = 0, receivedChunks = _chunks });
        });
        Wait("round-vocabulary", () => Announcer.IsValid(Word), 20);
        Request("round-visual-respawn", () => "respawn", json => _visualId = json.GetProperty("visualId").GetUInt32());
        Wait("round-visual-arrival", () => VisualRoot(_visualId) != null, 20);
        Add("round-visual-bindings", () => { AssertVisual(_visualId); _lastVisualRoot = VisualRoot(_visualId); });
    }

    private bool PumpConnection()
    {
        object instance = _prompt.GetField("_instance", All).GetValue(null);
        if (instance != null && (bool)instance.GetType().GetField("_isVisible", All).GetValue(instance))
        {
            string accept = ((TMP_Text)instance.GetType().GetField("_acceptLabel", All).GetValue(instance)).text;
            if (accept == "Trust and connect")
            {
                _log("network-trust", new { cycle = _cycle, endpoint = Server });
                Submit(instance, "_accept"); return false;
            }
            if (accept == "Allow content")
            {
                if (!_sawConsent)
                {
                    _sawConsent = true; _decisionAt = Time.realtimeSinceStartupAsDouble + 2;
                    _log("network-consent-prompt", new { cycle = _cycle });
                }
                if (Time.realtimeSinceStartupAsDouble < _decisionAt) return false;
                _require(!IsActivated && _chunks == 0, "Content is unpublished and undownloaded before the explicit choice");
                Submit(instance, _approve ? "_accept" : "_cancel");
                _consentChoices++;
                _log("network-consent-choice", new { cycle = _cycle, approve = _approve });
                return false;
            }
        }
        return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) && Il2Cpp.StaticUnityMethods.IsPlaying;
    }

    private static void Submit(object instance, string field) =>
        ((Button)instance.GetType().GetField(field, All).GetValue(instance)).OnSubmit(new BaseEventData(EventSystem.current));

    private void CaptureStockAndLocalG7()
    {
        _g7Line = FindLine(".g7");
        _require(_g7Line != null && _g7Line.Clip != null, "Actual CASSIE database contains the stock .g7 line");
        var saved = (IDictionary)_cassiePatch.GetField("_originalClips", All).GetValue(null);
        _stockG7 = saved.Contains(_g7Line) ? (AudioClip)saved[_g7Line] : _g7Line.Clip;
        _stockDuration = _g7Line.Duration;
        _require(_stockG7 != null && _stockG7 != _localG7 && Math.Abs(_stockDuration - 1.5f) < 0.00001f,
            "The preserved stock .g7 baseline is distinct from local content with unchanged 1.5-second schedule");
        // Invoke the existing scheduling prefix against a real stock line to
        // witness its consumer binding, without directly assigning Clip.
        _cassiePatch.GetMethod("Prefix", All).Invoke(null, new object[] { _g7Line });
        _require(_g7Line.Clip == _localG7 && _g7Line.Duration == _stockDuration, "Ordinary CASSIE scheduling selects the local .g7 layer");
        _log("network-cassie-baseline", new { stockId = _stockG7.GetInstanceID(), localId = _localG7.GetInstanceID(), duration = _stockDuration });
    }

    private void AssertAssets()
    {
        _require(IsActivated, "Production content coordinator reports transactional activation");
        _require(AnomalyResources.TryGetTexture(Address("textures/checker"), out _texture), "Network Texture2D resolves");
        _require(AnomalyResources.TryGetSprite(Address("sprites/checker"), out _sprite), "Network Sprite resolves");
        _require(AnomalyResources.TryGetMesh(Address("meshes/quad"), out _mesh), "Network Mesh resolves");
        _require(AnomalyResources.TryGetAudioClip(Address("audio/tone"), out _tone), "Network AudioClip resolves");
        _require(_texture.width == 256 && _texture.height == 256 && _sprite.rect.width == 256 && _sprite.rect.height == 256 &&
            _mesh.vertexCount == 4 && _mesh.triangles.Length == 6 && _tone.samples == 12000 && _tone.frequency == 48000 && _tone.channels == 2,
            "All native network assets match the validated index metadata");
        _require(AnomalyResources.TryGetAudioClip(AssetAddress.Parse("game/cassie/.g7"), out _remoteG7) && _remoteG7 != _localG7,
            "Server .g7 wins over the distinct local .g7 pack for the active session");
        _require((bool)_manager.GetMethod("IsRemoteAsset", All).Invoke(null, new object[] { AssetAddress.Parse("game/cassie/.g7"), AssetKind.AudioClip }),
            "Effective .g7 is owned by the authenticated remote layer");
        _require(_remoteG7.samples == 72000 && _remoteG7.frequency == 44100 && _remoteG7.channels == 1,
            "Remote .g7 preserves physical stock sample metadata");
        _cassiePatch.GetMethod("Prefix", All).Invoke(null, new object[] { _g7Line });
        _require(_g7Line.Clip == _remoteG7 && _g7Line.Duration == _stockDuration, "CASSIE selects server .g7 without changing its stock schedule");
        _remoteObjects.AddRange(new Object[] { _texture, _sprite, _mesh, _tone, _remoteG7 });
        _log("network-native-assets", new { cycle = _cycle, textureId = _texture.GetInstanceID(), spriteId = _sprite.GetInstanceID(),
            meshId = _mesh.GetInstanceID(), toneId = _tone.GetInstanceID(), remoteG7 = _remoteG7.GetInstanceID(), localG7 = _localG7.GetInstanceID() });
        RecordLifetime("activated-" + _cycle + (_roundVerified ? "-round" : ""));
    }

    private void AssertRemoteAbsent()
    {
        _require(!IsActivated && Instances.Count == 0 && !Announcer.IsValid(Word), "Unapproved/disconnected content has no activation, visual instances, or server vocabulary");
        _require(!AnomalyResources.TryGetTexture(Address("textures/checker"), out _) && !AnomalyResources.TryGetSprite(Address("sprites/checker"), out _) &&
            !AnomalyResources.TryGetMesh(Address("meshes/quad"), out _) && !AnomalyResources.TryGetAudioClip(Address("audio/tone"), out _),
            "All server fixture addresses are unavailable outside an approved session");
    }

    private void AssertVisual(uint id)
    {
        var root = VisualRoot(id);
        _require(root != null && root.activeInHierarchy, "Network decorative root exists and is active: " + id);
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        _require(filters.Length == 1 && filters[0].sharedMesh == _mesh && renderers.Length == 1,
            "Trusted network hierarchy binds the validated quad mesh");
        var material = renderers[0].sharedMaterial;
        _require(material != null && material.shader != null && material.shader.name == "HDRP/Unlit" &&
            material.GetTexture("_UnlitColorMap") == _texture, "Trusted built-in material binds the validated texture");
        _require(root.GetComponentsInChildren<Collider>(true).Length == 0 && root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
            "Decorative hierarchy contains no scripts or colliders");
        _log("network-visual", new { id, root = root.GetInstanceID(), mesh = _mesh.GetInstanceID(), shader = material.shader.name,
            x = root.transform.position.x, y = root.transform.position.y, z = root.transform.position.z });
    }

    private string TextureCommand()
    {
        var player = ApiLocalPlayer.Instance;
        _textureMessage = false;
        _textureTarget = player.ReferenceHub.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.StartsWith("jumpsuit", StringComparison.OrdinalIgnoreCase) &&
                renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials[0] != null && renderer.sharedMaterials[0].HasProperty("_BaseColorMap"))
            .OrderByDescending(renderer => renderer.gameObject.activeInHierarchy).FirstOrDefault();
        _require(_textureTarget != null, "Stock ClassD jumpsuit provides the documented material target");
        _textureOriginal = _textureTarget.materials[0].GetTexture("_BaseColorMap");
        var names = new List<string>();
        for (Transform node = _textureTarget.transform; node != player.Transform; node = node.parent)
        {
            if (node == null) throw new InvalidOperationException("Texture target is outside its networked player hierarchy.");
            names.Add(node.name);
        }
        names.Reverse();
        string path = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("/", names)));
        _log("network-texture-target", new { netId = player.ReferenceHub.netId, renderer = _textureTarget.name, componentPath = string.Join("/", names) });
        return $"texture {player.ReferenceHub.netId} {path} 0";
    }

    private bool IsActivated => (bool)_content.GetProperty("IsActivated", All).GetValue(null);
    private IDictionary Instances => (IDictionary)_visual.GetField("_instances", All).GetValue(null);
    private GameObject VisualRoot(uint id)
    {
        object entry = Instances[id];
        return entry == null ? null : (GameObject)entry.GetType().GetField("Root", All).GetValue(entry);
    }
    private static AssetAddress Address(string path) => AssetAddress.Parse("mods/fixture/" + path);
    private static CassieLine FindLine(string name) => Announcer.LineDatabase?.AllLines?
        .FirstOrDefault(line => line != null && string.Equals(line.ApiName, name, StringComparison.OrdinalIgnoreCase));

    private void OnMessage(IAnomalyMessage message)
    {
        if (message is SmokeResponse response) _responses[response.Token] = response;
        if (message is ContentCatalogMessage catalog)
        {
            _catalogHash = Convert.ToHexString(catalog.Catalog.CatalogSha256);
            _packIds = catalog.Catalog.Packs.Select(pack => pack.PackId).ToArray();
        }
        if (message is FileManifestMessage manifest)
        {
            _manifests++; _manifestBytes = manifest.Files.Sum(file => (long)file.Size);
            _manifestHash = Convert.ToHexString(manifest.ManifestSha256);
        }
        if (message is FileChunkMessage chunk)
        {
            _chunks++; _chunkBytes += chunk.Data.Length;
            if (_negative && _interruptArmed && chunk.TotalChunks > 1 && chunk.ChunkIndex < chunk.TotalChunks - 1)
            {
                _interruptArmed = false; _interrupted = true; _interruptedBytes = _chunkBytes;
                var transfer = Type.GetType("Anomaly.Client.FileTransfer.FileTransferClient, Anomaly.Client", true);
                _interruptedState = transfer.GetField("_state", All).GetValue(null).ToString();
                _log("network-transfer-interrupted", new { cycle = _cycle, state = _interruptedState, receivedBytes = _chunkBytes,
                    offeredBytes = _manifestBytes, chunkIndex = chunk.ChunkIndex, totalChunks = chunk.TotalChunks });
                ApiClient.Disconnect();
            }
        }
        if (message is DecorativeVisualSnapshotMessage) _snapshots++;
        if (message is DecorativeVisualUpdateMessage) _visualUpdates++;
        if (message is AssetOverrideTextureMessage) _textureMessage = true;
    }

    private void Add(string name, Action action) => Wait(name, () => { action(); return true; }, 10);
    private void Wait(string name, Func<bool> predicate, double seconds) => _steps.Enqueue((name, predicate, seconds));
    private void Request(string name, Func<string> command, Action<JsonElement> inspect)
    {
        int token = 0;
        Add(name + "-send", () => { token = ++_token; AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = command() }); });
        Wait(name + "-reply", () =>
        {
            if (!_responses.Remove(token, out var response)) return false;
            _require(response.Success, name + " server response: " + response.Details);
            using var json = JsonDocument.Parse(response.Details);
            inspect(json.RootElement); return true;
        }, 30);
    }
    private void Poll(string name, Func<JsonElement, bool> predicate, Action<JsonElement> inspect)
    {
        int token = 0; double next = 0;
        Wait(name, () =>
        {
            // Content prompts can arrive after normal gameplay readiness.
            PumpConnection();
            if (token != 0)
            {
                if (!_responses.Remove(token, out var response)) return false;
                _require(response.Success, name + " server response: " + response.Details);
                using var json = JsonDocument.Parse(response.Details);
                if (predicate(json.RootElement)) { inspect(json.RootElement); return true; }
                token = 0; next = Time.realtimeSinceStartupAsDouble + 0.25;
            }
            if (Time.realtimeSinceStartupAsDouble < next) return false;
            token = ++_token; AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = "status" });
            return false;
        }, 60);
    }
    private object Diagnostics() => new { step = _stepName, cycle = _cycle, connected = ApiClient.IsConnected,
        ready = ApiClient.IsClientReady, enrolled = !string.IsNullOrEmpty(ApiClient.EnrollmentId), activated = IsActivated,
        prompted = _sawConsent, chunks = _chunks, bytes = _chunkBytes, manifests = _manifests, snapshots = _snapshots, visuals = Instances.Count };
    private void Release()
    {
        if (_released) return;
        _released = true; AnomalyMessaging.MessageReceived -= OnMessage; _capability.Dispose();
    }

    private Dictionary<string, long> RecordLifetime(string stage)
    {
        var values = new Dictionary<string, long>();
        var native = (IDictionary)_manager.GetField("NativePacks", All).GetValue(null);
        values["nativePacks"] = native.Count;
        values["nativeOwners"] = native.Values.Cast<object>().Sum(pack => (long)(int)pack.GetType().GetField("Owners", All).GetValue(pack));
        foreach (string layerName in new[] { "local", "remote" })
        {
            object layer = _manager.GetField("_" + layerName, All).GetValue(null);
            values[layerName + "Packs"] = ((ICollection)layer.GetType().GetField("Packs", All).GetValue(layer)).Count;
        }
        lock (_manager.GetField("CacheLock", All).GetValue(null))
        {
            var copies = (IDictionary)_manager.GetField("Cache", All).GetValue(null);
            values["cachedCopies"] = copies.Count; values["cacheLeases"] = 0; values["verifiedFileLocks"] = 0; values["cachedEncodedBytes"] = 0;
            foreach (object copy in copies.Values)
            {
                values["cacheLeases"] += (int)copy.GetType().GetField("Leases", All).GetValue(copy);
                values["cachedEncodedBytes"] += (long)copy.GetType().GetField("Bytes", All).GetValue(copy);
                if (copy.GetType().GetField("Lock", All).GetValue(copy) != null) values["verifiedFileLocks"]++;
            }
        }
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            values["workingSetBytes"] = process.WorkingSet64;
            values["privateBytes"] = process.PrivateMemorySize64;
        }
        values["managedBytes"] = GC.GetTotalMemory(forceFullCollection: false);
        values["nativeLoadAttempts"] = NativeLoadAttempts;
        _lifetimeSnapshots.Add(new { stage, utc = DateTimeOffset.UtcNow, values });
        _log("network-lifetime", new { stage, values });
        return values;
    }

    private static long NativeLoadAttempts => (long)Type.GetType("Anomaly.Client.Assets.NativeAssetBundle, Anomaly.Client", true)
        .GetProperty("LoadAttempts", All).GetValue(null);

    private void WriteResult(string status, string error) => File.WriteAllText(Path.Combine(_output, "network.json"),
        JsonSerializer.Serialize(new { status, error, mode = _negative ? "negative" : "positive", endpoint = Server, cycles = _cycles, roundReactivated = _roundVerified,
            explicitConsentChoices = _consentChoices, productionTransferOnly = true, serverLocalStockPrecedence = !_negative && status == "passed",
            interruptedTransfer = _interrupted, invalidBundleRejectedBeforeNativeLoad = _validationFailure && !_unexpectedPublication,
            humanAudibilityProven = false, lifetimeSnapshots = _lifetimeSnapshots, diagnostic = Diagnostics() }, new JsonSerializerOptions { WriteIndented = true }));
}
