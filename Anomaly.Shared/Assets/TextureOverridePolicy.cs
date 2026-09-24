using System;
using System.Text;

namespace Anomaly.Shared.Assets;

/// <summary>Shared bounds for authoritative texture state and its wire representation.</summary>
public static class TextureOverridePolicy
{
    public const int MaximumOverrides = 1024;
    public const int MaximumOverridesPerObject = 32;
    public const int MaximumComponentPathBytes = 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>Validates an existing network object and a renderer/material address.</summary>
    public static void ValidateTarget(uint netId, string componentPath, int materialIndex)
    {
        if (netId == 0) throw new ArgumentOutOfRangeException(nameof(netId));
        if (materialIndex < 0 || materialIndex > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(materialIndex));
        if (Utf8.GetByteCount(componentPath ?? string.Empty) > MaximumComponentPathBytes)
            throw new ArgumentException("Component path exceeds the UTF-8 wire limit.", nameof(componentPath));
    }

    /// <summary>Existing keys remain replaceable when a registry reaches its bound.</summary>
    public static bool CanStore(bool replacing, int totalCount, int objectCount) =>
        replacing || (totalCount < MaximumOverrides && objectCount < MaximumOverridesPerObject);
}
