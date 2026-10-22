using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Anomaly.Shared.Poses;

/// <summary>A stable authored bone slot. Paths are relative to the character model, never traversal indices.</summary>
public sealed class PoseBoneDescriptor
{
    internal PoseBoneDescriptor(ushort id, string semanticName, int humanoidBone, string path)
    {
        Id = id;
        SemanticName = semanticName;
        HumanoidBone = humanoidBone;
        Path = path;
    }

    /// <summary>The stable bone ID within this descriptor's schema.</summary>
    public ushort Id { get; }
    /// <summary>The authored semantic name, such as Head or LeftHand.</summary>
    public string SemanticName { get; }
    /// <summary>Unity HumanBodyBones numeric value, or -1 for a path-only bone.</summary>
    public int HumanoidBone { get; }
    /// <summary>Exact transform path relative to the model root, or null for humanoid-avatar resolution.</summary>
    public string Path { get; }
}

/// <summary>Immutable built-in model contract for the supported game revision.</summary>
public sealed class PoseRigDescriptor
{
    internal PoseRigDescriptor(string id, string modelName, IEnumerable<PoseBoneDescriptor> bones)
    {
        Id = id;
        ModelName = modelName;
        Bones = new ReadOnlyCollection<PoseBoneDescriptor>(new List<PoseBoneDescriptor>(bones));
    }

    /// <summary>The stable namespaced rig identifier.</summary>
    public string Id { get; }
    /// <summary>The schema version defining the bone slots and mappings.</summary>
    public ushort Version => 1;
    /// <summary>The expected stock character model prefab name.</summary>
    public string ModelName { get; }
    /// <summary>The immutable authored mappings; never inferred from runtime traversal order.</summary>
    public IReadOnlyList<PoseBoneDescriptor> Bones { get; }

    /// <summary>Looks up a stable bone ID in this schema.</summary>
    public bool TryGetBone(ushort id, out PoseBoneDescriptor bone)
    {
        foreach (PoseBoneDescriptor candidate in Bones)
            if (candidate.Id == id) { bone = candidate; return true; }
        bone = null;
        return false;
    }
}

/// <summary>Versioned SCP:SL 14.2.7 built-in pose rigs. Custom and non-body roles fail closed.</summary>
public static class PoseRigCatalog
{
    private static readonly Dictionary<string, PoseRigDescriptor> ById = new(StringComparer.Ordinal);
    private static readonly Dictionary<sbyte, PoseRigDescriptor> ByRole = new();
    private static readonly IReadOnlyList<PoseRigDescriptor> _all;

    static PoseRigCatalog()
    {
        Add("classd", "ClassD Model", Human(), 1);
        Add("scientist", "Scientist Model", Human(), 6);
        Add("guard", "Guard Model", Human(), 14, 15);
        Add("mtf", "MTF Model", Human(), 4, 11, 12, 13);
        Add("chaos", "Chaos Insurgency Model", Human(), 8, 18, 19, 20);
        Add("scp049", "SCP-049 Model", Scp049(), 5);
        Add("scp0492", "Zombie Model", Mixamo(), 10);
        Add("scp106", "SCP-106 Model", Scp106(), 3);
        Add("scp3114", "SCP-3114 Model", Mixamo(), 23);
        Add("scp096", "SCP-096 Model", Scp096(), 9);
        Add("scp939", "SCP-939 Model", Scp939(), 16);
        _all = new ReadOnlyCollection<PoseRigDescriptor>(new List<PoseRigDescriptor>(ById.Values));
    }

    /// <summary>All supported built-in body families.</summary>
    public static IReadOnlyList<PoseRigDescriptor> All => _all;
    /// <summary>Resolves an exact rig identifier and schema version.</summary>
    public static bool TryGet(string id, ushort version, out PoseRigDescriptor descriptor)
    {
        descriptor = null;
        return id != null && version == 1 && ById.TryGetValue(id, out descriptor);
    }
    /// <summary>Resolves a stock role's underlying rig; unsupported roles return false.</summary>
    public static bool TryGetForRole(sbyte roleId, out PoseRigDescriptor descriptor) => ByRole.TryGetValue(roleId, out descriptor);

