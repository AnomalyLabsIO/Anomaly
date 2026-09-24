using System.Globalization;
using System.Text.Json;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Il2CppMapGeneration;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private IDisposable _facilityViewHud;
    private bool _facilityViewHudWasVisible;
    private JsonElement[] _facilityViewRooms;
    private Vector3 _facilityViewPosition;
    private Il2CppGameCore.RoundStart _facilityLobby;
    private bool _facilityLobbyWasEnabled;
    private GameObject _facilityLobbyWindow;
    private bool _facilityLobbyWindowWasActive;
    private GameObject _facilityLobbyBackground;
    private bool _facilityLobbyBackgroundWasActive;

    private void EnqueueFacilityViews(string server)
    {
        Add("facility-views-connect", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Facility Views");
                Submit();
            }
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("facility-views-ready", () => PumpServerApiConnection(1, false, 0), 180);
        AddSmokePoll("facility-views-content-declined", "report", json =>
        {
            PumpServerApiConnection(1, false, 0);
            return json.GetProperty("readiness").GetString() == "Declined";
        });
        Add("facility-views-generation-evidence", () => SaveFacilityEvidence(1));
        AddFacilityViews(1, "current");
        Add("facility-views-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Facility views captured two generated rooms at four orientations; inspect PNGs alongside room descriptors.\n");
            ApiClient.Disconnect();
            Application.Quit();
        });
    }

    // May also be queued by the matrix after a completed generation, before its next restart.
    private void AddFacilityViews(int cycle, string scenario)
    {
        Add("facility-view-select-" + cycle, () => SelectFacilityViewRooms(cycle, scenario));
        AddSmokeRequest("facility-view-prepare-" + cycle, "prepare", _ => { });
        AddWait("facility-view-fpc-" + cycle, () => LocalPlayer.Instance?.FirstPersonMovementModule?.ModuleReady == true &&
            LocalPlayer.Instance.Camera != null && LocalPlayer.Instance.Camera.enabled, 30);
        Add("facility-view-hide-hud-" + cycle, () =>
        {
            _facilityViewHudWasVisible = Hud.IsVisible;
            _facilityViewHud = Hud.PushHide();
            // Harness-only: dispatch the native visibility delegate, then hide
            // the separately controlled lobby panel without starting the round.
            Il2Cpp.HideHUDController.ToggleHUD?.Invoke(false);
            _facilityLobby = Il2CppGameCore.RoundStart.singleton;
            Require(_facilityLobby != null && _facilityLobby.window != null, "Facility views find the native lobby window");
            _facilityLobbyWasEnabled = _facilityLobby.enabled;
            _facilityLobbyWindow = _facilityLobby.window;
            _facilityLobbyWindowWasActive = _facilityLobbyWindow.activeSelf;
            _facilityLobbyBackground = _facilityLobby._background?.gameObject;
            _facilityLobbyBackgroundWasActive = _facilityLobbyBackground != null && _facilityLobbyBackground.activeSelf;
            _facilityLobby.enabled = false;
            _facilityLobbyWindow.SetActive(false);
            if (_facilityLobbyBackground != null) _facilityLobbyBackground.SetActive(false);
        });
        for (int roomIndex = 0; roomIndex < 2; roomIndex++)
        {
            int target = roomIndex;
            foreach (int yaw in new[] { 0, 90, 180, 270 })
            {
                int direction = yaw;
                string name = "facility-view-" + cycle + "-" + scenario + "-room" + target + "-yaw" + direction;
                AddFacilityViewRequest(name, target, direction);
                double settleAt = 0;
                AddWait(name + "-arrival", () =>
                {
                    var player = LocalPlayer.Instance;
                    if (player?.Camera == null || Vector3.Distance(player.Position, _facilityViewPosition) > 0.8f ||
                        Math.Abs(Mathf.DeltaAngle(player.Camera.transform.eulerAngles.y, direction)) > 5f)
                    {
                        settleAt = 0;
                        return false;
                    }
                    if (settleAt == 0) settleAt = Time.realtimeSinceStartupAsDouble + 1;
                    return Time.realtimeSinceStartupAsDouble >= settleAt;
                }, 30);
                Add(name + "-capture", () =>
                {
                    Require(!Hud.IsVisible, "Facility screenshot hides the global HUD");
                    Require(_facilityLobbyWindow != null && !_facilityLobbyWindow.activeInHierarchy,
                        "Facility screenshot hides the native lobby window");
                    Capture(name);
                });
            }
        }
        Add("facility-view-restore-hud-" + cycle, () =>
        {
            ReleaseFacilityViewHud();
            Require(Hud.IsVisible == _facilityViewHudWasVisible, "Facility screenshot restores prior HUD visibility");
        });
    }

    private void SelectFacilityViewRooms(int cycle, string scenario)
    {
        var diagnostics = Type.GetType("Anomaly.Client.Internal.Map.FacilityGenerationDiagnostics, Anomaly.Client", true);
        using var capture = JsonDocument.Parse((string)diagnostics.GetMethod("Capture", All).Invoke(null, null));
        var rooms = capture.RootElement.GetProperty("Rooms").EnumerateArray().ToArray();
        var snapshot = CurrentFacilitySnapshot();
        int Value(JsonElement room, string key) => room.GetProperty(key).GetInt32();
        bool Mutated(JsonElement room) => snapshot.Recipe.Any(entry => entry.OpType == 0 && entry.DirtyMask != 0 &&
            entry.ZoneId == Value(room, "Zone") && entry.CoordsX == Value(room, "X") && entry.CoordsY == Value(room, "Y"));
        var primary = rooms.Where(room => Value(room, "Candidate") == (int)RoomName.Unnamed)
            .OrderByDescending(Mutated).ThenByDescending(room => Value(room, "Zone") == (int)FacilityZone.Entrance)
            .ThenBy(room => Value(room, "Zone")).ThenBy(room => Value(room, "X")).ThenBy(room => Value(room, "Y")).First();
        var entrance = rooms.First(room => Value(room, "Zone") == (int)FacilityZone.Entrance &&
            Value(room, "Candidate") == (int)RoomName.HczCheckpointToEntranceZone);
        _facilityViewRooms = new[] { primary.Clone(), entrance.Clone() };
        File.WriteAllText(Path.Combine(_output, "facility-view-targets-" + cycle + ".json"), JsonSerializer.Serialize(new
        {
            scenario, snapshot.GenerationId, rooms = _facilityViewRooms,
        }));
    }

    private void AddFacilityViewRequest(string name, int target, int yaw)
    {
        int token = 0;
        double next = 0;
        AddWait(name + "-move", () =>
        {
            if (token != 0)
            {
                if (!_smokeResponses.TryGetValue(token, out var response)) return false;
                _smokeResponses.Remove(token);
                using var parsed = JsonDocument.Parse(response.Details);
                if (response.Success)
                {
                    var position = parsed.RootElement.GetProperty("position");
                    _facilityViewPosition = new Vector3(position.GetProperty("x").GetSingle(), position.GetProperty("y").GetSingle(), position.GetProperty("z").GetSingle());
                    File.WriteAllText(Path.Combine(_output, name + ".json"), response.Details);
                    return true;
                }
                Require(parsed.RootElement.TryGetProperty("motionFailure", out var failure) && failure.GetString() == "ModuleNotReady", name + ": " + response.Details);
                token = 0;
            }
            if (Time.realtimeSinceStartupAsDouble < next) return false;
            next = Time.realtimeSinceStartupAsDouble + 0.5;
            JsonElement room = _facilityViewRooms[target];
            string command = string.Join(":", "view-room", room.GetProperty("Zone").GetInt32(),
                room.GetProperty("X").GetInt32(), room.GetProperty("Y").GetInt32(), yaw.ToString(CultureInfo.InvariantCulture));
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = command });
            return false;
        }, 30);
    }

    private void ReleaseFacilityViewHud()
    {
        if (_facilityLobbyBackground != null) _facilityLobbyBackground.SetActive(_facilityLobbyBackgroundWasActive);
        if (_facilityLobbyWindow != null) _facilityLobbyWindow.SetActive(_facilityLobbyWindowWasActive);
        if (_facilityLobby != null) _facilityLobby.enabled = _facilityLobbyWasEnabled;
        _facilityLobby = null;
        _facilityLobbyWindow = _facilityLobbyBackground = null;
        bool hadHudHandle = _facilityViewHud != null;
        _facilityViewHud?.Dispose();
        _facilityViewHud = null;
        if (hadHudHandle) Il2Cpp.HideHUDController.ToggleHUD?.Invoke(Hud.IsVisible);
    }
}
