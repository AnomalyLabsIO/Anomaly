using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppHints;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

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

    /// <summary>
    /// The local player's active camera. Delegates to <see cref="CameraManager.Main"/>,
    /// which provides cached resolution with multi-fallback chain
    /// (<c>MainCameraController.CurrentCamera</c> → user fallbacks →
    /// <c>Camera.main</c> → scene walk) and auto-invalidates on role / scene change.
    /// </summary>
    public Camera Camera => CameraManager.Main;

    /// <summary>
    /// Drops the cached camera reference. Mods rarely need this — the cache
    /// invalidates itself on role / scene change. Use when you've swapped
    /// the camera rig in a way the framework can't detect.
    /// </summary>
    public static void InvalidateCamera() => CameraManager.Invalidate();
}
