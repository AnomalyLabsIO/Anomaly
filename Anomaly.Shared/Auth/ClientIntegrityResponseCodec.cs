using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Anomaly.Shared.Auth;

public sealed class ClientIntegrityResponse
{
    public const string Discriminator = "anomaly.authentication_integrity";
    public const int SchemaVersion = 1;
    public ClientIntegrityAuditFlags Flags { get; }

    internal ClientIntegrityResponse(ClientIntegrityAuditFlags flags) => Flags = flags;
}

/// <summary>Bounded canonical custom rejection for an authenticated client's reported integrity risk.</summary>
public static class ClientIntegrityResponseCodec
{
    public const int MaxUtf8Bytes = ModAdmissionResponseCodec.MaxUtf8Bytes;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Encode(ClientIntegrityAuditFlags flags)
    {
        if ((flags & ~ClientIntegrityAudit.KnownFlags) != 0 || (flags & ClientIntegrityAudit.RiskyFlags) == 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "An integrity rejection requires known risky flags.");
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(ResponseWire)).WriteObject(stream, new ResponseWire
            {
                Kind = ClientIntegrityResponse.Discriminator,
                Version = ClientIntegrityResponse.SchemaVersion,
                Flags = (ushort)flags,
            });
            if (stream.Length > MaxUtf8Bytes)
                throw new InvalidOperationException("Client integrity rejection exceeds the packet bound.");
            return StrictUtf8.GetString(stream.ToArray());
        }
    }

    public static bool TryDecode(string json, out ClientIntegrityResponse response)
    {
        response = null;
        if (string.IsNullOrEmpty(json))
            return false;
        try
        {
            var bytes = StrictUtf8.GetBytes(json);
            if (bytes.Length > MaxUtf8Bytes)
                return false;
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, new XmlDictionaryReaderQuotas
            {
                MaxDepth = 4,
                MaxStringContentLength = MaxUtf8Bytes,
                MaxArrayLength = MaxUtf8Bytes,
            }))
            {
                var wire = new DataContractJsonSerializer(typeof(ResponseWire)).ReadObject(reader) as ResponseWire;
                if (wire == null || reader.MoveToContent() != XmlNodeType.None
                    || wire.Kind != ClientIntegrityResponse.Discriminator
                    || wire.Version != ClientIntegrityResponse.SchemaVersion)
                    return false;
                var flags = (ClientIntegrityAuditFlags)wire.Flags;
                if ((flags & ~ClientIntegrityAudit.KnownFlags) != 0 || (flags & ClientIntegrityAudit.RiskyFlags) == 0)
                    return false;
                // The framework JSON reader can stop at the first object while buffering
                // trailing data. Only our canonical bounded representation is accepted.
                if (!StringComparer.Ordinal.Equals(json, Encode(flags)))
                    return false;
                response = new ClientIntegrityResponse(flags);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    [DataContract]
    private sealed class ResponseWire
    {
        [DataMember(Name = "kind", Order = 0, IsRequired = true)] public string Kind;
        [DataMember(Name = "version", Order = 1, IsRequired = true)] public int Version;
        [DataMember(Name = "flags", Order = 2, IsRequired = true)] public ushort Flags;
    }
}
