using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Capabilities;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Client-to-server replacement snapshot. A complete snapshot avoids
    /// capability state leaking when a mod unloads or re-registers a version.
    /// </summary>
    public sealed class CapabilitySnapshotMessage :
        IAnomalyMessage,
        IDirectionalAnomalyMessage,
        IBoundedAnomalyMessage,
        IRoundScopedAnomalyMessage
    {
        public const int MaxCapabilities = 32;

        public string MessageName => "anomaly:capability:snapshot";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public AnomalyMessageDirection Direction => AnomalyMessageDirection.ClientToServer;
        public int MaxPayloadBytes => 4096;

        public uint RoundEpoch { get; set; }
        public uint Sequence { get; set; }
        public List<CapabilityDescriptor> Capabilities { get; } = new List<CapabilityDescriptor>();

        public void Serialize(BinaryWriter writer)
        {
            if (Capabilities.Count > MaxCapabilities)
                throw new InvalidDataException($"Capability count exceeds {MaxCapabilities}.");

            writer.Write(RoundEpoch);
            writer.Write(Sequence);
            writer.Write((byte)Capabilities.Count);
            for (int i = 0; i < Capabilities.Count; i++)
            {
                CapabilityDescriptor descriptor = Capabilities[i] ??
                    throw new InvalidDataException("Capability snapshot contains a null descriptor.");
                writer.Write(descriptor.Id);
                writer.Write(descriptor.Major);
                writer.Write(descriptor.Minor);
                writer.Write(descriptor.Patch);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            RoundEpoch = reader.ReadUInt32();
            Sequence = reader.ReadUInt32();

            int count = reader.ReadByte();
            if (count > MaxCapabilities)
                throw new InvalidDataException($"Capability count exceeds {MaxCapabilities}.");

            Capabilities.Clear();
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                if (id == null || id.Length > CapabilityDescriptor.MaxIdLength)
                    throw new InvalidDataException("Capability id is missing or too long.");

                try
                {
                    Capabilities.Add(new CapabilityDescriptor(
                        id,
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16()));
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException("Capability descriptor is invalid.", ex);
                }
            }
        }
    }
}
