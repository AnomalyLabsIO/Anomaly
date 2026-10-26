using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anomaly.Shared.Scripting;

namespace Anomaly.Shared.Content;

/// <summary>An authenticated logical source pack; identical source bytes share a transfer file.</summary>
public sealed class CatalogScriptPack
{
    public string PackId = string.Empty;
    public byte[] IndexHash;
    public uint IndexSize;
    public List<CatalogScriptSource> Sources = new List<CatalogScriptSource>();

    public void WriteTo(BinaryWriter writer)
    {
        CatalogIo.WriteStr(writer, PackId);
        CatalogIo.WriteRequiredHash(writer, IndexHash);
        writer.Write(IndexSize);
        if (Sources == null || Sources.Count < 1 || Sources.Count > ScriptPackLimits.MaxScripts)
            throw new InvalidDataException("Invalid catalog source count.");
        writer.Write((byte)Sources.Count);
        foreach (CatalogScriptSource source in Sources.OrderBy(value => value?.Address, StringComparer.Ordinal))
        {
            if (source == null) throw new InvalidDataException("Catalog contained a null source.");
            CatalogIo.WriteStr(writer, source.Address);
            CatalogIo.WriteRequiredHash(writer, source.Hash);
            writer.Write(source.Size);
        }
    }

    public static CatalogScriptPack ReadFrom(BinaryReader reader)
    {
        var pack = new CatalogScriptPack { PackId = CatalogIo.ReadStr(reader, 128), IndexHash = CatalogIo.ReadRequiredHash(reader), IndexSize = reader.ReadUInt32() };
        int count = reader.ReadByte();
        if (count < 1 || count > ScriptPackLimits.MaxScripts) throw new InvalidDataException("Invalid catalog source count.");
        for (int i = 0; i < count; i++) pack.Sources.Add(new CatalogScriptSource
        { Address = CatalogIo.ReadStr(reader, Assets.AssetAddress.MaxLength), Hash = CatalogIo.ReadRequiredHash(reader), Size = reader.ReadUInt32() });
        return pack;
    }
}

public sealed class CatalogScriptSource
{
    public string Address = string.Empty;
    public byte[] Hash;
    public uint Size;
}
