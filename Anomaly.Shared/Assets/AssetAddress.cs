using System;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Assets;

/// <summary>A canonical catalog address, independent of any filesystem path.</summary>
public readonly struct AssetAddress : IEquatable<AssetAddress>
{
    public const int MaxLength = 255;
    private readonly string _value;
    public string Value => _value ?? string.Empty;
    public bool IsValid => !string.IsNullOrEmpty(_value);
    public bool IsGameReplacement => Value.StartsWith("game/", StringComparison.Ordinal);
    public bool IsSkin => Value == "game/skin/logo" || Value == "game/skin/background";

    private AssetAddress(string value) => _value = value;

    public static AssetAddress Parse(string value) => TryParse(value, out AssetAddress address)
        ? address : throw new ArgumentException("Asset addresses must be canonical lowercase game/ or mods/ catalog addresses.", nameof(value));

    public static bool TryParse(string value, out AssetAddress address)
    {
        address = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return false;
        foreach (char character in value)
            if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9') &&
                character != '/' && character != '_' && character != '-' && character != '.')
                return false;
        string[] parts = value.Split('/');
        if (parts.Length < 3) return false;
        foreach (string part in parts)
            if (part.Length == 0 || part == "." || part == "..") return false;
        if (parts[0] == "mods")
        {
            if (parts[1].Length > 64 || !ModNamespace.IsValid(parts[1])) return false;
        }
        else if (parts[0] == "game")
        {
            if (parts.Length != 3 || (parts[1] != "cassie" &&
                (parts[1] != "skin" || (parts[2] != "logo" && parts[2] != "background")))) return false;
            if (parts[1] == "cassie" && !StockAssetProfile.TryGetCassie(value, out _)) return false;
        }
        else return false;
        address = new AssetAddress(value);
        return true;
    }

    public bool Equals(AssetAddress other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is AssetAddress other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
    public static bool operator ==(AssetAddress left, AssetAddress right) => left.Equals(right);
    public static bool operator !=(AssetAddress left, AssetAddress right) => !left.Equals(right);
}
