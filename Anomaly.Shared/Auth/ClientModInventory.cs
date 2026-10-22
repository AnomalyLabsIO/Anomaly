using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Anomaly.Shared.Auth;

/// <summary>A client claim about a mod loaded during this process lifetime, not remote attestation.</summary>
public sealed class ClientModEntry
{
    public string Id { get; }
    public string Version { get; }

    public ClientModEntry(string id, string version)
    {
        Id = ClientModInventory.ValidateText(id, ClientModInventory.MaxIdBytes, nameof(id));
        Version = ClientModInventory.ValidateText(version, ClientModInventory.MaxVersionBytes, nameof(version));
    }
}

/// <summary>Canonical, bounded inventory sent in the encrypted client login.</summary>
public sealed class ClientModInventory
{
    public const string CoreId = "anomaly.client";
    public const int MaxEntries = 32;
    public const int MaxEncodedBytes = 768;
    public const int MaxIdBytes = 128;
    public const int MaxVersionBytes = 48;

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly ClientModEntry[] _entries;
    private readonly IReadOnlyList<ClientModEntry> _readOnlyEntries;

    public static ClientModInventory Empty { get; } = new ClientModInventory(Array.Empty<ClientModEntry>());
    public IReadOnlyList<ClientModEntry> Entries => _readOnlyEntries;

    public ClientModInventory(IEnumerable<ClientModEntry> entries)
    {
        if (entries == null)
            throw new ArgumentNullException(nameof(entries));

        _entries = entries.ToArray();
        if (_entries.Length > MaxEntries)
            throw new ArgumentException($"Client mod inventory cannot exceed {MaxEntries} entries.", nameof(entries));
        if (_entries.Any(entry => entry == null))
            throw new ArgumentException("Client mod inventory contains a null entry.", nameof(entries));

        // Ordinal wire order is stable across client and server runtimes.
        Array.Sort(_entries, (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        // Policy matches IDs without case, so two names that differ only by case
        // cannot be allowed to carry conflicting versions.
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            if (!knownIds.Add(entry.Id))
                throw new ArgumentException("Client mod inventory contains duplicate ID '" + entry.Id + "'.", nameof(entries));
        }

        _readOnlyEntries = Array.AsReadOnly(_entries);
        // Validate the aggregate bound during construction so callers fail before connecting.
        Encode();
    }

    public byte[] Encode()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)_entries.Length);
            foreach (var entry in _entries)
            {
                var id = StrictUtf8.GetBytes(entry.Id);
                var version = StrictUtf8.GetBytes(entry.Version);
                writer.Write((byte)id.Length);
                writer.Write(id);
                writer.Write((byte)version.Length);
                writer.Write(version);
            }
            writer.Flush();
            if (stream.Length > MaxEncodedBytes)
                throw new ArgumentException($"Client mod inventory exceeds {MaxEncodedBytes} encoded bytes.");
            return stream.ToArray();
        }
    }

    public static ClientModInventory Decode(byte[] bytes)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length < 1 || bytes.Length > MaxEncodedBytes)
            throw new InvalidDataException("Client mod inventory has an invalid encoded length.");

        try
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                var count = reader.ReadByte();
                if (count > MaxEntries)
                    throw new InvalidDataException("Client mod inventory has too many entries.");

                var entries = new List<ClientModEntry>(count);
                string precedingId = null;
                for (var i = 0; i < count; i++)
                {
                    var idLength = reader.ReadByte();
                    if (idLength == 0 || idLength > MaxIdBytes)
                        throw new InvalidDataException("Client mod ID has an invalid length.");
                    var idBytes = reader.ReadBytes(idLength);
                    if (idBytes.Length != idLength)
                        throw new InvalidDataException("Client mod ID is truncated.");

                    var versionLength = reader.ReadByte();
                    if (versionLength == 0 || versionLength > MaxVersionBytes)
                        throw new InvalidDataException("Client mod version has an invalid length.");
                    var versionBytes = reader.ReadBytes(versionLength);
                    if (versionBytes.Length != versionLength)
                        throw new InvalidDataException("Client mod version is truncated.");

                    var id = StrictUtf8.GetString(idBytes);
                    if (precedingId != null && StringComparer.Ordinal.Compare(precedingId, id) >= 0)
                        throw new InvalidDataException("Client mod IDs are not in canonical order or are duplicated.");
                    entries.Add(new ClientModEntry(id, StrictUtf8.GetString(versionBytes)));
                    precedingId = id;
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Client mod inventory has trailing data.");
                return new ClientModInventory(entries);
            }
        }
        catch (Exception error) when (error is EndOfStreamException || error is DecoderFallbackException || error is ArgumentException)
        {
            throw new InvalidDataException("Client mod inventory is malformed.", error);
        }
    }

    internal static string ValidateText(string text, int maximumBytes, string parameterName)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Value must not be empty.", parameterName);
        foreach (var character in text)
        {
            if (char.IsControl(character))
                throw new ArgumentException("Value must not contain control characters.", parameterName);
        }
        int length;
        try
        {
            length = StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("Value must be valid UTF-8 text.", parameterName, error);
        }
        if (length > maximumBytes)
            throw new ArgumentException($"Value cannot exceed {maximumBytes} UTF-8 bytes.", parameterName);
        return text;
    }
}
