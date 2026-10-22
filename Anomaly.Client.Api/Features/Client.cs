using System.Collections.ObjectModel;
using Anomaly.Client.Api.Enum;
using Il2Cpp;
using Il2CppInteractables.Interobjects.DoorUtils;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using Il2CppMapGeneration;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Anomaly.Client.Api.Features;

public static class Client
{
    private static NewMainMenu _mainMenu;
    private static string _enrollmentId;

    public static NewMainMenu MainMenu
    {
        get
        {
            if (_mainMenu == null)
            {
                _mainMenu = UnityEngine.Object.FindObjectOfType<NewMainMenu>();
            }

            return _mainMenu;
        }
    }

    public static string CurrentSceneName => SceneManager.GetActiveScene().name;

    public static SceneType CurrentSceneType => GetSceneType(CurrentSceneName);

    public static ReadOnlyCollection<Player> Players => Player.PlayerList;

    public static ReadOnlyCollection<Room> Rooms => Room.List;

    public static ReadOnlyCollection<Door> Doors => Door.List;

    public static ReadOnlyCollection<Pickup> Pickups => Pickup.List;

    public static ReadOnlyCollection<Projectile> Projectiles => Projectile.List;

    public static ReadOnlyCollection<AdminToy> AdminToys => AdminToy.List;

    public static LocalPlayer LocalPlayer => Anomaly.Client.Api.Features.LocalPlayer.Instance;

    public static string ServerIp => LiteNetLib4MirrorTransport.Singleton.clientAddress;
    public static ushort ServerPort => LiteNetLib4MirrorTransport.Singleton.port;

    public static bool IsConnected => NetworkClient.isConnected;

    /// <summary>
    /// True after the client's scene-ready lifecycle callback, until a lifecycle
    /// reset. This is not a guarantee that the local hub, world objects, or enrollment
    /// are available; check the state required by each operation separately.
    /// </summary>
    /// <seealso cref="Anomaly.Client.Api.Events.Handlers.ClientEvents.Ready"/>
    public static bool IsClientReady { get; internal set; }

    /// <summary>
    /// Server-local enrollment id authenticated for the current connection, or
    /// <c>null</c> while disconnected/before the server publishes it.
    /// It may still be <c>null</c> when <see cref="IsClientReady"/> becomes true.
    /// </summary>
    public static string EnrollmentId
    {
        get => _enrollmentId;
        internal set
        {
            if (string.Equals(_enrollmentId, value, StringComparison.Ordinal))
                return;
            string previous = _enrollmentId;
            _enrollmentId = value;
            Events.Handlers.ClientEvents.OnEnrollmentChanged(new Events.Args.EnrollmentChangedEventArgs(previous, value));
        }
    }

    public static void QuitGame() => Application.Quit();

    public static void Connect(string address)
    {
        if (IsConnected) return;

        var menu = MainMenu;
        if (menu == null)
        {
            MelonLogger.Warning($"Client.Connect('{address}'): main menu not available yet — call after ClientEvents.CanvasReady.");
            return;
        }

        menu.Connect(address);
    }

    public static void Disconnect()
    {
        if (!IsConnected) return;

        var console = Il2CppGameCore.Console.Singleton;
        console.TypeCommand("disconnect");

        // TypeCommand appended our injected "disconnect" to the visible command log;
        // pop it so the user's history isn't polluted by a programmatic disconnect.
        var logs = console._clientCommandLogs;
        if (logs != null && logs.Count > 0)
            logs.RemoveAt(logs.Count - 1);
    }

    public static void Reconnect()
        => Reconnect(ServerIp + ":" + ServerPort);

    public static void Reconnect(string address)
    {
        Disconnect();
        Coroutines.CallDelayed(0.5f, () => Connect(address));
    }

    private static SceneType GetSceneType(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return SceneType.Loader;

        string normalized = sceneName.ToLowerInvariant();

        if (normalized.Contains("facility"))
            return SceneType.Facility;

        if (normalized.Contains("mainmenuremastered"))
            return SceneType.MainMenuRemastered;

        if (normalized.Contains("fastmenu"))
            return SceneType.FastMenu;

        if (normalized.Contains("new") && normalized.Contains("menu"))
            return SceneType.NewMainMenu;

        return SceneType.Loader;
    }
}
