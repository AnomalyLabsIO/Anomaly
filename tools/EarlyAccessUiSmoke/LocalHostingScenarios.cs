using System.Text.Json;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMirror;
using Il2CppRemoteAdmin;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ApiClient = Anomaly.Client.Api.Features.Client;
using GameConsole = Il2CppGameCore.Console;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private bool _localHostingSmoke;
    private bool _dummyVisibilitySmoke;
    private bool _watchHostingWarning;
    private int _remoteAdminRepliesBefore;

    private void EnqueueLocalHostingSmoke()
    {
        Add("local-host-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Local Host Smoke");
                Submit();
                Require(!IsVisible(), "Fresh isolated nickname was saved");
            }
            if (_dummyVisibilitySmoke)
            {
                MuteDummyMenuMusic("initial");
                ApplyDummyResolution();
            }
            Capture("local-host-main-menu");
        });

        if (_dummyVisibilitySmoke) AddWait("dummy-primary-resolution", IsDummyResolutionReady, 20);

        foreach (string entry in new[] { "console", "menu" })
        {
            Add("local-host-start-" + entry, () =>
            {
                Require(!NetworkServer.active && !NetworkClient.active, "Hosting starts from offline state: " + entry);
                Require(NewMainMenu.Singleton != null, "Stock main menu is available: " + entry);
                _watchHostingWarning = true;
                CheckHostingWarning();
                if (entry == "console")
                    GameConsole.Singleton.TypeCommand("host");
                else
                    NewMainMenu.Singleton.StartServer();
                CheckHostingWarning();
            });
            AddWait("local-host-connected-" + entry, HasLocalHostAdmin, 150);
            if (Environment.GetEnvironmentVariable("ANOMALY_CLIENT_SCRIPTING_SMOKE") == "1")
                EnqueueClientScriptingSmoke(entry);
            if (_dummyVisibilitySmoke) AddWait("dummy-host-resolution-" + entry, IsDummyResolutionReady, 20);
            Add("local-host-admin-" + entry, () =>
            {
                AssertLocalHost(entry);
                Capture("local-host-connected-" + entry);
                var ui = RemoteAdminUIController.Singleton;
                Require(ui != null && ui.LoggedIn && ui.CanToggle, "Remote Admin is logged in and available: " + entry);
                ui.IsEnabled = true;
            });
            Add("local-host-ra-open-" + entry, () =>
            {
                var ui = RemoteAdminUIController.Singleton;
                Require(ui.IsEnabled && ui.RootPanel.activeInHierarchy && !ui.RootLogin.activeInHierarchy,
                    "Remote Admin opens without a login prompt: " + entry);
                Capture("local-host-remote-admin-" + entry);
                Require(TextBasedRemoteAdmin.singleton != null, "Stock text Remote Admin is available: " + entry);
                ui.ChangeTextMode(true);
            });
            Add("local-host-ra-query-" + entry, () =>
            {
                var console = TextBasedRemoteAdmin.singleton;
                Require(console.gameObject.activeInHierarchy, "Text Remote Admin is active: " + entry);
                _remoteAdminRepliesBefore = CountVersionReplies();
                console.commandField.SetTextWithoutNotify("version");
                console.SendCommand();
            });
            AddWait("local-host-ra-reply-" + entry, () =>
            {
                SaveLocalHostingEvidence(entry);
                return CountVersionReplies() > _remoteAdminRepliesBefore;
            }, 15);
            Add("local-host-evidence-" + entry, () =>
            {
                SaveLocalHostingEvidence(entry);
                Capture("local-host-remote-admin-reply-" + entry);
                Require(!GameConsole.Singleton._clientCommandLogs.ToArray().Any(command =>
                    command.Trim().StartsWith("adminme", StringComparison.OrdinalIgnoreCase)),
                    "No admin grant command was entered: " + entry);
                Require(true, "Stock Remote Admin version request succeeded: " + entry);
            });
            if (_dummyVisibilitySmoke) EnqueueLocalHostDummyProbe(entry);
            Add("local-host-ra-close-" + entry, () => RemoteAdminUIController.Singleton.IsEnabled = false);
            Add("local-host-disconnect-" + entry, ApiClient.Disconnect);
            AddWait("local-host-returned-menu-" + entry,
                () => !NetworkServer.active && !NetworkClient.active && NewMainMenu.Singleton != null, 40);
            Add("local-host-disconnected-" + entry, () =>
            {
                _watchHostingWarning = false;
                Require(NetworkServer.localConnection == null, "Disconnect clears the in-process server connection: " + entry);
                if (Environment.GetEnvironmentVariable("ANOMALY_CLIENT_SCRIPTING_SMOKE") == "1")
                    Require(ScriptOutputLabels().Length == 0, "Disconnect destroys every script-owned text label: " + entry);
                AssertCanvasLifetime();
                if (_dummyVisibilitySmoke) MuteDummyMenuMusic("returned-" + entry);
                Capture("local-host-returned-menu-" + entry);
            });
        }

        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!string.IsNullOrEmpty(server)) EnqueueLocalHostingRemoteCheck(server);
        Add("local-host-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"),
                $"Local hosting smoke passed; {_captures} screenshots. Both stock hosting entry points, no warning, automatic full admin, real Remote Admin request/reply, and disconnect/rehost verified. " +
                (_dummyVisibilitySmoke ? "Dummy role, bring, follow, host observation, client model state, and gameplay views were recorded for both entries. Inspect screenshots to certify visual rendering. " : "") +
                (string.IsNullOrEmpty(server) ? "Remote-client exclusion was not exercised in this run.\n" : "Separate loopback server connection received no automatic admin.\n"));
            Application.Quit();
        });
    }

    private static bool SameNative(Il2CppObjectBase left, Il2CppObjectBase right) =>
        left != null && right != null && left.Pointer != IntPtr.Zero && left.Pointer == right.Pointer;

    private static bool HasLocalHostAdmin()
    {
        if (!NetworkServer.active || !NetworkClient.activeHost || !NetworkClient.isConnected || ServerStatic.IsDedicated ||
            !ReferenceHub.TryGetLocalHub(out var hub) || !ReferenceHub.TryGetHostHub(out var host) || !SameNative(hub, host) ||
            !hub.isLocalPlayer || !SameNative(hub.netIdentity, NetworkClient.localPlayer) ||
            !SameNative(hub.connectionToClient, NetworkServer.localConnection)) return false;
        var roles = hub.serverRoles;
        var query = hub.queryProcessor;
        return roles != null && roles.RemoteAdmin && roles.Permissions == ServerStatic.PermissionsHandler.FullPerm &&
            roles.AdminChatPerms && query != null && query.GameplayData && query._commandsSynced &&
            TextBasedRemoteAdmin.Commands != null && TextBasedRemoteAdmin.Commands.ToArray().Any(command => command.Command == "version");
    }

    private void AssertLocalHost(string entry)
    {
        Require(HasLocalHostAdmin(), "In-process host has full stock admin permissions and command synchronization: " + entry);
        ReferenceHub.TryGetLocalHub(out var hub);
        var local = NetworkServer.localConnection;
        Require(local != null && SameNative(local.identity, hub.netIdentity) &&
            SameNative(local.connectionToServer, NetworkClient.connection) &&
            SameNative(local.connectionToServer.connectionToClient, local),
            "Host connection has the reciprocal in-process identity: " + entry);
        CheckHostingWarning();
        Require(true, "Create Anyway warning was never visible: " + entry);
    }

    private static int CountVersionReplies() => TextBasedRemoteAdmin.singleton._logs.ToArray().Count(text =>
        text != null && text.Contains("[VERSION] ", StringComparison.Ordinal) &&
        text.Contains("TBRA_CommandSuccess", StringComparison.Ordinal) && !text.Contains("TBRA_CommandFail", StringComparison.Ordinal));

    private void CheckHostingWarning()
    {
        foreach (var manager in Resources.FindObjectsOfTypeAll<CustomNetworkManager>())
            if (manager != null && manager.createpop != null && manager.createpop.activeInHierarchy)
                throw new InvalidOperationException("The stock Create Anyway warning became visible.");
    }

    private void SaveLocalHostingEvidence(string entry)
    {
        ReferenceHub.TryGetLocalHub(out var hub);
        File.WriteAllText(Path.Combine(_output, "local-host-" + entry + ".json"), JsonSerializer.Serialize(new
        {
            entryPoint = entry == "console" ? "Console.TypeCommand(host)" : "NewMainMenu.StartServer()",
            activeServer = NetworkServer.active,
            activeHost = NetworkClient.activeHost,
            localPlayer = hub.isLocalPlayer,
            remoteAdmin = hub.serverRoles.RemoteAdmin,
            permissions = hub.serverRoles.Permissions,
            expectedPermissions = ServerStatic.PermissionsHandler.FullPerm,
            adminChat = hub.serverRoles.AdminChatPerms,
            gameplayData = hub.queryProcessor.GameplayData,
            commandsSynced = hub.queryProcessor._commandsSynced,
            commandCount = TextBasedRemoteAdmin.Commands.Count,
            replies = TextBasedRemoteAdmin.singleton._logs.ToArray().Where(text => text.Contains("[VERSION] ", StringComparison.Ordinal)).ToArray(),
            remoteAdminLog = TextBasedRemoteAdmin.singleton._logs.ToArray().ToArray(),
            remoteAdminConsole = TextBasedRemoteAdmin.singleton.consoleWindow.text,
            localHub = hub.Pointer.ToInt64(),
            serverConnection = NetworkServer.localConnection.Pointer.ToInt64(),
            clientConnection = NetworkClient.connection.Pointer.ToInt64()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void EnqueueLocalHostingRemoteCheck(string server)
    {
        if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var endpoint) ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var address) || !System.Net.IPAddress.IsLoopback(address))
            throw new InvalidOperationException("Remote exclusion smoke requires an explicit loopback server endpoint.");
        Add("local-host-remote-connect", () => ApiClient.Connect(server));
        AddWait("local-host-remote-ready", () =>
        {
            if (Instance() != null && IsVisible())
            {
                string accept = ((TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance())).text;
                if (accept == "Trust and connect") PromptButton("_accept").OnSubmit(new BaseEventData(EventSystem.current));
                else if (accept == "Allow content") PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
            }
            return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) &&
                ReferenceHub.TryGetLocalHub(out var hub) && hub.isLocalPlayer;
        }, 90);
        Add("local-host-remote-exclusion", () =>
        {
            ReferenceHub.TryGetLocalHub(out var hub);
            Require(!NetworkServer.active && !NetworkClient.activeHost && NetworkServer.localConnection == null,
                "Separate server uses an ordinary remote client connection");
            Require(!hub.serverRoles.RemoteAdmin && hub.serverRoles.Permissions == 0 && !hub.serverRoles.AdminChatPerms,
                "Ordinary remote connection receives no automatic admin");
            Capture("local-host-remote-no-admin");
        });
        Add("local-host-remote-disconnect", ApiClient.Disconnect);
        AddWait("local-host-remote-returned-menu", () => !NetworkClient.active && NewMainMenu.Singleton != null, 40);
    }
}
