using System;
using System.Collections.Generic;
using System.Globalization;

namespace Anomaly.Client.Api.UI.ModSettings.Internal;

/// <summary>Pure value rules shared by settings registration and runtime controls.</summary>
internal static class ModSettingValue
{
    internal static bool IsNumeric(Type type)
        => type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    internal static string Format(object value)
        => value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? string.Empty;

    internal static bool TryParse(Type type, string text, out object value)
    {
        value = null;
        if (type == typeof(string)) { value = text ?? string.Empty; return true; }
        if (!IsNumeric(type)) return false;
        try
        {
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            if (value is float single && (float.IsNaN(single) || float.IsInfinity(single))
                || value is double number && (double.IsNaN(number) || double.IsInfinity(number)))
            {
                value = null;
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException)
        {
            value = null;
            return false;
        }
    }

    internal static object ChoiceValue(Type type, IReadOnlyList<string> choices, int index)
    {
        if (index < 0 || index >= choices.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (type == typeof(string)) return choices[index];
        if (type.IsEnum) return System.Enum.Parse(type, choices[index], ignoreCase: false);
        if (IsNumeric(type)) return Convert.ChangeType(index, type, CultureInfo.InvariantCulture);
        throw new ArgumentException("Choices require a string, numeric, or enum setting.", nameof(type));
    }

    internal static int ChoiceIndex(Type type, IReadOnlyList<string> choices, object value)
    {
        if (value == null) return -1;
        for (int index = 0; index < choices.Count; index++)
            if (Equals(value, ChoiceValue(type, choices, index))) return index;
        return -1;
    }

    internal static void ValidateMetadata(Type type, ModSettingMetadata metadata)
    {
        if (metadata == null) return;
        if (metadata.MinValue.HasValue || metadata.MaxValue.HasValue)
        {
            if (!IsNumeric(type) || !metadata.MinValue.HasValue || !metadata.MaxValue.HasValue
                || !double.IsFinite(metadata.MinValue.Value) || !double.IsFinite(metadata.MaxValue.Value)
                || metadata.MaxValue.Value <= metadata.MinValue.Value
                || metadata.MinValue.Value < -float.MaxValue || metadata.MaxValue.Value > float.MaxValue)
                throw new ArgumentException("A slider requires two finite numeric bounds with maximum greater than minimum.", nameof(metadata));
            if (metadata.Choices != null)
                throw new ArgumentException("Choose either dropdown choices or slider bounds for a setting.", nameof(metadata));
        }
        if (metadata.Choices == null) return;
        if (metadata.Choices.Count == 0 || (!IsNumeric(type) && type != typeof(string) && !type.IsEnum))
            throw new ArgumentException("Choices must be nonempty and belong to a string, numeric, or enum setting.", nameof(metadata));
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string choice in metadata.Choices)
        {
            if (string.IsNullOrWhiteSpace(choice) || !unique.Add(choice))
                throw new ArgumentException("Choice labels must be nonempty and unique.", nameof(metadata));
            if (type.IsEnum && Array.IndexOf(System.Enum.GetNames(type), choice) < 0)
                throw new ArgumentException("Enum choices must use declared enum names.", nameof(metadata));
        }
        // Reject an index range the entry's numeric type cannot store.
        ChoiceValue(type, metadata.Choices, metadata.Choices.Count - 1);
    }
}
