namespace Anomaly.Shared.Assets;

/// <summary>Protocol 6 transfers complete containers and their authenticated indexes.</summary>
public enum FileContentKind : byte
{
    Bundle = 1,
    PackIndex = 2,
}
