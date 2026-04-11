using System.Collections.ObjectModel;
using Anomaly.Client.API.Enum;
using Anomaly.Client.Internal.Bootstrap;
using Il2Cpp;
using Il2CppInteractables.Interobjects.DoorUtils;
using Il2CppMirror;
using Il2CppMirror.LiteNetLib4Mirror;
using Il2CppMapGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Anomaly.Client.API.Features;

public static class Client
{
    private static NewMainMenu _mainMenu;

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

    public static LocalPlayer LocalPlayer => Anomaly.Client.API.Features.LocalPlayer.Instance;

    public static string ServerIp => LiteNetLib4MirrorTransport.Singleton.clientAddress;
    public static ushort ServerPort => LiteNetLib4MirrorTransport.Singleton.port;

    public static bool IsConnected => NetworkClient.isConnected;
    public static bool IsClientReady => ClientRuntime.IsClientReady;

    public static void QuitGame() => Application.Quit();

    public static void Connect(string address)
    {
        if (!IsConnected)
            MainMenu.Connect(address);
    }

    public static void Disconnect()
    {
        if (IsConnected)
        {
            Il2CppGameCore.Console.Singleton.TypeCommand("disconnect");
            Il2CppGameCore.Console.Singleton._clientCommandLogs.RemoveAt(Il2CppGameCore.Console.Singleton._clientCommandLogs.Count - 1);
        }
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
