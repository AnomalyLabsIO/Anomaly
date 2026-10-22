using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Poses;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>Reliable pose bindings, activation snapshots, and generation tombstones.</summary>
public sealed class PlayerPoseControlMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const int MaxControls = 64;
    public override int MaxPayloadBytes => 256 * 1024;
    public string MessageName => "anomaly:pose:control";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public uint RoundEpoch { get; set; }
    public List<PoseControl> Controls { get; set; } = new List<PoseControl>();
    public void Serialize(BinaryWriter writer)
    {
        ValidateHeader(); writer.Write(RoundEpoch); writer.Write((ushort)Controls.Count);
        foreach (var control in Controls)
        {
            Validate(control); writer.Write((byte)control.Operation);
            if (control.Operation == PoseControlOperation.ResetAll) continue;
            PoseWireCodec.WriteBinding(writer, control.Binding);
            if (control.Operation == PoseControlOperation.Bind) continue;
            writer.Write(control.OverrideGeneration);
            if (control.Operation == PoseControlOperation.Clear) continue;
            writer.Write(control.Sequence); writer.Write(control.ServerTime); PoseWireCodec.WriteBones(writer, control.Frame.Bones);
        }
    }
    public void Deserialize(BinaryReader reader)
    {
        RoundEpoch = reader.ReadUInt32(); int count = reader.ReadUInt16();
        if (RoundEpoch == 0 || count > MaxControls) throw new InvalidDataException("Invalid pose control header.");
        var controls = new List<PoseControl>(count);
        for (int i = 0; i < count; i++)
        {
            var control = new PoseControl { Operation = (PoseControlOperation)reader.ReadByte() };
            if (control.Operation != PoseControlOperation.ResetAll)
            {
                control.Binding = PoseWireCodec.ReadBinding(reader);
                if (control.Operation != PoseControlOperation.Bind)
                {
                    control.OverrideGeneration = reader.ReadUInt64();
                    if (control.Operation == PoseControlOperation.Activate)
                    {
                        control.Sequence = reader.ReadUInt64(); control.ServerTime = reader.ReadDouble();
                        control.Frame = new PoseFrame(control.Binding.RigId, control.Binding.SchemaVersion, PoseWireCodec.ReadBones(reader));
                    }
                }
            }
            Validate(control); controls.Add(control);
        }
        Controls = controls;
    }
    private void ValidateHeader()
    {
        if (RoundEpoch == 0 || Controls == null || Controls.Count > MaxControls) throw new InvalidDataException("Invalid pose control header.");
    }
    private static void Validate(PoseControl control)
    {
        if (control == null || control.Operation < PoseControlOperation.Bind || control.Operation > PoseControlOperation.ResetAll)
            throw new InvalidDataException("Unknown pose control operation.");
        if (control.Operation == PoseControlOperation.ResetAll) return;
        if (control.Binding == null) throw new InvalidDataException("Missing pose binding.");
        control.Binding.Validate();
        if (control.Operation == PoseControlOperation.Bind) return;
        if (control.OverrideGeneration == 0) throw new InvalidDataException("Invalid pose generation.");
        if (control.Operation == PoseControlOperation.Clear) return;
        if (control.Sequence == 0 || !PoseWireCodec.ValidTime(control.ServerTime) || control.Frame == null ||
            control.Frame.RigId != control.Binding.RigId || control.Frame.SchemaVersion != control.Binding.SchemaVersion)
            throw new InvalidDataException("Invalid pose activation.");
    }
}
