using System;
using UnityEngine;

namespace Anomaly.Client.API.Input;

/// <summary>
/// Modifier keys that can be combined with a primary <see cref="UnityEngine.KeyCode"/>
/// to form a compound keybind like Shift+F.
/// </summary>
[Flags]
public enum InputModifiers : byte
{
    None  = 0,
    Ctrl  = 1 << 0,
    Shift = 1 << 1,
    Alt   = 1 << 2,
}

/// <summary>
/// A concrete input binding: a primary key plus a set of required modifiers.
/// </summary>
/// <remarks>
/// The binding is considered "pressed" iff the primary key is held AND every
/// required modifier is held. Extra held modifiers (e.g. holding Ctrl when the
/// binding only requires Shift) fail the match, so Shift+F and Ctrl+Shift+F are
/// distinct bindings.
/// </remarks>
public readonly struct InputKey : IEquatable<InputKey>
{
    public KeyCode Key { get; }
    public InputModifiers Modifiers { get; }

    public InputKey(KeyCode key, InputModifiers modifiers = InputModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public bool HasModifier(InputModifiers m) => (Modifiers & m) == m;

    public override string ToString()
    {
        if (Modifiers == InputModifiers.None) return Key.ToString();
        string mods = string.Empty;
        if (HasModifier(InputModifiers.Ctrl))  mods += "Ctrl+";
        if (HasModifier(InputModifiers.Shift)) mods += "Shift+";
        if (HasModifier(InputModifiers.Alt))   mods += "Alt+";
        return mods + Key;
    }

    /// <summary>
    /// Parses a string like "Shift+F" or "Ctrl+Alt+Space" into an <see cref="InputKey"/>.
    /// Returns false if the primary segment is not a valid <see cref="KeyCode"/>.
    /// </summary>
    public static bool TryParse(string s, out InputKey result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        InputModifiers mods = InputModifiers.None;
        string[] parts = s.Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= InputModifiers.Ctrl; break;
                case "shift":
                    mods |= InputModifiers.Shift; break;
                case "alt":
                    mods |= InputModifiers.Alt; break;
                default:
                    return false;
            }
        }

        string keyPart = parts[parts.Length - 1].Trim();
        if (!System.Enum.TryParse<KeyCode>(keyPart, ignoreCase: true, out var code))
            return false;

        result = new InputKey(code, mods);
        return true;
    }

    public bool Equals(InputKey other) => Key == other.Key && Modifiers == other.Modifiers;
    public override bool Equals(object obj) => obj is InputKey other && Equals(other);
    public override int GetHashCode() => ((int)Key * 397) ^ (int)Modifiers;

    public static bool operator ==(InputKey a, InputKey b) => a.Equals(b);
    public static bool operator !=(InputKey a, InputKey b) => !a.Equals(b);
}
