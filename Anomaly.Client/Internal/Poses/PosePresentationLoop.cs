using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Anomaly.Client.Internal.Poses;

internal sealed class PoseRestoreLoopMarker : Il2CppSystem.Object
{
    public PoseRestoreLoopMarker(IntPtr pointer) : base(pointer) { }
}
internal sealed class PosePresentLoopMarker : Il2CppSystem.Object
{
    public PosePresentLoopMarker(IntPtr pointer) : base(pointer) { }
}

/// <summary>Owns only two loop entries; never replaces another mod's player loop.</summary>
internal static class PosePresentationLoop
{
    private static PlayerLoopSystem.UpdateFunction _restore;
    private static PlayerLoopSystem.UpdateFunction _present;
    private static int _lastFrame;
    private static float _nextVerification;
    internal static bool Installed { get; private set; }

    internal static void Install()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PoseRestoreLoopMarker>()) ClassInjector.RegisterTypeInIl2Cpp<PoseRestoreLoopMarker>();
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PosePresentLoopMarker>()) ClassInjector.RegisterTypeInIl2Cpp<PosePresentLoopMarker>();
        _restore ??= DelegateSupport.ConvertDelegate<PlayerLoopSystem.UpdateFunction>((Action)PoseRuntime.Restore);
        _present ??= DelegateSupport.ConvertDelegate<PlayerLoopSystem.UpdateFunction>((Action)Present);
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Remove(loop);
        if (!Insert(loop, typeof(EarlyUpdate).FullName, new PlayerLoopSystem { type = Il2CppType.Of<PoseRestoreLoopMarker>(), updateDelegate = _restore }, true) ||
            !Insert(loop, typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate).FullName, new PlayerLoopSystem { type = Il2CppType.Of<PosePresentLoopMarker>(), updateDelegate = _present }, false))
            throw new InvalidOperationException("The pose renderer requires EarlyUpdate and script LateUpdate phases.");
        PlayerLoop.SetPlayerLoop(loop);
        Installed = true;
        _lastFrame = Time.frameCount;
        _nextVerification = Time.unscaledTime + 5;
    }

    internal static bool Healthy()
    {
        if (!Installed || Time.frameCount - _lastFrame > 2) return false;
        if (Time.unscaledTime < _nextVerification) return true;
        _nextVerification = Time.unscaledTime + 5;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        return Contains(loop, typeof(PoseRestoreLoopMarker).FullName) && Contains(loop, typeof(PosePresentLoopMarker).FullName);
    }

    private static void Present()
    {
        _lastFrame = Time.frameCount;
        if (Installed) PoseRuntime.Present();
    }

    internal static void Uninstall()
    {
        Installed = false;
        PoseRuntime.Restore();
        if (_restore == null) return;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Remove(loop);
        PlayerLoop.SetPlayerLoop(loop);
    }

    private static bool Contains(PlayerLoopSystem system, string name)
    {
        if (system.type?.FullName == name) return true;
        if (system.subSystemList != null)
            foreach (var child in system.subSystemList) if (Contains(child, name)) return true;
        return false;
    }

    private static bool Insert(PlayerLoopSystem system, string target, PlayerLoopSystem entry, bool atStart)
    {
        if (atStart && system.type?.FullName == target)
        {
            var prior = system.subSystemList;
            var next = new PlayerLoopSystem[(prior?.Length ?? 0) + 1];
            next[0] = entry;
            for (int i = 0; i < (prior?.Length ?? 0); i++) next[i + 1] = prior[i];
            system.subSystemList = next;
            return true;
        }
        var children = system.subSystemList;
        if (children == null) return false;
        for (int i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (!atStart && child.type?.FullName == target)
            {
                var next = new PlayerLoopSystem[children.Length + 1];
                for (int j = 0; j <= i; j++) next[j] = children[j];
                next[i + 1] = entry;
                for (int j = i + 1; j < children.Length; j++) next[j + 1] = children[j];
                system.subSystemList = next;
                return true;
            }
            if (Insert(child, target, entry, atStart))
            {
                children[i] = child; // IL2CPP value proxies need the changed child written back.
                return true;
            }
        }
        return false;
    }

    private static void Remove(PlayerLoopSystem system)
    {
        if (system.subSystemList == null) return;
        var keep = new List<PlayerLoopSystem>();
        foreach (var child in system.subSystemList)
        {
            string name = child.type?.FullName;
            if (name == typeof(PoseRestoreLoopMarker).FullName || name == typeof(PosePresentLoopMarker).FullName) continue;
            Remove(child);
            keep.Add(child);
        }
        system.subSystemList = keep.ToArray();
    }
}
