using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Capabilities;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Client-to-server replacement snapshot. A complete snapshot avoids
    /// capability state leaking when a mod unloads or re-registers a version.
    /// </summary>
    public sealed class CapabilitySnapshotMessage :
        AuthenticatedClientToServerMessage,
        IAnomalyMessage,
        IRoundScopedAnomalyMessage
    {
        public const int MaxCapabilities = 32;

        public string MessageName => "anomaly:capability:snapshot";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 4096;

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
                string id = ReadBoundedId(reader);

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

        private static string ReadBoundedId(BinaryReader reader)
        {
            // BinaryWriter.Write(string) prefixes the UTF-8 byte count with a
            // seven-bit encoded integer. Parse and cap that prefix before any
            // allocation; BinaryReader.ReadString would allocate from an
            // attacker-controlled declared length before descriptor validation.
            int byteCount = 0;
            int shift = 0;
            for (int index = 0; index < 5; index++)
            {
                byte value = reader.ReadByte();
                byteCount |= (value & 0x7f) << shift;
                if ((value & 0x80) == 0)
                {
                    if (byteCount <= 0 || byteCount > CapabilityDescriptor.MaxIdLength)
                        throw new InvalidDataException("Capability id is missing or too long.");
                    byte[] bytes = reader.ReadBytes(byteCount);
                    if (bytes.Length != byteCount)
                        throw new EndOfStreamException("Capability id was truncated.");
                    return Encoding.UTF8.GetString(bytes);
                }
                shift += 7;
            }

            throw new InvalidDataException("Capability id length prefix is invalid.");
        }
    }
}
