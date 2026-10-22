using System;
using System.Collections.Generic;
using Anomaly.Server.Api.Features;
using Anomaly.Shared.Poses;
using CommandSystem;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;

namespace HelloPlayerPoses;

/// <summary>An ordinary LabAPI plugin demonstrating independent owners and complete pose updates.</summary>
public sealed class Plugin : Plugin<Config>
{
    internal static Plugin Instance;
    private sealed class Demo
    {
        internal Player Player;
        internal PoseRigDescriptor Rig;
        internal ushort Head;
        internal Quaternion Neutral;
        internal PlayerPoseOverride Base;
        internal PlayerPoseOverride Overlap;
    }
    private readonly Dictionary<int, Demo> _demos = new Dictionary<int, Demo>();
    private PlayerPoseScope _baseScope;
    private PlayerPoseScope _overlapScope;
    private float _nextUpdate;
    public override string Name => "Hello Player Poses";
    public override string Description => "Server-console pose API and priority example.";
    public override string Author => "AnomalyLabs";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);

    public override void Enable() { Instance = this; StaticUnityMethods.OnLateUpdate += Tick; }
    public override void Disable()
    {
        StaticUnityMethods.OnLateUpdate -= Tick;
        _overlapScope?.Dispose(); _baseScope?.Dispose();
        _overlapScope = null; _baseScope = null; _demos.Clear(); Instance = null;
    }

    internal bool Execute(int id, string operation, out string response)
    {
        if (operation == "clear")
        {
            if (_demos.TryGetValue(id, out var old)) { old.Overlap?.Clear(); old.Base.Clear(); _demos.Remove(id); }
            response = "Pose contributions cleared."; return true;
        }
        if (operation == "overlap" && _demos.TryGetValue(id, out var current))
        {
            if (current.Overlap?.IsValid == true) { current.Overlap.Clear(); current.Overlap = null; response = "Higher priority released; base owner is visible again."; return true; }
            if (!_overlapScope.TryCreate(current.Player, Frame(current, 0), new PlayerPoseOptions { Priority = 10, Weight = .65f },
                out current.Overlap, out var failure)) { response = failure.ToString(); return false; }
            response = "Higher priority head contribution enabled (65% against animation)."; return true;
        }
        if (operation != "start") { response = "Use poseexample <player-id> start|overlap|clear."; return false; }
        Player player = Player.Get(id);
        if (player == null || !PlayerPoseRigs.TryGet(player, out var rig) ||
            player.ReferenceHub.roleManager.CurrentRole is not IFpcRole role ||
            role.FpcModule.CharacterModelInstance is not AnimatedCharacterModel model)
        { response = "A live player with a supported body is required."; return false; }
        PoseBoneDescriptor head = null;
        foreach (var bone in rig.Bones) if (bone.SemanticName == "Head") { head = bone; break; }
        Animator animator = model.GetComponentInChildren<Animator>();
        Transform transform = head == null ? null : head.Path != null ? model.transform.Find(head.Path) : animator?.GetBoneTransform((HumanBodyBones)head.HumanoidBone);
        if (transform == null) { response = "The stock head binding is unavailable."; return false; }
        _baseScope ??= PlayerPoseOverrides.CreateScope("example.poses.base");
        _overlapScope ??= PlayerPoseOverrides.CreateScope("example.poses.overlap");
        if (_demos.TryGetValue(id, out var previous)) { previous.Overlap?.Clear(); previous.Base.Clear(); _demos.Remove(id); }
        var demo = new Demo { Player = player, Rig = rig, Head = head.Id, Neutral = Quaternion.Inverse(model.transform.rotation) * transform.rotation };
        if (!_baseScope.TryCreate(player, Frame(demo, 0), new PlayerPoseOptions(), out demo.Base, out var error))
        { response = error.ToString(); return false; }
        _demos[id] = demo;
        response = "Head animation started for remote compatible Anomaly viewers; SL-VR is not required."; return true;
    }

    private void Tick()
    {
        if (Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + .05f;
        foreach (var pair in new List<KeyValuePair<int, Demo>>(_demos))
        {
            var demo = pair.Value;
            if (!demo.Base.IsValid) { demo.Overlap?.Dispose(); _demos.Remove(pair.Key); continue; }
            demo.Base.TryUpdate(Frame(demo, Mathf.Sin(Time.unscaledTime * 2) * 20), out _);
            if (demo.Overlap?.IsValid == true) demo.Overlap.TryUpdate(Frame(demo, -25), out _);
        }
    }
    private static PoseFrame Frame(Demo demo, float angle)
    {
        Quaternion q = Quaternion.AngleAxis(angle, Vector3.up) * demo.Neutral;
        return new PoseFrame(demo.Rig.Id, demo.Rig.Version, new[] { new PoseBone(demo.Head, PoseComponents.Rotation,
            0, 0, 0, q.x, q.y, q.z, q.w) });
    }
}

public sealed class Config { }

[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class PoseExampleCommand : ICommand
{
    public string Command => "poseexample";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "poseexample <player-id> start|overlap|clear";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Plugin.Instance == null || arguments.Count != 2 || !int.TryParse(arguments.Array[arguments.Offset], out int id))
        { response = Description; return false; }
        try { return Plugin.Instance.Execute(id, arguments.Array[arguments.Offset + 1].ToLowerInvariant(), out response); }
        catch (InvalidOperationException exception) { response = exception.Message; return false; }
    }
}
