using System.Globalization;
using Anomaly.Client.Api.UI.ModSettings;
using Anomaly.Client.Api.UI.ModSettings.Internal;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class ModSettingsValueTests
{
    private enum Quality { Low = 10, Medium = 20, High = 30 }
    private static readonly string[] Labels = { "Low", "Medium", "High" };

    [Theory]
    [InlineData(typeof(short))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    public void NumericChoicesStoreIndicesRegardlessOfTheirDisplayLabels(Type type)
    {
        ModSettingValue.ValidateMetadata(type, new ModSettingMetadata { Choices = Labels });
        object expected = Convert.ChangeType(2, type, CultureInfo.InvariantCulture);
        Assert.Equal(expected, ModSettingValue.ChoiceValue(type, Labels, 2));
        Assert.Equal(2, ModSettingValue.ChoiceIndex(type, Labels, expected));
        Assert.Equal(-1, ModSettingValue.ChoiceIndex(type, Labels, Convert.ChangeType(5, type)));
    }

    [Fact]
    public void StringAndEnumChoicesUseLabelsAndDeclaredValues()
    {
        Assert.Equal("High", ModSettingValue.ChoiceValue(typeof(string), Labels, 2));
        Assert.Equal(Quality.High, ModSettingValue.ChoiceValue(typeof(Quality), Labels, 2));
        Assert.Equal(2, ModSettingValue.ChoiceIndex(typeof(Quality), Labels, Quality.High));
        Assert.Equal(-1, ModSettingValue.ChoiceIndex(typeof(string), Labels, "Removed option"));
        Assert.Equal(-1, ModSettingValue.ChoiceIndex(typeof(Quality), Labels, (Quality)99));
        Assert.Equal(-1, ModSettingValue.ChoiceIndex(typeof(double), Labels, 1.5d));
    }

    [Fact]
    public void MetadataRejectsInvalidChoicesAndRangesAtRegistrationBoundary()
    {
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(bool), new() { Choices = Labels }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { Choices = Array.Empty<string>() }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { Choices = new[] { "Low", "Low" } }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { Choices = new[] { " " } }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(Quality), new() { Choices = new[] { "20" } }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { MinValue = 0 }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { MinValue = 1, MaxValue = 1 }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(float), new() { MinValue = 0, MaxValue = double.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(string), new() { MinValue = 0, MaxValue = 2 }));
        Assert.Throws<ArgumentException>(() => ModSettingValue.ValidateMetadata(typeof(int), new() { MinValue = 0, MaxValue = 2, Choices = Labels }));
    }

    [Fact]
    public void InputParsingIsInvariantFiniteAndPreservesExactLongValues()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(ModSettingValue.TryParse(typeof(double), "1.5", out object fraction));
            Assert.Equal(1.5d, fraction);
            Assert.Equal("1.5", ModSettingValue.Format(fraction));
            Assert.True(ModSettingValue.TryParse(typeof(long), "9007199254740993", out object integer));
            Assert.Equal(9007199254740993L, integer);
            Assert.False(ModSettingValue.TryParse(typeof(short), "32768", out _));
            Assert.False(ModSettingValue.TryParse(typeof(double), "NaN", out _));
            Assert.False(ModSettingValue.TryParse(typeof(float), "1e100", out _));
            Assert.False(ModSettingValue.TryParse(typeof(int), "1.5", out _));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void RegisteredMetadataIsASnapshotUntilReregistered()
    {
        string category = "snapshot-" + Guid.NewGuid().ToString("N");
        var choices = new List<string> { "First", "Second" };
        var metadata = new ModSettingMetadata { Description = "Original", Choices = choices };
        try
        {
            ModSettingsRegistry.SetEntry(category, "choice", metadata);
            metadata.Description = "Changed";
            choices[0] = "Mutated";
            var snapshot = ModSettingsRegistry.GetEntry(category, "choice");
            Assert.Equal("Original", snapshot.Description);
            Assert.Equal("First", snapshot.Choices[0]);
        }
        finally { ModSettingsRegistry.SetEntry(category, "choice", null); }
    }

    [Fact]
    public void FailedPersistenceStaysDirtyUntilARetrySucceeds()
    {
        var saves = new PendingSettingsSaves<string>();
        saves.MarkDirty("audio");
        saves.MarkDirty("video");
        saves.Flush(category => category == "video");
        Assert.Equal(1, saves.Count);
        var retried = new List<string>();
        saves.Flush(category => { retried.Add(category); return true; });
        Assert.Equal(new[] { "audio" }, retried);
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void AnEditDuringSavingRemainsDirtyForTheNextFlush()
    {
        var saves = new PendingSettingsSaves<string>();
        saves.MarkDirty("audio");
        saves.Flush(category => { saves.MarkDirty(category); return true; });
        Assert.Equal(1, saves.Count);
        saves.Flush(_ => true);
        Assert.Equal(0, saves.Count);
    }
}
