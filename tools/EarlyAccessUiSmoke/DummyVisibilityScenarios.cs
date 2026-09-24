using System.Text.Json;
using Anomaly.Client.Api.Input;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMirror;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.FirstPersonControl;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private const string DummyNamePrefix = "AnomalySmokeDummy";
    private int _dummyRepliesBefore;
    private int _dummyReplyCountSaved;
    private string _dummyCommand;
    private uint _dummyNetId;
    private Vector3 _dummyHostMoveStart;
    private Vector3 _dummyFollowStart;
    private IDisposable _dummyMoveInput;
    private IDisposable _dummyMoveButton;
    private float _dummyMoveAxis;
    private ActionName[] _dummyFrameDirections;
    private int _dummyFrameDirectionIndex;
    private double _dummyMoveDirectionStarted;
    private KeyCode _dummyActiveMoveKey;

    private static int DummyScreenWidth => int.TryParse(Environment.GetEnvironmentVariable("ANOMALY_DUMMY_SCREEN_WIDTH"), out int value)
        ? value : Screen.currentResolution.width;

    private static int DummyScreenHeight => int.TryParse(Environment.GetEnvironmentVariable("ANOMALY_DUMMY_SCREEN_HEIGHT"), out int value)
        ? value : Screen.currentResolution.height;

    private static void ApplyDummyResolution() => Screen.SetResolution(DummyScreenWidth, DummyScreenHeight, false);

    private static bool IsDummyResolutionReady()
    {
        if (Screen.width == DummyScreenWidth && Screen.height == DummyScreenHeight) return true;
        ApplyDummyResolution();
        return false;
    }

    private void MuteDummyMenuMusic(string stage)
    {
        MainMenuSoundtrackController.Singleton?.MuteTheme();
        MuteDummyMenuMusicContinuous();
        var soundtrack = MainMenuSoundtrackController.Singleton;
        var sources = new List<AudioSource>();
        if (soundtrack != null)
            sources.AddRange(new[] { soundtrack.ToposThemeSource, soundtrack.IntenseThemeSource,
                soundtrack.RetroThemeSource, soundtrack.LobbyThemeSource }.Where(source => source != null));
        foreach (var manager in Resources.FindObjectsOfTypeAll<MenuMusicManager>())
        {
            if (manager == null) continue;
            if (manager.mainSource != null) sources.Add(manager.mainSource);
            if (manager.creditsSource != null) sources.Add(manager.creditsSource);
        }
        File.WriteAllText(Path.Combine(_output, "dummy-menu-audio-" + stage + ".json"),
            JsonSerializer.Serialize(new { stage, soundtrackState = soundtrack?.SoundtrackState.ToString(),
                sources = sources.Select(source => new { source.name, source.mute, source.isPlaying, source.volume }).ToArray() },
                new JsonSerializerOptions { WriteIndented = true }));
        Require(sources.All(source => source.mute), "Test-process menu music sources are muted: " + stage);
    }

    private static void MuteDummyMenuMusicContinuous()
    {
        var soundtrack = MainMenuSoundtrackController.Singleton;
        if (soundtrack != null)
            foreach (var source in new[] { soundtrack.ToposThemeSource, soundtrack.IntenseThemeSource,
                         soundtrack.RetroThemeSource, soundtrack.LobbyThemeSource })
                if (source != null) source.mute = true;
        foreach (var manager in Resources.FindObjectsOfTypeAll<MenuMusicManager>())
        {
            if (manager == null) continue;
            if (manager.mainSource != null) manager.mainSource.mute = true;
            if (manager.creditsSource != null) manager.creditsSource.mute = true;
        }
    }

    private void EnqueueLocalHostDummyProbe(string entry)
    {
        string name = DummyNamePrefix + entry;
        Add("dummy-before-round-" + entry, () => SaveDummyStage(entry, "before-round"));
        AddDummyCommand(entry, "start-round", () => "forcestart", "Done! Forced round start.");
        AddWait("dummy-round-started-" + entry, () =>
        {
            SaveDummyStage(entry, "round-starting");
            return Il2CppGameCore.RoundStart.RoundStarted;
        }, 90);
        AddDummyCommand(entry, "lock-round", () => "roundlock enable", "Round lock enabled!");
        AddWait("dummy-round-locked-" + entry, () =>
        {
            SaveDummyStage(entry, "round-locking");
            return RoundSummary.RoundLock;
        }, 15);
        Add("dummy-host-before-role-" + entry, () => SaveDummyStage(entry, "host-before-role"));
        AddDummyCommand(entry, "role-host", () => $"forcerole {LocalHostPlayerId()} FacilityGuard", "Changed role of 1 player to FacilityGuard");
        AddWait("dummy-host-living-" + entry, () =>
        {
            SaveDummyStage(entry, "host-role-changing");
            return ReferenceHub.TryGetLocalHub(out var host) && host.roleManager?.CurrentRole?.RoleTypeId == RoleTypeId.FacilityGuard &&
                Anomaly.Client.Api.Features.LocalPlayer.Instance?.FirstPersonMovementModule?.ModuleReady == true;
        }, 30);
        Add("dummy-host-role-" + entry, () => SaveDummyStage(entry, "host-role"));

        AddDummyCommand(entry, "spawn", () => "dummies spawn " + name, "A dummy has been spawned.");
        AddWait("dummy-created-" + entry, () =>
        {
            SaveDummyStage(entry, "spawning");
            return FindDummy(name) != null;
        }, 20);
        Add("dummy-spawned-" + entry, () =>
        {
            var dummy = FindDummy(name);
            _dummyNetId = dummy.netId;
            SaveDummyStage(entry, "spawned");
            Require(dummy.IsDummy && _dummyNetId != 0, "Stock RA created a networked dummy: " + entry);
        });
        AddDummyCommand(entry, "role-dummy", () => $"forcerole {DummyPlayerId(name)} FacilityGuard", "Changed role of 1 player to FacilityGuard");
        AddWait("dummy-role-ready-" + entry, () =>
        {
            SaveDummyStage(entry, "role-changing");
            return FindDummy(name)?.roleManager?.CurrentRole?.RoleTypeId == RoleTypeId.FacilityGuard;
        }, 30);
        Add("dummy-role-" + entry, () =>
        {
            SaveDummyStage(entry, "role");
            Require(FindDummy(name).roleManager.CurrentRole.RoleTypeId == RoleTypeId.FacilityGuard,
                "Dummy role changed to FacilityGuard: " + entry);
        });
        AddDummyCommand(entry, "bring", () => $"bring {DummyPlayerId(name)}", "request affected 1 player");
        AddWait("dummy-brought-" + entry, () =>
        {
            SaveDummyStage(entry, "bringing");
            return ReferenceHub.TryGetLocalHub(out var host) && FindDummy(name) is { } dummy &&
                Vector3.Distance(host.transform.position, dummy.transform.position) < 2.5f;
        }, 20);
        Add("dummy-brought-state-" + entry, () =>
        {
            SaveDummyStage(entry, "brought");
            Require(Vector3.Distance(FindDummy(name).transform.position, LocalHost().transform.position) < 2.5f,
                "Bring placed the dummy at the host: " + entry);
        });
        AddDummyCommand(entry, "follow", () => $"dummies follow {DummyPlayerId(name)}", "request affected 1 dummy");
        Add("dummy-follow-state-" + entry, () =>
        {
            SaveDummyStage(entry, "following");
            Require(FindDummy(name).gameObject.GetComponent("PlayerFollower") != null,
                "Stock follow component was attached: " + entry);
            RemoteAdminUIController.Singleton.IsEnabled = false;
            _dummyHostMoveStart = LocalHost().transform.position;
            _dummyFollowStart = FindDummy(name).transform.position;
            BeginDummyMoveBackward();
        });
        AddWait("dummy-host-moved-by-input-" + entry, () =>
        {
            SaveDummyStage(entry, "moving");
            if (Vector3.Distance(_dummyHostMoveStart, LocalHost().transform.position) < 2f) return false;
            StopDummyMove();
            return true;
        }, 20);
        AddWait("dummy-follow-moved-" + entry, () =>
        {
            SaveDummyStage(entry, "follow-motion");
            return Vector3.Distance(_dummyFollowStart, FindDummy(name).transform.position) > 0.5f;
        }, 20);
        Add("dummy-follow-ra-reopen-" + entry, () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            ui.IsEnabled = true;
            ui.ChangeTextMode(true);
        });
        AddDummyCommand(entry, "stop-follow", () => $"dummies follow {DummyPlayerId(name)}", "request affected 1 dummy");
        AddWait("dummy-follow-stopped-" + entry, () =>
        {
            SaveDummyStage(entry, "follow-stopping");
            return FindDummy(name)?.gameObject.GetComponent("PlayerFollower") == null;
        }, 10);
        Add("dummy-frame-move-start-" + entry, () =>
        {
            SaveDummyStage(entry, "follow-stopped");
            RemoteAdminUIController.Singleton.IsEnabled = false;
            _dummyHostMoveStart = LocalHost().transform.position;
            BeginDummyFrameMove(name);
        });
        AddWait("dummy-frame-gap-" + entry, () =>
        {
            SaveDummyStage(entry, "framing-move");
            var view = ProbeDummyView(FindDummy(name));
            if (view == null || view.CameraDistance < 2.5f)
            {
                AdvanceDummyFrameMove();
                return false;
            }
            StopDummyMove();
            return true;
        }, 30);
        Add("dummy-face-start-" + entry, () =>
        {
            File.AppendAllText(Path.Combine(_output, "dummy-input.log"),
                "Facing uses stock FpcMouseLook angle setters; synthetic mouse axes are ignored while the test window cursor is visible.\n");
        });
        AddWait("dummy-face-model-" + entry, () =>
        {
            FaceDummyThroughMouseLook(name);
            SaveDummyStage(entry, "facing");
            var view = ProbeDummyView(FindDummy(name));
            return view?.OnScreen == true && Math.Abs(DummyViewAngle(name)) < 15f;
        }, 15);
        AddWait("dummy-gameplay-resolution-" + entry, IsDummyResolutionReady, 20);
        Add("dummy-gameplay-view-" + entry, () =>
        {
            SaveDummyStage(entry, "gameplay-view");
            Capture("dummy-gameplay-" + entry);
        });
        Add("dummy-gameplay-validate-" + entry, () =>
        {
            var dummy = FindDummy(name);
            var view = ProbeDummyView(dummy);
            Require(dummy != null && dummy.netId == _dummyNetId, "Same dummy survived movement: " + entry);
            Require(NetworkClient.spawned.ContainsKey(_dummyNetId), "Host client has the dummy network identity: " + entry);
            Require(dummy.roleManager.CurrentRole.TryCast<IFpcRole>()?.FpcModule?.CharacterModelInstance != null,
                "Host client has the dummy character model: " + entry);
            Require(!RemoteAdminUIController.Singleton.IsEnabled, "Remote Admin is closed for gameplay view: " + entry);
            Require(view != null && view.CameraDistance >= 2.5f, "Dummy model is at least 2.5 m from the gameplay camera: " + entry);
            Require(view.OnScreen, "Dummy torso is within the gameplay camera viewport: " + entry);
            Require(view.LineOfSight, "Gameplay camera has a clear line of sight to the dummy: " + entry);
        });
        Add("dummy-ra-reopen-" + entry, () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            ui.IsEnabled = true;
            ui.ChangeTextMode(true);
        });
        AddDummyCommand(entry, "destroy", () => $"dummies destroy {DummyPlayerId(name)}", "request affected 1 dummy");
        AddWait("dummy-removed-" + entry, () =>
        {
            SaveDummyStage(entry, "removing");
            return FindDummy(name) == null;
        }, 20);
        Add("dummy-removed-state-" + entry, () =>
        {
            SaveDummyStage(entry, "removed");
            Require(!NetworkClient.spawned.ContainsKey(_dummyNetId), "Dummy client identity was removed: " + entry);
            ReleaseDummyInputs();
        });
        AddDummyCommand(entry, "unlock-round", () => "roundlock disable", "Round lock disabled!");
        Add("dummy-round-unlocked-" + entry, () =>
        {
            SaveDummyStage(entry, "round-unlocked");
            Require(!RoundSummary.RoundLock, "Round lock cleared before disconnect: " + entry);
        });
    }

    private void AddDummyCommand(string entry, string stage, Func<string> command, string successText)
    {
        Add("dummy-command-" + stage + "-" + entry, () =>
        {
            var ra = TextBasedRemoteAdmin.singleton;
            Require(ra != null && ra.gameObject.activeInHierarchy, "Stock text Remote Admin is active for " + stage + ": " + entry);
            _dummyCommand = command();
            _dummyRepliesBefore = ra._logs.Count;
            _dummyReplyCountSaved = _dummyRepliesBefore;
            ra.commandField.SetTextWithoutNotify(_dummyCommand);
            ra.SendCommand();
        });
        AddWait("dummy-reply-" + stage + "-" + entry, () =>
        {
            var logs = TextBasedRemoteAdmin.singleton._logs.ToArray();
            if (logs.Length > _dummyReplyCountSaved)
            {
                foreach (string line in logs.Skip(_dummyReplyCountSaved))
                    File.AppendAllText(Path.Combine(_output, "dummy-commands-" + entry + ".jsonl"),
                        JsonSerializer.Serialize(new { stage, command = _dummyCommand, reply = line }) + "\n");
                _dummyReplyCountSaved = logs.Length;
            }
            return logs.Skip(_dummyRepliesBefore).Any(line =>
                line != null && line.Contains("TBRA_CommandSuccess", StringComparison.Ordinal) &&
                line.Contains(successText, StringComparison.OrdinalIgnoreCase));
        }, 20);
    }

    private static ReferenceHub LocalHost()
    {
        if (ReferenceHub.TryGetLocalHub(out var host) && host != null) return host;
        throw new InvalidOperationException("Local host hub is unavailable.");
    }

    private static int LocalHostPlayerId() => LocalHost().PlayerId;

    private static ReferenceHub FindDummy(string name)
    {
        foreach (var hub in ReferenceHub.AllHubs)
            if (hub != null && hub.IsDummy && hub.nicknameSync?.MyNick == name) return hub;
        return null;
    }

    private static int DummyPlayerId(string name) => FindDummy(name)?.PlayerId ??
        throw new InvalidOperationException("Named dummy is unavailable: " + name);

    private static object Point(Vector3 point) => new { x = point.x, y = point.y, z = point.z };

    private float DummyViewAngle(string name)
    {
        var camera = Anomaly.Client.Api.Features.LocalPlayer.Instance?.Camera;
        var dummy = FindDummy(name);
        if (camera == null || dummy == null) throw new InvalidOperationException("Camera or dummy is unavailable.");
        var toward = DummyAimPoint(dummy) - camera.transform.position;
        toward.y = 0;
        var forward = camera.transform.forward;
        forward.y = 0;
        return Vector3.SignedAngle(forward, toward, Vector3.up);
    }

    private void FaceDummyThroughMouseLook(string name)
    {
        var dummy = FindDummy(name) ?? throw new InvalidOperationException("Dummy is unavailable while facing it.");
        var player = Anomaly.Client.Api.Features.LocalPlayer.Instance;
        var camera = player?.Camera;
        var mouseLook = player?.FirstPersonMovementModule?.MouseLook;
        if (camera == null || mouseLook == null) throw new InvalidOperationException("Local first-person camera or mouse look is unavailable.");
        var direction = DummyAimPoint(dummy) - camera.transform.position;
        if (direction.sqrMagnitude < 0.01f) throw new InvalidOperationException("Dummy is too close to set camera direction.");
        var angles = Quaternion.LookRotation(direction, Vector3.up).eulerAngles;
        mouseLook.CurrentHorizontal = angles.y;
        mouseLook.CurrentVertical = Mathf.Clamp(-Mathf.DeltaAngle(0, angles.x), -88f, 88f);
    }

    private static Vector3 DummyAimPoint(ReferenceHub dummy)
    {
        var model = dummy.roleManager?.CurrentRole?.TryCast<IFpcRole>()?.FpcModule?.CharacterModelInstance;
        if (model != null)
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.name == "torso" && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    return renderer.bounds.center;
        return dummy.transform.position + Vector3.up * 1.25f;
    }

    private sealed class DummyViewProbe
    {
        public Vector3 Target;
        public Vector3 Viewport;
        public float Separation;
        public float CameraDistance;
        public bool OnScreen;
        public bool LineOfSight;
        public string RaycastHitName;
        public float? RaycastHitDistance;
    }

    private static DummyViewProbe ProbeDummyView(ReferenceHub dummy)
    {
        var camera = Anomaly.Client.Api.Features.LocalPlayer.Instance?.Camera;
        if (camera == null || dummy == null || !ReferenceHub.TryGetLocalHub(out var host) || host == null) return null;
        Vector3 target = DummyAimPoint(dummy);
        Vector3 start = camera.transform.position;
        Vector3 delta = target - start;
        float distance = delta.magnitude;
        var viewport = camera.WorldToViewportPoint(target);
        RaycastHit hit = default;
        bool hitSomething = distance > 0 && Physics.Raycast(start, delta.normalized, out hit, distance);
        bool targetHit = hitSomething && hit.collider != null &&
            (hit.transform.IsChildOf(dummy.transform) ||
             (dummy.roleManager?.CurrentRole?.TryCast<IFpcRole>()?.FpcModule?.CharacterModelInstance is { } model &&
              hit.transform.IsChildOf(model.transform)));
        return new DummyViewProbe
        {
            Target = target,
            Viewport = viewport,
            Separation = Vector3.Distance(host.transform.position, dummy.transform.position),
            CameraDistance = distance,
            OnScreen = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1,
            LineOfSight = !hitSomething || targetHit,
            RaycastHitName = hitSomething ? hit.collider?.name : null,
            RaycastHitDistance = hitSomething ? hit.distance : null
        };
    }

    private void SaveDummyStage(string entry, string stage)
    {
        var host = ReferenceHub.TryGetLocalHub(out var local) ? local : null;
        var dummy = FindDummy(DummyNamePrefix + entry);
        var identity = dummy?.netIdentity;
        var role = dummy?.roleManager?.CurrentRole;
        var module = role?.TryCast<IFpcRole>()?.FpcModule;
        var model = module?.CharacterModelInstance;
        var camera = Anomaly.Client.Api.Features.LocalPlayer.Instance?.Camera;
        var hostModule = Anomaly.Client.Api.Features.LocalPlayer.Instance?.FirstPersonMovementModule;
        var moveKey = _dummyMoveAxis < 0 ? _dummyActiveMoveKey : KeyCode.None;
        NetworkIdentity clientIdentity = null;
        bool clientSpawned = identity != null && NetworkClient.spawned.TryGetValue(identity.netId, out clientIdentity);
        var renderers = new List<object>();
        if (model != null)
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer != null)
                    renderers.Add(new { renderer.name, renderer.enabled, renderer.gameObject.activeSelf,
                        renderer.gameObject.activeInHierarchy, boundsCenter = Point(renderer.bounds.center) });
        var culling = model?.GetComponentInChildren<CullingSubcontroller>(true);
        bool? cullingShouldBeVisible = null;
        string cullingProbeError = null;
        try { if (culling != null) cullingShouldBeVisible = culling.ShouldBeVisible; }
        catch (Exception ex) { cullingProbeError = ex.ToString(); }
        var view = ProbeDummyView(dummy);
        File.WriteAllText(Path.Combine(_output, "dummy-" + entry + "-" + stage + ".json"),
            JsonSerializer.Serialize(new
            {
                entry, stage, time = Time.realtimeSinceStartupAsDouble,
                serverActive = NetworkServer.active, clientActiveHost = NetworkClient.activeHost,
                roundStarted = Il2CppGameCore.RoundStart.RoundStarted,
                roundLocked = RoundSummary.RoundLock,
                cursorMovementLocked = Il2CppCursorManagement.CursorManager.MovementLocked,
                cursorVisible = Cursor.visible,
                hostMovementModuleReady = hostModule?.ModuleReady,
                hostMotorInputLocked = hostModule?.Motor?.InputLocked,
                mouseLookYaw = hostModule?.MouseLook?.CurrentHorizontal,
                mouseLookPitch = hostModule?.MouseLook?.CurrentVertical,
                dummyHorizontalAngle = dummy != null && camera != null ? DummyViewAngle(DummyNamePrefix + entry) : (float?)null,
                moveKey = moveKey.ToString(), virtualMoveHeld = _dummyMoveAxis < 0,
                frameMoveDirection = _dummyFrameDirections == null ? null : _dummyFrameDirections[_dummyFrameDirectionIndex].ToString(),
                observedMoveKeyHeld = _dummyMoveAxis < 0 ? UnityEngine.Input.GetKey(moveKey) : (bool?)null,
                screen = new { Screen.width, Screen.height, expectedWidth = DummyScreenWidth, expectedHeight = DummyScreenHeight },
                remoteAdminOpen = RemoteAdminUIController.Singleton?.IsEnabled,
                host = host == null ? null : new { host.PlayerId, host.netId,
                    identityLocalPlayer = host.netIdentity?.isLocalPlayer,
                    position = Point(host.transform.position),
                    role = host.roleManager?.CurrentRole?.RoleTypeId.ToString(), camera = camera == null ? null : Point(camera.transform.position) },
                dummy = dummy == null ? null : new { dummy.PlayerId, dummy.netId, dummy.IsDummy,
                    nickname = dummy.nicknameSync?.MyNick, position = Point(dummy.transform.position),
                    role = role?.RoleTypeId.ToString(), serverIdentity = identity != null,
                    followerAttached = dummy.gameObject.GetComponent("PlayerFollower") != null,
                    identityLocalPlayer = identity?.isLocalPlayer,
                    hostObserver = identity?.observers != null && NetworkServer.localConnection != null &&
                        identity.observers.ContainsKey(NetworkServer.localConnection.connectionId),
                    clientSpawned, clientIdentityMatchesServer = SameNative(identity, clientIdentity),
                    model = model == null ? null : new { model.IsVisible, ownerMatchesDummy = SameNative(model.OwnerHub, dummy),
                        ownerLocalPlayer = model.OwnerHub?.isLocalPlayer,
                        model.gameObject.activeSelf, model.gameObject.activeInHierarchy,
                        position = Point(model.transform.position),
                        view = view == null ? null : new { aimPoint = Point(view.Target), viewport = Point(view.Viewport),
                            view.Separation, view.CameraDistance, view.OnScreen, view.LineOfSight, view.RaycastHitName, view.RaycastHitDistance },
                        cullingShouldBeVisible, cullingProbeError, renderers }
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ReleaseDummyInputs()
    {
        StopDummyMove();
    }

    private void BeginDummyMoveBackward() => BeginDummyMove(ActionName.MoveBackward);

    private void BeginDummyFrameMove(string name)
    {
        var host = LocalHost();
        var dummy = FindDummy(name) ?? throw new InvalidOperationException("Dummy is unavailable for framing movement.");
        var camera = Anomaly.Client.Api.Features.LocalPlayer.Instance?.Camera;
        bool dummyBehind = camera != null && Vector3.Dot(camera.transform.forward,
            dummy.transform.position - host.transform.position) < 0;
        _dummyFrameDirections = dummyBehind
            ? new[] { ActionName.MoveForward, ActionName.MoveBackward, ActionName.MoveLeft, ActionName.MoveRight }
            : new[] { ActionName.MoveBackward, ActionName.MoveForward, ActionName.MoveLeft, ActionName.MoveRight };
        _dummyFrameDirectionIndex = 0;
        BeginDummyMove(_dummyFrameDirections[0]);
    }

    private void AdvanceDummyFrameMove()
    {
        if (_dummyFrameDirections == null || _dummyFrameDirectionIndex >= _dummyFrameDirections.Length - 1 ||
            Time.realtimeSinceStartupAsDouble - _dummyMoveDirectionStarted < 5) return;
        _dummyFrameDirectionIndex++;
        BeginDummyMove(_dummyFrameDirections[_dummyFrameDirectionIndex]);
    }

    private void BeginDummyMove(ActionName action)
    {
        StopDummyMove();
        _dummyMoveAxis = -1f;
        var fallback = action switch
        {
            ActionName.MoveForward => KeyCode.W,
            ActionName.MoveLeft => KeyCode.A,
            ActionName.MoveRight => KeyCode.D,
            _ => KeyCode.S
        };
        var key = NewInput.GetKey(action, fallback);
        _dummyActiveMoveKey = key;
        _dummyMoveButton = VirtualInputs.RegisterButton("earlyaccessuismoke.dummy.move", key, () => _dummyMoveAxis < 0);
        _dummyMoveDirectionStarted = Time.realtimeSinceStartupAsDouble;
        File.AppendAllText(Path.Combine(_output, "dummy-input.log"),
            $"{_dummyMoveDirectionStarted:F3} {action} key={key} host={LocalHost().transform.position}\n");
    }

    private void StopDummyMove()
    {
        _dummyMoveAxis = 0;
        _dummyActiveMoveKey = KeyCode.None;
        _dummyMoveButton?.Dispose();
        _dummyMoveInput?.Dispose();
        _dummyMoveButton = _dummyMoveInput = null;
    }
}
