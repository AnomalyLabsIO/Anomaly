using System;
using System.IO;

namespace Anomaly.Shared.Auth;

[Flags]
public enum ClientIntegrityAuditFlags : ushort
{
    None = 0,
    ExternalHarmonyModification = 1,
    ExternalDetourModification = 2,
    CoverageIncomplete = 4,
    InventoryDivergence = 8,
    BlockedAttemptObserved = 16,
}

/// <summary>An authenticated client claim about observed protection state, not remote attestation.</summary>
public sealed class ClientIntegrityAudit
{
    public const byte SchemaVersion = 1;
    public const ushort CatalogVersion = 1;
    public const int EncodedSize = 6;
    public const ClientIntegrityAuditFlags RiskyFlags = (ClientIntegrityAuditFlags)15;
    public const ClientIntegrityAuditFlags KnownFlags = (ClientIntegrityAuditFlags)31;

    public ClientIntegrityAuditFlags Flags { get; }
    public bool AllowExternalAuthenticationModifications { get; }
    public bool IsRisky => (Flags & RiskyFlags) != 0;

    public ClientIntegrityAudit(ClientIntegrityAuditFlags flags, bool allowExternalAuthenticationModifications)
    {
        if ((flags & ~KnownFlags) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "Unknown client integrity audit flags.");
        Flags = flags;
        AllowExternalAuthenticationModifications = allowExternalAuthenticationModifications;
    }

    public byte[] Encode()
    {
        return new[]
        {
            SchemaVersion,
            (byte)CatalogVersion, (byte)(CatalogVersion >> 8),
            (byte)Flags, (byte)((ushort)Flags >> 8),
            AllowExternalAuthenticationModifications ? (byte)1 : (byte)0,
        };
    }

    public static ClientIntegrityAudit Decode(byte[] bytes)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length != EncodedSize || bytes[0] != SchemaVersion
            || (bytes[1] | (bytes[2] << 8)) != CatalogVersion || bytes[5] > 1)
            throw new InvalidDataException("Client integrity audit has an invalid size, version, catalog or option.");
        var flags = (ClientIntegrityAuditFlags)(bytes[3] | (bytes[4] << 8));
        if ((flags & ~KnownFlags) != 0)
            throw new InvalidDataException("Client integrity audit contains unknown flags.");
        return new ClientIntegrityAudit(flags, bytes[5] == 1);
    }
}
