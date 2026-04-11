using Anomaly.Client.Internal.Wrappers;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppHints;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class LocalPlayer : Player
{
    internal LocalPlayer(ReferenceHub hub) : base(hub) { }
    internal LocalPlayer(GameObject obj) : base(obj) { }

    public static LocalPlayer Instance => PlayerRegistry.GetLocal();

    public HintDisplay Hints => ReferenceHub.hints;
    public AspectRatioSync AspectRatioSync => ReferenceHub.aspectRatioSync;
    public GameConsoleTransmission GameConsoleTransmission => ReferenceHub.gameConsoleTransmission;
    public PlayerAuthenticationManager AuthManager => ReferenceHub.authManager;
    public EncryptedChannelManager EncryptedChannelManager => ReferenceHub.encryptedChannelManager;
    public QueryProcessor QueryProcessor => ReferenceHub.queryProcessor;

    public Camera Camera => GameObject.Find("Main Camera Controller/Default Camera/All Cameras/CharacterCamera")?.GetComponent<Camera>();
}
