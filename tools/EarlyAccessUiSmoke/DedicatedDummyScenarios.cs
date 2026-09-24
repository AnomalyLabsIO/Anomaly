using Il2Cpp;
using Il2CppMirror;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.FirstPersonControl;
using Il2CppRemoteAdmin;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    // This scenario is intentionally separate from local hosting. A dedicated client's
    // ReferenceHubs are replicated client objects; server observers, follower components,
    // and the static RoundLock flag cannot be checked from this process.
    private void EnqueueDedicatedDummyControlSmoke()
    {
        const string entry = "dedicated";
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var endpoint) ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var address) ||
            !System.Net.IPAddress.IsLoopback(address))
            throw new InvalidOperationException("Dedicated dummy control requires an explicit loopback server endpoint.");

        string name = DummyNamePrefix + entry;
        Add("dedicated-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Dedicated Dummy Smoke");
                Submit();
                Require(!IsVisible(), "Fresh isolated nickname was saved for dedicated control");
            }
            MuteDummyMenuMusic("dedicated-start");
            ApplyDummyResolution();
        });
        AddWait("dedicated-resolution", IsDummyResolutionReady, 20);
        Add("dedicated-connect", () =>
        {
            Require(!NetworkServer.active && !NetworkClient.active, "Dedicated control starts with no local server or client");
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("dedicated-ready", () =>
        {
            if (Instance() != null && IsVisible())
            {
                string accept = ((TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance())).text;
                if (accept == "Trust and connect") PromptButton("_accept").OnSubmit(new BaseEventData(EventSystem.current));
                else if (accept == "Allow content") PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
            }
            return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) &&
                NetworkClient.isConnected && !NetworkServer.active && !NetworkClient.activeHost &&
                ReferenceHub.TryGetLocalHub(out var hub) && hub != null && hub.isLocalPlayer;
        }, 90);
        AddWait("dedicated-owner-remote-admin", () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            return ReferenceHub.TryGetLocalHub(out var hub) && hub != null &&
                hub.serverRoles != null && hub.serverRoles.RemoteAdmin &&
                hub.queryProcessor != null && hub.queryProcessor.GameplayData &&
                ui != null && ui.LoggedIn && ui.CanToggle &&
                TextBasedRemoteAdmin.Commands != null &&
                TextBasedRemoteAdmin.Commands.ToArray().Any(command => command.Command == "version");
        }, 30);
        Add("dedicated-owner-state", () =>
        {
            var hub = DedicatedLocalPlayer();
            Require(!NetworkServer.active && !NetworkClient.activeHost && NetworkServer.localConnection == null,
                "Control client is connected to a separate dedicated server");
            Require(hub.serverRoles.RemoteAdmin && hub.serverRoles.Permissions != 0 && hub.queryProcessor.GameplayData,
                "Configured test owner received stock Remote Admin permissions");
            SaveDummyStage(entry, "connected");
            Capture("dedicated-connected");
            var ui = RemoteAdminUIController.Singleton;
            ui.IsEnabled = true;
        });
        AddWait("dedicated-ra-open", () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            if (ui == null || !ui.IsEnabled || !ui.RootPanel.activeInHierarchy || ui.RootLogin.activeInHierarchy)
                return false;
            ui.ChangeTextMode(true);
            return TextBasedRemoteAdmin.singleton != null && TextBasedRemoteAdmin.singleton.gameObject.activeInHierarchy;
        }, 15);
        AddDummyCommand(entry, "version", () => "version", "[VERSION]");
        Add("dedicated-before-round", () => SaveDummyStage(entry, "before-round"));
        AddDummyCommand(entry, "start-round", () => "forcestart", "Done! Forced round start.");
        AddWait("dedicated-round-started", () =>
        {
            SaveDummyStage(entry, "round-starting");
            return Il2CppGameCore.RoundStart.RoundStarted;
        }, 90);
        AddDummyCommand(entry, "lock-round", () => "roundlock enable", "Round lock enabled!");
        Add("dedicated-round-locked-reply", () => SaveDummyStage(entry, "round-lock-reply"));

        AddDummyCommand(entry, "role-player", () => $"forcerole {DedicatedLocalPlayer().PlayerId} FacilityGuard",
            "Changed role of 1 player to FacilityGuard");
        AddWait("dedicated-player-living", () =>
        {
            SaveDummyStage(entry, "player-role-changing");
            return DedicatedLocalPlayer().roleManager?.CurrentRole?.RoleTypeId == RoleTypeId.FacilityGuard &&
                Anomaly.Client.Api.Features.LocalPlayer.Instance?.FirstPersonMovementModule?.ModuleReady == true;
        }, 30);
        Add("dedicated-player-role", () => SaveDummyStage(entry, "player-role"));

        AddDummyCommand(entry, "spawn", () => "dummies spawn " + name, "A dummy has been spawned.");
        AddWait("dedicated-dummy-created", () =>
        {
            SaveDummyStage(entry, "spawning");
            return FindDummy(name) != null;
        }, 20);
        Add("dedicated-dummy-spawned", () =>
        {
            var dummy = FindDummy(name);
            _dummyNetId = dummy.netId;
            SaveDummyStage(entry, "spawned");
            Require(dummy.IsDummy && _dummyNetId != 0 && NetworkClient.spawned.ContainsKey(_dummyNetId),
                "Dedicated client received the named networked dummy");
        });
        AddDummyCommand(entry, "role-dummy", () => $"forcerole {DummyPlayerId(name)} FacilityGuard",
            "Changed role of 1 player to FacilityGuard");
        AddWait("dedicated-dummy-role-ready", () =>
        {
            SaveDummyStage(entry, "role-changing");
            return FindDummy(name)?.roleManager?.CurrentRole?.RoleTypeId == RoleTypeId.FacilityGuard;
        }, 30);
        Add("dedicated-dummy-role", () =>
        {
            SaveDummyStage(entry, "role");
            Require(FindDummy(name).roleManager.CurrentRole.RoleTypeId == RoleTypeId.FacilityGuard,
                "Dedicated client received the dummy FacilityGuard role");
        });
        AddDummyCommand(entry, "bring", () => $"bring {DummyPlayerId(name)}", "request affected 1 player");
        AddWait("dedicated-dummy-brought", () =>
        {
            SaveDummyStage(entry, "bringing");
            return FindDummy(name) is { } dummy &&
                Vector3.Distance(DedicatedLocalPlayer().transform.position, dummy.transform.position) < 2.5f;
        }, 20);
        Add("dedicated-dummy-brought-state", () => SaveDummyStage(entry, "brought"));
        AddDummyCommand(entry, "follow", () => $"dummies follow {DummyPlayerId(name)}", "request affected 1 dummy");
        Add("dedicated-follow-state", () =>
        {
            SaveDummyStage(entry, "following");
            RemoteAdminUIController.Singleton.IsEnabled = false;
            _dummyHostMoveStart = DedicatedLocalPlayer().transform.position;
            _dummyFollowStart = FindDummy(name).transform.position;
            BeginDummyMoveBackward();
        });
        AddWait("dedicated-player-moved-by-input", () =>
        {
            SaveDummyStage(entry, "moving");
            if (Vector3.Distance(_dummyHostMoveStart, DedicatedLocalPlayer().transform.position) < 2f) return false;
            StopDummyMove();
            return true;
        }, 20);
        AddWait("dedicated-dummy-follow-motion", () =>
        {
            SaveDummyStage(entry, "follow-motion");
            return FindDummy(name) is { } dummy && Vector3.Distance(_dummyFollowStart, dummy.transform.position) > 0.5f;
        }, 20);

        Add("dedicated-stop-follow-ra-open", () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            ui.IsEnabled = true;
            ui.ChangeTextMode(true);
        });
        AddDummyCommand(entry, "stop-follow", () => $"dummies follow {DummyPlayerId(name)}", "request affected 1 dummy");
        Add("dedicated-frame-move-start", () =>
        {
            SaveDummyStage(entry, "follow-stopped-reply");
            RemoteAdminUIController.Singleton.IsEnabled = false;
            _dummyHostMoveStart = DedicatedLocalPlayer().transform.position;
            BeginDummyFrameMove(name);
        });
        AddWait("dedicated-frame-gap", () =>
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
        Add("dedicated-face-start", () => FaceDummyThroughMouseLook(name));
        AddWait("dedicated-face-model", () =>
        {
            FaceDummyThroughMouseLook(name);
            SaveDummyStage(entry, "facing");
            return ProbeDummyView(FindDummy(name))?.OnScreen == true &&
                Math.Abs(DummyViewAngle(name)) < 15f;
        }, 20);
        AddWait("dedicated-gameplay-resolution", IsDummyResolutionReady, 20);
        Add("dedicated-gameplay-view", () =>
        {
            SaveDummyStage(entry, "gameplay-view");
            Capture("dummy-gameplay-dedicated");
        });
        Add("dedicated-gameplay-validate", () =>
        {
            var dummy = FindDummy(name);
            var view = ProbeDummyView(dummy);
            Require(dummy != null && dummy.netId == _dummyNetId, "Same dedicated dummy survived movement");
            Require(NetworkClient.spawned.ContainsKey(_dummyNetId), "Dedicated client still has the dummy network identity");
            Require(dummy.roleManager.CurrentRole.TryCast<IFpcRole>()?.FpcModule?.CharacterModelInstance != null,
                "Dedicated client has the dummy character model");
            Require(!RemoteAdminUIController.Singleton.IsEnabled, "Remote Admin is closed for dedicated gameplay view");
            Require(view != null && view.CameraDistance >= 2.5f, "Dedicated dummy is at least 2.5 m from the camera");
            Require(view.OnScreen, "Dedicated dummy torso is within the camera viewport");
            Require(view.LineOfSight, "Dedicated camera has a clear line of sight to the dummy");
        });
        Add("dedicated-destroy-ra-open", () =>
        {
            var ui = RemoteAdminUIController.Singleton;
            ui.IsEnabled = true;
            ui.ChangeTextMode(true);
        });
        AddDummyCommand(entry, "destroy", () => $"dummies destroy {DummyPlayerId(name)}", "request affected 1 dummy");
        AddWait("dedicated-dummy-removed", () =>
        {
            SaveDummyStage(entry, "removing");
            return FindDummy(name) == null;
        }, 20);
        Add("dedicated-dummy-removed-state", () =>
        {
            SaveDummyStage(entry, "removed");
            Require(!NetworkClient.spawned.ContainsKey(_dummyNetId), "Dedicated dummy client identity was removed");
            ReleaseDummyInputs();
        });
        AddDummyCommand(entry, "unlock-round", () => "roundlock disable", "Round lock disabled!");
        Add("dedicated-round-unlocked-reply", () => SaveDummyStage(entry, "round-unlocked-reply"));
        Add("dedicated-disconnect", () =>
        {
            RemoteAdminUIController.Singleton.IsEnabled = false;
            ApiClient.Disconnect();
        });
        AddWait("dedicated-returned-menu", () => !NetworkClient.active && !NetworkServer.active &&
            NewMainMenu.Singleton != null, 40);
        Add("dedicated-returned-menu-capture", () =>
        {
            ReleaseDummyInputs();
            MuteDummyMenuMusic("dedicated-returned");
            Capture("dedicated-returned-menu");
        });
        Add("dedicated-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"),
                $"Dedicated dummy control completed; {_captures} screenshots. Stock Remote Admin commands, client role, movement, model, and removal probes passed. Inspect dummy-gameplay-dedicated.png to certify visual rendering.\n");
            Application.Quit();
        });
    }

    private static ReferenceHub DedicatedLocalPlayer()
    {
        if (ReferenceHub.TryGetLocalHub(out var hub) && hub != null) return hub;
        throw new InvalidOperationException("Dedicated control has no local player hub.");
    }
}