    private static void Add(string id, string model, IEnumerable<PoseBoneDescriptor> bones, params sbyte[] roles)
    {
        var rig = new PoseRigDescriptor("anomaly." + id, model, bones);
        ById.Add(rig.Id, rig);
        foreach (sbyte role in roles) ByRole.Add(role, rig);
    }

    private static readonly string[] Names = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes" };
    private static readonly int[] HumanIds = { 0, 7, 8, 54, 9, 10, 11, 13, 15, 17, 12, 14, 16, 18, 1, 3, 5, 19, 2, 4, 6, 20 };

    private static IEnumerable<PoseBoneDescriptor> Human()
    {
        for (ushort i = 0; i < Names.Length; i++)
            if (i != 3) yield return new PoseBoneDescriptor(i, Names[i], HumanIds[i], null);
        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        string[] segments = { "Proximal", "Intermediate", "Distal" };
        for (int side = 0; side < 2; side++)
            for (int finger = 0; finger < 5; finger++)
                for (int segment = 0; segment < 3; segment++)
                {
                    int offset = side * 15 + finger * 3 + segment;
                    yield return new PoseBoneDescriptor((ushort)(22 + offset), (side == 0 ? "Left" : "Right") + fingers[finger] + segments[segment], 24 + offset, null);
                }
    }

    private static List<PoseBoneDescriptor> Paths(string[] paths)
    {
        var bones = new List<PoseBoneDescriptor>();
        for (ushort i = 0; i < paths.Length; i++)
            if (paths[i] != null) bones.Add(new PoseBoneDescriptor(i, Names[i], -1, paths[i]));
        return bones;
    }

    private static IEnumerable<PoseBoneDescriptor> Mixamo()
    {
        const string h = "mixamorig:Hips";
        const string s = h + "/mixamorig:Spine";
        const string c = s + "/mixamorig:Spine1";
        const string u = c + "/mixamorig:Spine2";
        const string n = u + "/mixamorig:Neck";
        var bones = Paths(new[] { h, s, c, u, n, n + "/mixamorig:Head", u + "/mixamorig:LeftShoulder", u + "/mixamorig:LeftShoulder/mixamorig:LeftArm", u + "/mixamorig:LeftShoulder/mixamorig:LeftArm/mixamorig:LeftForeArm", u + "/mixamorig:LeftShoulder/mixamorig:LeftArm/mixamorig:LeftForeArm/mixamorig:LeftHand", u + "/mixamorig:RightShoulder", u + "/mixamorig:RightShoulder/mixamorig:RightArm", u + "/mixamorig:RightShoulder/mixamorig:RightArm/mixamorig:RightForeArm", u + "/mixamorig:RightShoulder/mixamorig:RightArm/mixamorig:RightForeArm/mixamorig:RightHand", h + "/mixamorig:LeftUpLeg", h + "/mixamorig:LeftUpLeg/mixamorig:LeftLeg", h + "/mixamorig:LeftUpLeg/mixamorig:LeftLeg/mixamorig:LeftFoot", h + "/mixamorig:LeftUpLeg/mixamorig:LeftLeg/mixamorig:LeftFoot/mixamorig:LeftToeBase", h + "/mixamorig:RightUpLeg", h + "/mixamorig:RightUpLeg/mixamorig:RightLeg", h + "/mixamorig:RightUpLeg/mixamorig:RightLeg/mixamorig:RightFoot", h + "/mixamorig:RightUpLeg/mixamorig:RightLeg/mixamorig:RightFoot/mixamorig:RightToeBase" });
        AddFingers(bones, (side, finger, segment) => "mixamorig:" + side + "Hand" + (finger == "Little" ? "Pinky" : finger) + segment);
        return bones;
    }

    private static void AddFingers(List<PoseBoneDescriptor> bones, Func<string, string, int, string> name)
    {
        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        string[] segments = { "Proximal", "Intermediate", "Distal" };
        for (int side = 0; side < 2; side++)
            for (int finger = 0; finger < 5; finger++)
            {
                string path = bones.Find(b => b.Id == (side == 0 ? 9 : 13)).Path;
                for (int segment = 0; segment < 3; segment++)
                {
                    path += "/" + name(side == 0 ? "Left" : "Right", fingers[finger], segment + 1);
                    bones.Add(new PoseBoneDescriptor((ushort)(22 + side * 15 + finger * 3 + segment), (side == 0 ? "Left" : "Right") + fingers[finger] + segments[segment], -1, path));
                }
            }
    }

