using System.Text;
using System.Text.Json;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppInventorySystem.Items.Usables;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ApiClient = Anomaly.Client.Api.Features.Client;
using ApiLocalPlayer = Anomaly.Client.Api.Features.LocalPlayer;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private bool _serverApiSmoke;
    private IDisposable _smokeCapability;
    private readonly Dictionary<int, SmokeResponse> _smokeResponses = new();
    private int _requestToken;
    private bool _sawContentPrompt;
    private double _contentDecisionAt;
    private int _customItemId;
    private int _usedBefore;
    private Vector3 _expectedMotionPosition;
    private Renderer _textureTarget;
    private Texture _textureOriginal;
    private bool _textureMessageSeen;

    private void InitializeServerApiSmoke()
    {
        _serverApiSmoke = Environment.GetEnvironmentVariable("ANOMALY_SERVER_API_SMOKE") == "1";
        if (!_serverApiSmoke && !_remoteScriptingSmoke) return;
        AnomalyMessageRegistry.Register(SmokeRequest.Name, () => new SmokeRequest());
        AnomalyMessageRegistry.Register(SmokeResponse.Name, () => new SmokeResponse());
        _smokeCapability = ClientCapabilities.Register(new CapabilityDescriptor("anomaly-smoke:client", 1, 0));
        AnomalyMessaging.MessageReceived += OnSmokeMessage;
    }

    private void ShutdownServerApiSmoke()
    {
        ReleaseFacilityViewHud();
        AnomalyMessaging.MessageReceived -= OnRecipientMessage;
        AnomalyMessaging.MessageReceived -= OnSmokeMessage;
        _smokeCapability?.Dispose();
    }

    private void OnSmokeMessage(IAnomalyMessage message)
    {
        if (message is SmokeResponse response) _smokeResponses[response.Token] = response;
        if (message is AssetOverrideTextureMessage) _textureMessageSeen = true;
    }

    private void EnqueueServerApiSmoke()
    {
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var endpoint) ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("Server API smoke requires an explicit loopback endpoint.");
        string mode = Environment.GetEnvironmentVariable("ANOMALY_SERVER_API_SMOKE_MODE") ?? "full";
        if (mode == "facility-views") { EnqueueFacilityViews(server); return; }
        if (mode == "recipients-sender" || mode == "recipients-observer") { EnqueueRecipients(server, mode.Substring("recipients-".Length)); return; }
        if (mode == "facility") { EnqueueFacilityMatrix(server); return; }
        bool connectionOnly = mode == "connection";
        bool roundContent = mode == "round-content";
        IntPtr previousConnection = IntPtr.Zero;
        uint previousHub = 0;
        ulong previousGeneration = 0;
        string previousEnrollment = null;
        int cycles = connectionOnly || roundContent ? 2 : mode == "approve" || mode == "changed" ? 1 : 3;
        Add("server-api-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Server API Smoke");
                Submit();
                Require(!IsVisible(), "Fresh isolated nickname was saved");
            }
        });
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            int current = cycle;
            bool approve = !connectionOnly && (roundContent || mode == "approve" || mode == "changed" || cycle > 1);
            double deliberation = mode == "full" && cycle <= 2 ? 35 : 2;
            Add("server-api-connect-" + current, () =>
            {
                _sawContentPrompt = false;
                _contentDecisionAt = 0;
                if (roundContent && current > 1)
                {
                    previousConnection = Il2CppMirror.NetworkClient.connection.Pointer;
                    previousHub = ApiLocalPlayer.Instance.ReferenceHub.netId;
                    previousEnrollment = ApiClient.EnrollmentId;
                    previousGeneration = CurrentFacilitySnapshot().GenerationId;
                    AnomalyMessaging.Send(new SmokeRequest { Token = ++_requestToken, Command = "facility-restart:pristine" });
                    return;
                }
                ApiClient.MainMenu.ChangeMenu(0);
                ApiClient.Connect(server);
            });
            AddWait("server-api-ready-" + current, () =>
            {
                if (!PumpServerApiConnection(current, approve, deliberation)) return false;
                return !roundContent || current == 1 ||
                    CurrentFacilitySnapshot()?.GenerationId > previousGeneration &&
                    ApiLocalPlayer.Instance?.ReferenceHub?.netId != previousHub;
            }, 150);
            Add("server-api-facility-" + current, () =>
            {
                Require(ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId), "Authenticated gameplay readiness");
                if (mode == "changed" || mode == "full" && current <= 2) Require(_sawContentPrompt, "New or previously declined content requires an explicit consent choice");
                if (mode == "full" && current == 3) Require(!_sawContentPrompt, "Matching cached content approval needs no new prompt");
                if (roundContent && current > 1)
                {
                    Require(Il2CppMirror.NetworkClient.connection.Pointer == previousConnection,
                        "Native round restart retains the same Mirror connection");
                    Require(ApiClient.EnrollmentId == previousEnrollment,
                        "Native round restart retains authenticated enrollment");
                    Require(!_sawContentPrompt, "Matching approved content reactivates without another prompt after native restart");
                }
                SaveFacilityEvidence(current);
                Capture("server-api-connected-" + current);
            });
            if (!connectionOnly)
            {
                AddSmokePoll("content-state-" + current, "status", json =>
                {
                    string readiness = json.GetProperty("readiness").GetString();
                    return readiness == (approve ? "Activated" : "Declined") && json.GetProperty("capability").GetBoolean();
                });
                if (approve)
                {
                    Add("content-overlay-hidden-" + current, () =>
                    {
                        var overlayType = Type.GetType("Anomaly.Client.Components.FileTransferOverlay, Anomaly.Client", true);
                        var overlay = overlayType.GetField("_instance", All).GetValue(null);
                        var root = overlay == null ? null : (GameObject)overlay.GetType().GetField("_root", All).GetValue(overlay);
                        Require(root == null || !root.activeSelf, "Completed optional content hides the transfer overlay");
                    });
                    AddSmokeRequest("prepare-" + current, "prepare", _ => { });
                    AddWait("fpc-role-" + current, () => ApiLocalPlayer.Instance?.CurrentRole?.RoleTypeId == Il2CppPlayerRoles.RoleTypeId.ClassD, 20);
                    AddSmokeRequest("exercise-" + current, "exercise", json =>
                    {
                        Require(json.GetProperty("granted").GetBoolean(), "Server custom item grant succeeded after activation");
                        Require(json.GetProperty("moved").GetBoolean(), "Server accepted the owned motion discontinuity");
                        _usedBefore = json.GetProperty("usedCount").GetInt32();
                        var position = json.GetProperty("position");
                        _expectedMotionPosition = new Vector3(position.GetProperty("x").GetSingle(),
                            position.GetProperty("y").GetSingle(), position.GetProperty("z").GetSingle());
                        _customItemId = json.GetProperty("itemId").GetInt32();
                        File.WriteAllText(Path.Combine(_output, "exercise-" + current + ".json"), json.GetRawText());
                    });
                    AddWait("owned-motion-arrival-" + current, () =>
                        Vector3.Distance(ApiLocalPlayer.Instance.Position, _expectedMotionPosition) < 0.75f, 15);
                    AddWait("announcer-vocabulary-" + current, () => Announcer.IsValid("anomaly_smoke_tone"), 15);
                    AddWait("custom-item-arrival-" + current, () => ApiLocalPlayer.Instance?.Inventory.Items.Any(item => item.TypeId == _customItemId) == true, 20);
                    Add("custom-item-render-" + current, () =>
                    {
                        var item = ApiLocalPlayer.Instance.Inventory.Items.First(item => item.TypeId == _customItemId);
                        Require(item.Icon != null, "Custom inventory item has its materialized icon");
                        ApiLocalPlayer.Instance.InventoryBase.ClientSelectItem(item.Serial);
                        Capture("server-api-custom-item-" + current);
                    });
                    AddWait("custom-item-equipped-" + current, () => ApiLocalPlayer.Instance?.CurrentItem?.TypeId == _customItemId, 15);
                    Add("native-item-use-" + current, () =>
                    {
                        var item = ApiLocalPlayer.Instance.CurrentItem;
                        Il2CppMirror.NetworkClient.Send(new StatusMessage(StatusMessage.StatusType.Start, item.Serial), 0);
                    });
                    AddSmokePoll("native-item-used-" + current, "status", json => json.GetProperty("usedCount").GetInt32() > _usedBefore);
                    AddTextureScenario(current);
                    foreach (int yaw in new[] { 0, 90, 180, 270 })
                    {
                        int view = yaw;
                        AddSmokeRequest("look-" + current + "-" + view, "look:" + view, _ => { });
                        Add("view-" + current + "-" + view, () => Capture("server-api-view-" + current + "-" + view));
                    }
                    AddSmokeRequest("clear-" + current, "clear", _ => { });
                    AddWait("texture-restored-" + current, () => _textureTarget.materials[0].GetTexture("_BaseColorMap") == _textureOriginal, 15);
                }
                AddSmokeRequest("report-" + current, "report", json =>
                    File.WriteAllText(Path.Combine(_output, "server-report-" + current + ".json"), json.GetRawText()));
            }
            if (roundContent && current == 1) continue;
            Add("server-api-disconnect-" + current, () =>
            {
                Call("HideAll");
                _disconnectedEventsBeforeStop = _disconnectedEvents;
                ApiClient.Disconnect();
            });
            AddWait("server-api-menu-" + current, () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 60);
            Add("server-api-cleanup-" + current, () =>
            {
                Require(ApiClient.EnrollmentId == null && !ApiClient.IsClientReady, "Disconnect clears identity disclosure and readiness");
                Require(_disconnectedEvents == _disconnectedEventsBeforeStop + 1, "Disconnect event occurs once");
                AssertCanvasLifetime();
            });
        }
        Add("server-api-complete", () =>
        {
            string lifecycle = roundContent ? "2 player sessions on one authenticated connection" : cycles + " real connection cycles";
            File.WriteAllText(Path.Combine(_output, "result.txt"), $"Server API {mode} smoke passed; {lifecycle} and {_captures} engine screenshots.\n");
            Application.Quit();
        });
    }

    private bool PumpServerApiConnection(int cycle, bool approve, double deliberation)
    {
        if (Instance() != null && IsVisible())
        {
            var accept = (TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance());
            if (accept.text == "Trust and connect") { Capture("server-api-trust-" + cycle); Submit(); return false; }
            if (accept.text == "Allow content")
            {
                if (!_sawContentPrompt)
                {
                    _sawContentPrompt = true;
                    _contentDecisionAt = Time.realtimeSinceStartupAsDouble + deliberation;
                    Capture("server-api-consent-" + cycle);
                }
                if (Time.realtimeSinceStartupAsDouble < _contentDecisionAt) return false;
                if (deliberation > 30) Require(ApiClient.IsConnected, "Reading optional consent beyond 30 seconds preserves connection");
                if (approve) Submit();
                else PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
                return false;
            }
        }
        return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) && Il2Cpp.StaticUnityMethods.IsPlaying;
    }

    private void AddSmokeRequest(string name, string command, Action<JsonElement> inspect)
    {
        int token = 0;
        Add(name + "-send", () => { token = ++_requestToken; AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = command }); });
        AddWait(name + "-reply", () => _smokeResponses.ContainsKey(token), 30);
        Add(name + "-assert", () =>
        {
            SmokeResponse response = _smokeResponses[token];
            _smokeResponses.Remove(token);
            Require(response.Success, name + ": " + response.Details);
            using var json = JsonDocument.Parse(response.Details);
            inspect(json.RootElement);
        });
    }

    private void AddSmokePoll(string name, string command, Func<JsonElement, bool> predicate)
    {
        int token = 0;
        AddWait(name, () =>
        {
            if (token != 0)
            {
                if (!_smokeResponses.TryGetValue(token, out var response)) return false;
                _smokeResponses.Remove(token);
                Require(response.Success, name + ": " + response.Details);
                using var json = JsonDocument.Parse(response.Details);
                if (predicate(json.RootElement)) return true;
            }
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = command });
            return false;
        }, 45);
    }

    private void AddTextureScenario(int cycle)
    {
        int token = 0;
        Add("texture-send-" + cycle, () =>
        {
            var player = ApiLocalPlayer.Instance;
            _textureMessageSeen = false;
            _textureTarget = player.ReferenceHub.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.StartsWith("jumpsuit", StringComparison.OrdinalIgnoreCase) &&
                    renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials[0] != null && renderer.sharedMaterials[0].HasProperty("_BaseColorMap"))
                .OrderByDescending(renderer => renderer.gameObject.activeInHierarchy)
                .First();
            _textureOriginal = _textureTarget.materials[0].GetTexture("_BaseColorMap");
            var names = new List<string>();
            for (Transform node = _textureTarget.transform; node != player.Transform; node = node.parent)
            {
                if (node == null) throw new InvalidOperationException("Renderer is outside the networked player hierarchy.");
                names.Add(node.name);
            }
            names.Reverse();
            string path = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("/", names)));
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = $"texture:{player.ReferenceHub.netId}:{path}:0" });
        });
        AddWait("texture-reply-" + cycle, () => _smokeResponses.ContainsKey(token), 20);
        AddWait("texture-applied-" + cycle, () =>
        {
            var response = _smokeResponses[token];
            Require(response.Success, response.Details);
            using var json = JsonDocument.Parse(response.Details);
            var address = Anomaly.Shared.Assets.AssetAddress.Parse(json.RootElement.GetProperty("textureAddress").GetString());
            bool loaded = AnomalyResources.TryGetTexture(address, out var texture);
            var actual = _textureTarget.materials[0].GetTexture("_BaseColorMap");
            File.WriteAllText(Path.Combine(_output, "texture-state-" + cycle + ".json"), JsonSerializer.Serialize(new
            {
                received = _textureMessageSeen, loaded, renderer = _textureTarget.name,
                expectedId = texture == null ? 0 : texture.GetInstanceID(), actualId = actual == null ? 0 : actual.GetInstanceID()
            }));
            return loaded && actual == texture;
        }, 20);
    }

    private void SaveFacilityEvidence(int cycle)
    {
        var recipeType = Type.GetType("Anomaly.Client.Internal.Map.FacilityRecipeClient, Anomaly.Client", true);
        var snapshot = (FacilitySnapshotMessage)recipeType.GetProperty("CurrentSnapshot", All).GetValue(null);
        Require(snapshot != null && (bool)recipeType.GetProperty("IsReady", All).GetValue(null), "Authenticated facility completed before client readiness");
        File.WriteAllText(Path.Combine(_output, "facility-" + cycle + ".json"), JsonSerializer.Serialize(new
        {
            snapshot.GenerationId, snapshot.Seed, digest = Convert.ToBase64String(snapshot.Digest),
            roomOperations = snapshot.Recipe.Count, atlasEdits = snapshot.AtlasEdits.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
        var diagnostics = Type.GetType("Anomaly.Client.Internal.Map.FacilityGenerationDiagnostics, Anomaly.Client", true);
        File.WriteAllText(Path.Combine(_output, "facility-rooms-" + cycle + ".json"),
            (string)diagnostics.GetMethod("Capture", All).Invoke(null, null));
    }
}
