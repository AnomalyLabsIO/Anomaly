using System;

namespace Anomaly.Shared.Capabilities
{
    /// <summary>
    /// Versioned, namespaced capability advertised by an Anomaly client.
    /// Major versions are intentionally incompatible; minor and patch versions
    /// are backward-compatible within a major version.
    /// </summary>
    public sealed class CapabilityDescriptor : IEquatable<CapabilityDescriptor>
    {
        public const int MaxIdLength = 64;

        public string Id { get; }
        public ushort Major { get; }
        public ushort Minor { get; }
        public ushort Patch { get; }

        public CapabilityDescriptor(string id, ushort major, ushort minor, ushort patch = 0)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Capability id is required.", nameof(id));
            if (id.Length > MaxIdLength)
                throw new ArgumentOutOfRangeException(nameof(id), $"Capability id cannot exceed {MaxIdLength} characters.");

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool valid = c >= 'a' && c <= 'z' ||
                             c >= '0' && c <= '9' ||
                             c == '.' || c == '-' || c == '_' || c == ':';
                if (!valid)
                    throw new ArgumentException(
                        "Capability ids may contain lowercase ASCII letters, digits, '.', '-', '_', and ':'.",
                        nameof(id));
            }

            Id = id;
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public bool Satisfies(CapabilityDescriptor requirement)
        {
            if (requirement == null) return false;
            if (!string.Equals(Id, requirement.Id, StringComparison.Ordinal)) return false;
            if (Major != requirement.Major) return false;
            if (Minor != requirement.Minor) return Minor > requirement.Minor;
            return Patch >= requirement.Patch;
        }

        public bool Equals(CapabilityDescriptor other)
        {
            return other != null &&
                   string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Major == other.Major &&
                   Minor == other.Minor &&
                   Patch == other.Patch;
        }

        public override bool Equals(object obj) => Equals(obj as CapabilityDescriptor);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                return hash;
            }
        }

        public override string ToString() => $"{Id}@{Major}.{Minor}.{Patch}";
    }
}