    // Exact primary skeleton paths are authored from the supported stock prefabs.
    private static IEnumerable<PoseBoneDescriptor> Scp049()
    {
        var bones = Paths(new[]
        {
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_Neck1",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_Neck1/ValveBiped.Bip01_Head1",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_L_Thigh",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_L_Thigh/ValveBiped.Bip01_L_Calf",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_L_Thigh/ValveBiped.Bip01_L_Calf/ValveBiped.Bip01_L_Foot",
            null,
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_R_Thigh",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_R_Thigh/ValveBiped.Bip01_R_Calf",
            "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_R_Thigh/ValveBiped.Bip01_R_Calf/ValveBiped.Bip01_R_Foot",
            null,
        });
        bones.Add(new PoseBoneDescriptor(22, "LeftThumbProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger0"));
        bones.Add(new PoseBoneDescriptor(23, "LeftThumbIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger0/ValveBiped.Bip01_L_Finger01"));
        bones.Add(new PoseBoneDescriptor(24, "LeftThumbDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger0/ValveBiped.Bip01_L_Finger01/ValveBiped.Bip01_L_Finger02"));
        bones.Add(new PoseBoneDescriptor(25, "LeftIndexProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger1"));
        bones.Add(new PoseBoneDescriptor(26, "LeftIndexIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger1/ValveBiped.Bip01_L_Finger11"));
        bones.Add(new PoseBoneDescriptor(27, "LeftIndexDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger1/ValveBiped.Bip01_L_Finger11/ValveBiped.Bip01_L_Finger12"));
        bones.Add(new PoseBoneDescriptor(28, "LeftMiddleProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger2"));
        bones.Add(new PoseBoneDescriptor(29, "LeftMiddleIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger2/ValveBiped.Bip01_L_Finger21"));
        bones.Add(new PoseBoneDescriptor(30, "LeftMiddleDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger2/ValveBiped.Bip01_L_Finger21/ValveBiped.Bip01_L_Finger22"));
        bones.Add(new PoseBoneDescriptor(31, "LeftRingProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger3"));
        bones.Add(new PoseBoneDescriptor(32, "LeftRingIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger3/ValveBiped.Bip01_L_Finger31"));
        bones.Add(new PoseBoneDescriptor(33, "LeftRingDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger3/ValveBiped.Bip01_L_Finger31/ValveBiped.Bip01_L_Finger32"));
        bones.Add(new PoseBoneDescriptor(34, "LeftLittleProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger4"));
        bones.Add(new PoseBoneDescriptor(35, "LeftLittleIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger4/ValveBiped.Bip01_L_Finger41"));
        bones.Add(new PoseBoneDescriptor(36, "LeftLittleDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_L_Clavicle/ValveBiped.Bip01_L_UpperArm/ValveBiped.Bip01_L_Forearm/ValveBiped.Bip01_L_Hand/ValveBiped.Bip01_L_Finger4/ValveBiped.Bip01_L_Finger41/ValveBiped.Bip01_L_Finger42"));
        bones.Add(new PoseBoneDescriptor(37, "RightThumbProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger0"));
        bones.Add(new PoseBoneDescriptor(38, "RightThumbIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger0/ValveBiped.Bip01_R_Finger01"));
        bones.Add(new PoseBoneDescriptor(39, "RightThumbDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger0/ValveBiped.Bip01_R_Finger01/ValveBiped.Bip01_R_Finger02"));
        bones.Add(new PoseBoneDescriptor(40, "RightIndexProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger1"));
        bones.Add(new PoseBoneDescriptor(41, "RightIndexIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger1/ValveBiped.Bip01_R_Finger11"));
        bones.Add(new PoseBoneDescriptor(42, "RightIndexDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger1/ValveBiped.Bip01_R_Finger11/ValveBiped.Bip01_R_Finger12"));
        bones.Add(new PoseBoneDescriptor(43, "RightMiddleProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger2"));
        bones.Add(new PoseBoneDescriptor(44, "RightMiddleIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger2/ValveBiped.Bip01_R_Finger21"));
        bones.Add(new PoseBoneDescriptor(45, "RightMiddleDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger2/ValveBiped.Bip01_R_Finger21/ValveBiped.Bip01_R_Finger22"));
        bones.Add(new PoseBoneDescriptor(46, "RightRingProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger3"));
        bones.Add(new PoseBoneDescriptor(47, "RightRingIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger3/ValveBiped.Bip01_R_Finger31"));
        bones.Add(new PoseBoneDescriptor(48, "RightRingDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger3/ValveBiped.Bip01_R_Finger31/ValveBiped.Bip01_R_Finger32"));
        bones.Add(new PoseBoneDescriptor(49, "RightLittleProximal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger4"));
        bones.Add(new PoseBoneDescriptor(50, "RightLittleIntermediate", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger4/ValveBiped.Bip01_R_Finger41"));
        bones.Add(new PoseBoneDescriptor(51, "RightLittleDistal", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4/ValveBiped.Bip01_R_Clavicle/ValveBiped.Bip01_R_UpperArm/ValveBiped.Bip01_R_Forearm/ValveBiped.Bip01_R_Hand/ValveBiped.Bip01_R_Finger4/ValveBiped.Bip01_R_Finger41/ValveBiped.Bip01_R_Finger42"));
        bones.Add(new PoseBoneDescriptor(64, "UpperSpine", -1, "scp049_player_reference_skeleton/ValveBiped.Bip01_Pelvis/ValveBiped.Bip01_Spine/ValveBiped.Bip01_Spine1/ValveBiped.Bip01_Spine2/ValveBiped.Bip01_Spine4"));
        return bones;
    }
    private static IEnumerable<PoseBoneDescriptor> Scp106()
    {
        var bones = Paths(new[]
        {
            "armature/Root/HipsCTRL/Pelvis",
            "armature/Root/HipsCTRL/Pelvis/Stomach",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/neck",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/neck/Head",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R",
            "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R",
            "armature/Root/HipsCTRL/Pelvis/Leg.L",
            "armature/Root/HipsCTRL/Pelvis/Leg.L/Lowerleg.L",
            "armature/Root/HipsCTRL/Pelvis/Leg.L/Lowerleg.L/Foot.L",
            "armature/Root/HipsCTRL/Pelvis/Leg.L/Lowerleg.L/Foot.L/Footfingers.L",
            "armature/Root/HipsCTRL/Pelvis/Leg.R",
            "armature/Root/HipsCTRL/Pelvis/Leg.R/Lowerleg.R",
            "armature/Root/HipsCTRL/Pelvis/Leg.R/Lowerleg.R/Foot.R",
            "armature/Root/HipsCTRL/Pelvis/Leg.R/Lowerleg.R/Foot.R/Footfingers.R",
        });
        bones.Add(new PoseBoneDescriptor(22, "LeftThumbProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Thumb1.L"));
        bones.Add(new PoseBoneDescriptor(23, "LeftThumbIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Thumb1.L/Thumb2.L"));
        bones.Add(new PoseBoneDescriptor(24, "LeftThumbDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Thumb1.L/Thumb2.L/Thumb3.L"));
        bones.Add(new PoseBoneDescriptor(25, "LeftIndexProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Index1.L"));
        bones.Add(new PoseBoneDescriptor(26, "LeftIndexIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Index1.L/Index2.L"));
        bones.Add(new PoseBoneDescriptor(27, "LeftIndexDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Index1.L/Index2.L/Index3.L"));
        bones.Add(new PoseBoneDescriptor(28, "LeftMiddleProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Mid1.L"));
        bones.Add(new PoseBoneDescriptor(29, "LeftMiddleIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Mid1.L/Mid2.L"));
        bones.Add(new PoseBoneDescriptor(30, "LeftMiddleDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Mid1.L/Mid2.L/Mid3.L"));
        bones.Add(new PoseBoneDescriptor(31, "LeftRingProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Ring1.L"));
        bones.Add(new PoseBoneDescriptor(32, "LeftRingIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Ring1.L/Ring2.L"));
        bones.Add(new PoseBoneDescriptor(33, "LeftRingDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Ring1.L/Ring2.L/Ring3.L"));
        bones.Add(new PoseBoneDescriptor(34, "LeftLittleProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Pinky1.L"));
        bones.Add(new PoseBoneDescriptor(35, "LeftLittleIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Pinky1.L/Pinky2.L"));
        bones.Add(new PoseBoneDescriptor(36, "LeftLittleDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.L/Arm.L/Forearm.L/Hand.L/Pinky1.L/Pinky2.L/Pinky3.L"));
        bones.Add(new PoseBoneDescriptor(37, "RightThumbProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Thumb1.R"));
        bones.Add(new PoseBoneDescriptor(38, "RightThumbIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Thumb1.R/Thumb2.R"));
        bones.Add(new PoseBoneDescriptor(39, "RightThumbDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Thumb1.R/Thumb2.R/Thumb3.R"));
        bones.Add(new PoseBoneDescriptor(40, "RightIndexProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Index1.R"));
        bones.Add(new PoseBoneDescriptor(41, "RightIndexIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Index1.R/Index2.R"));
        bones.Add(new PoseBoneDescriptor(42, "RightIndexDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Index1.R/Index2.R/Index3.R"));
        bones.Add(new PoseBoneDescriptor(43, "RightMiddleProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Mid1.R"));
        bones.Add(new PoseBoneDescriptor(44, "RightMiddleIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Mid1.R/Mid2.R"));
        bones.Add(new PoseBoneDescriptor(45, "RightMiddleDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Mid1.R/Mid2.R/Mid3.R"));
        bones.Add(new PoseBoneDescriptor(46, "RightRingProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Ring1.R"));
        bones.Add(new PoseBoneDescriptor(47, "RightRingIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Ring1.R/Ring2.R"));
        bones.Add(new PoseBoneDescriptor(48, "RightRingDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Ring1.R/Ring2.R/Ring3.R"));
        bones.Add(new PoseBoneDescriptor(49, "RightLittleProximal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Pinky1.R"));
        bones.Add(new PoseBoneDescriptor(50, "RightLittleIntermediate", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Pinky1.R/Pinky2.R"));
        bones.Add(new PoseBoneDescriptor(51, "RightLittleDistal", -1, "armature/Root/HipsCTRL/Pelvis/Stomach/LowerChest/UpperChest/Shoulder.R/Arm.R/Forearm.R/Hand.R/Pinky1.R/Pinky2.R/Pinky3.R"));
        return bones;
    }
    private static IEnumerable<PoseBoneDescriptor> Scp096()
    {
        var bones = Paths(new[]
        {
            "SCP-096/root/Hips",
            "SCP-096/root/Hips/Spine01",
            "SCP-096/root/Hips/Spine01/Spine02",
            "SCP-096/root/Hips/Spine01/Spine02/Spine03",
            "SCP-096/root/Hips/Spine01/Spine02/Spine03/Neck01/Neck02",
            "SCP-096/root/Hips/Spine01/Spine02/Spine03/Neck01/Neck02/head",
            "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder",
            "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U",
            "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L",
            "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand",
            "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder",
            "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U",
            "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L",
            "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand",
            "SCP-096/root/Hips/L.Thiegh",
            "SCP-096/root/Hips/L.Thiegh/L.Calf",
            "SCP-096/root/Hips/L.Thiegh/L.Calf/L.Foot",
            "SCP-096/root/Hips/L.Thiegh/L.Calf/L.Foot/L.Toes",
            "SCP-096/root/Hips/R.Thiegh",
            "SCP-096/root/Hips/R.Thiegh/R.Calf",
            "SCP-096/root/Hips/R.Thiegh/R.Calf/R.Foot",
            "SCP-096/root/Hips/R.Thiegh/R.Calf/R.Foot/R.Toes",
        });
        bones.Add(new PoseBoneDescriptor(22, "LeftThumbProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Thumb.1"));
        bones.Add(new PoseBoneDescriptor(23, "LeftThumbIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Thumb.1/L.Thumb.2"));
        bones.Add(new PoseBoneDescriptor(24, "LeftThumbDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Thumb.1/L.Thumb.2/L.Thumb.3"));
        bones.Add(new PoseBoneDescriptor(25, "LeftIndexProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Finger.A.1"));
        bones.Add(new PoseBoneDescriptor(26, "LeftIndexIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Finger.A.1/L.Finger.A.2"));
        bones.Add(new PoseBoneDescriptor(27, "LeftIndexDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.01.L/L.Finger.A.1/L.Finger.A.2/L.Finger.A.3"));
        bones.Add(new PoseBoneDescriptor(28, "LeftMiddleProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.02.L/L.Finger.B.1"));
        bones.Add(new PoseBoneDescriptor(29, "LeftMiddleIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.02.L/L.Finger.B.1/L.Finger.B.2"));
        bones.Add(new PoseBoneDescriptor(30, "LeftMiddleDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.02.L/L.Finger.B.1/L.Finger.B.2/L.Finger.B.3"));
        bones.Add(new PoseBoneDescriptor(31, "LeftRingProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.03.L/L.Finger.C.1"));
        bones.Add(new PoseBoneDescriptor(32, "LeftRingIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.03.L/L.Finger.C.1/L.Finger.C.2"));
        bones.Add(new PoseBoneDescriptor(33, "LeftRingDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.03.L/L.Finger.C.1/L.Finger.C.2/L.Finger.C.3"));
        bones.Add(new PoseBoneDescriptor(34, "LeftLittleProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.04.L/L.Finger.D.1"));
        bones.Add(new PoseBoneDescriptor(35, "LeftLittleIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.04.L/L.Finger.D.1/L.Finger.D.2"));
        bones.Add(new PoseBoneDescriptor(36, "LeftLittleDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/L.Shoulder/L.Arm_U/L.Arm_L/L.Hand/ORG-palm.04.L/L.Finger.D.1/L.Finger.D.2/L.Finger.D.3"));
        bones.Add(new PoseBoneDescriptor(37, "RightThumbProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Thumb.1"));
        bones.Add(new PoseBoneDescriptor(38, "RightThumbIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Thumb.1/R.Thumb.2"));
        bones.Add(new PoseBoneDescriptor(39, "RightThumbDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Thumb.1/R.Thumb.2/R.Thumb.3"));
        bones.Add(new PoseBoneDescriptor(40, "RightIndexProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Finger.A.1"));
        bones.Add(new PoseBoneDescriptor(41, "RightIndexIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Finger.A.1/R.Finger.A.2"));
        bones.Add(new PoseBoneDescriptor(42, "RightIndexDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.01.R/R.Finger.A.1/R.Finger.A.2/R.Finger.A.3"));
        bones.Add(new PoseBoneDescriptor(43, "RightMiddleProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.02.R/R.Finger.B.1"));
        bones.Add(new PoseBoneDescriptor(44, "RightMiddleIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.02.R/R.Finger.B.1/R.Finger.B.2"));
        bones.Add(new PoseBoneDescriptor(45, "RightMiddleDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.02.R/R.Finger.B.1/R.Finger.B.2/R.Finger.B.3"));
        bones.Add(new PoseBoneDescriptor(46, "RightRingProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.03.R/R.Finger.C.1"));
        bones.Add(new PoseBoneDescriptor(47, "RightRingIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.03.R/R.Finger.C.1/R.Finger.C.2"));
        bones.Add(new PoseBoneDescriptor(48, "RightRingDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.03.R/R.Finger.C.1/R.Finger.C.2/R.Finger.C.3"));
        bones.Add(new PoseBoneDescriptor(49, "RightLittleProximal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.04.R/R.Finger.D.1"));
        bones.Add(new PoseBoneDescriptor(50, "RightLittleIntermediate", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.04.R/R.Finger.D.1/R.Finger.D.2"));
        bones.Add(new PoseBoneDescriptor(51, "RightLittleDistal", -1, "SCP-096/root/Hips/Spine01/Spine02/R.Shoulder/R.Arm_U/R.Arm_L/R.Hand/ORG-palm.04.R/R.Finger.D.1/R.Finger.D.2/R.Finger.D.3"));
        bones.Add(new PoseBoneDescriptor(64, "LowerNeck", -1, "SCP-096/root/Hips/Spine01/Spine02/Spine03/Neck01"));
        return bones;
    }
    private static IEnumerable<PoseBoneDescriptor> Scp939()
    {
        var bones = Paths(new[]
        {
            "Anims/939Rig/HipControl/DEF-Hips",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest",
            null,
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Neck",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Neck/DEF-Head",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.L/DEF-Thigh.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.L/DEF-Thigh.L/DEF-Leg.L",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.L/DEF-Thigh.L/DEF-Leg.L/DEF-Foot.L",
            null,
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.R/DEF-Thigh.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.R/DEF-Thigh.R/DEF-Leg.R",
            "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.R/DEF-Thigh.R/DEF-Leg.R/DEF-Foot.R",
            null,
        });
        bones.Add(new PoseBoneDescriptor(22, "LeftThumbProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Thumb.L.001"));
        bones.Add(new PoseBoneDescriptor(23, "LeftThumbIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Thumb.L.001/DEF-Thumb.L.002"));
        bones.Add(new PoseBoneDescriptor(24, "LeftThumbDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Thumb.L.001/DEF-Thumb.L.002/DEF-Thumb.L.003"));
        bones.Add(new PoseBoneDescriptor(25, "LeftIndexProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.A.L.001"));
        bones.Add(new PoseBoneDescriptor(26, "LeftIndexIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.A.L.001/DEF-Finger.A.L.002"));
        bones.Add(new PoseBoneDescriptor(27, "LeftIndexDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.A.L.001/DEF-Finger.A.L.002/DEF-Finger.A.L.003"));
        bones.Add(new PoseBoneDescriptor(28, "LeftMiddleProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.B.L.001"));
        bones.Add(new PoseBoneDescriptor(29, "LeftMiddleIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.B.L.001/DEF-Finger.B.L.002"));
        bones.Add(new PoseBoneDescriptor(30, "LeftMiddleDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.B.L.001/DEF-Finger.B.L.002/DEF-Finger.B.L.003"));
        bones.Add(new PoseBoneDescriptor(34, "LeftLittleProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.C.L.001"));
        bones.Add(new PoseBoneDescriptor(35, "LeftLittleIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.C.L.001/DEF-Finger.C.L.002"));
        bones.Add(new PoseBoneDescriptor(36, "LeftLittleDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L/DEF-Hand.L/DEF-Finger.C.L.001/DEF-Finger.C.L.002/DEF-Finger.C.L.003"));
        bones.Add(new PoseBoneDescriptor(37, "RightThumbProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Thumb.R.001"));
        bones.Add(new PoseBoneDescriptor(38, "RightThumbIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Thumb.R.001/DEF-Thumb.R.002"));
        bones.Add(new PoseBoneDescriptor(39, "RightThumbDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Thumb.R.001/DEF-Thumb.R.002/DEF-Thumb.R.003"));
        bones.Add(new PoseBoneDescriptor(40, "RightIndexProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.A.R.001"));
        bones.Add(new PoseBoneDescriptor(41, "RightIndexIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.A.R.001/DEF-Finger.A.R.002"));
        bones.Add(new PoseBoneDescriptor(42, "RightIndexDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.A.R.001/DEF-Finger.A.R.002/DEF-Finger.A.R.003"));
        bones.Add(new PoseBoneDescriptor(43, "RightMiddleProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.B.R.001"));
        bones.Add(new PoseBoneDescriptor(44, "RightMiddleIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.B.R.001/DEF-Finger.B.R.002"));
        bones.Add(new PoseBoneDescriptor(45, "RightMiddleDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.B.R.001/DEF-Finger.B.R.002/DEF-Finger.B.R.003"));
        bones.Add(new PoseBoneDescriptor(49, "RightLittleProximal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.C.R.001"));
        bones.Add(new PoseBoneDescriptor(50, "RightLittleIntermediate", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.C.R.001/DEF-Finger.C.R.002"));
        bones.Add(new PoseBoneDescriptor(51, "RightLittleDistal", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R/DEF-Hand.R/DEF-Finger.C.R.001/DEF-Finger.C.R.002/DEF-Finger.C.R.003"));
        bones.Add(new PoseBoneDescriptor(64, "LeftWrist", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.L/DEF-UpperArm.L/DEF-LowerArm.L/DEF-Wrist.L"));
        bones.Add(new PoseBoneDescriptor(65, "RightWrist", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Stomach/DEF-Chest/DEF-Shoulder.R/DEF-UpperArm.R/DEF-LowerArm.R/DEF-Wrist.R"));
        bones.Add(new PoseBoneDescriptor(66, "LeftHip", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.L"));
        bones.Add(new PoseBoneDescriptor(67, "RightHip", -1, "Anims/939Rig/HipControl/DEF-Hips/DEF-Hip.R"));
        return bones;
    }
}
