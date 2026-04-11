using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Anomaly.Server.Networking.Messages;

public class BoneTransformsMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:bonetransforms";
    public MessageChannel TransportChannel => MessageChannel.Sequenced; // Sequenced

    public uint NetId { get; set; }
    public List<BoneEntry> Bones { get; set; } = new List<BoneEntry>();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write((byte)Bones.Count);
        foreach (var bone in Bones)
        {
            writer.Write(bone.Index);
            writer.Write(bone.Position.x);
            writer.Write(bone.Position.y);
            writer.Write(bone.Position.z);
            writer.Write(bone.Rotation.x);
            writer.Write(bone.Rotation.y);
            writer.Write(bone.Rotation.z);
            writer.Write(bone.Rotation.w);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadUInt32();
        int count = reader.ReadByte();
        Bones = new List<BoneEntry>(count);
        for (int i = 0; i < count; i++)
        {
            byte index = reader.ReadByte();
            var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Bones.Add(new BoneEntry(index, pos, rot));
        }
    }

    public struct BoneEntry
    {
        public byte Index;
        public Vector3 Position;
        public Quaternion Rotation;

        public BoneEntry(byte index, Vector3 position, Quaternion rotation)
        {
            Index = index;
            Position = position;
            Rotation = rotation;
        }
    }
}
