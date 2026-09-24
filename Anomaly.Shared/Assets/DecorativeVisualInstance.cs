using System;
using System.IO;

namespace Anomaly.Shared.Assets;

public enum DecorativeVisualOperation : byte { Upsert = 1, Remove = 2 }

/// <summary>Engine-independent state for one decorative visual instance.</summary>
public sealed class DecorativeVisualInstance
{
    public uint Id;
    public string Address = string.Empty;
    public float[] Position = { 0, 0, 0 };
    public float[] Rotation = { 0, 0, 0, 1 };
    public float[] Scale = { 1, 1, 1 };

    public void Validate()
    {
        if (Id == 0 || !AssetAddress.TryParse(Address, out AssetAddress address) || address.IsGameReplacement)
            throw new InvalidDataException("Invalid decorative instance id/address.");
        AssetPackIndexCodec.ValidateTransform(Position, Rotation, Scale);
    }
    public DecorativeVisualInstance Clone() => new DecorativeVisualInstance
    {
        Id = Id, Address = Address, Position = (float[])Position.Clone(), Rotation = (float[])Rotation.Clone(), Scale = (float[])Scale.Clone(),
    };
    internal void Write(BinaryWriter writer)
    {
        Validate(); writer.Write(Id); AssetAddressWire.Write(writer, Address);
        foreach (float value in Position) writer.Write(value);
        foreach (float value in Rotation) writer.Write(value);
        foreach (float value in Scale) writer.Write(value);
    }
    internal static DecorativeVisualInstance Read(BinaryReader reader)
    {
        var instance = new DecorativeVisualInstance { Id = reader.ReadUInt32(), Address = AssetAddressWire.Read(reader) };
        for (int i = 0; i < 3; i++) instance.Position[i] = reader.ReadSingle();
        for (int i = 0; i < 4; i++) instance.Rotation[i] = reader.ReadSingle();
        for (int i = 0; i < 3; i++) instance.Scale[i] = reader.ReadSingle();
        instance.Validate(); return instance;
    }
}
