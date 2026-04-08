using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppHints;
using Il2CppInteractables;
using Il2CppPlayerRoles.FirstPersonControl;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class LocalPlayer : Player
{
    private static LocalPlayer _localPlayer;

    public LocalPlayer(ReferenceHub hub) : base(hub) { }
    public LocalPlayer(GameObject obj) : base(obj) { }

    public static LocalPlayer Instance
    {
        get
        {
            if(_localPlayer?.ReferenceHub == null)
            {
                _localPlayer = new LocalPlayer(ReferenceHub.LocalHub);
            }

            return _localPlayer;
        }
    }
    
    
    public HintDisplay Hints => ReferenceHub.hints;
    public AspectRatioSync AspectRatioSync => ReferenceHub.aspectRatioSync;
    public GameConsoleTransmission  GameConsoleTransmission => ReferenceHub.gameConsoleTransmission;
    public PlayerAuthenticationManager AuthManager => ReferenceHub.authManager;
    public EncryptedChannelManager EncryptedChannelManager => ReferenceHub.encryptedChannelManager;
    public QueryProcessor  QueryProcessor => ReferenceHub.queryProcessor;

    public InteractionCoordinator InteractionCoordinator => ReferenceHub.interCoordinator;
    

    public Camera Camera => GameObject.Find("Main Camera Controller/Default Camera/All Cameras/CharacterCamera").GetComponent<Camera>();
}