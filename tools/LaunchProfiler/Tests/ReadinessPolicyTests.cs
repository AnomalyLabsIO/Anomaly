using Anomaly.LaunchProfiler;
using Xunit;

public sealed class ReadinessPolicyTests
{
    [Theory]
    [InlineData(false, false, 0f, 0f, false)]
    [InlineData(false, true, 0f, 0f, false)]
    [InlineData(true, false, 0f, 0f, false)]
    [InlineData(true, true, 0f, 0f, true)]
    [InlineData(true, true, 4.999f, 5f, false)]
    [InlineData(true, true, 5f, 5f, true)]
    [InlineData(true, true, 6f, 5f, true)]
    [InlineData(true, true, -1f, 0f, false)]
    [InlineData(true, true, 0f, -1f, false)]
    [InlineData(true, true, float.NaN, 0f, false)]
    [InlineData(true, true, 0f, float.NaN, false)]
    [InlineData(true, true, float.PositiveInfinity, 5f, false)]
    [InlineData(true, true, 0f, float.NegativeInfinity, false)]
    public void AcknowledgmentRequiresCompletedStockInitializationAndValidElapsedTime(
        bool startObserved, bool sceneInitialized, float current, float intended, bool expected)
        => Assert.Equal(expected, ReadinessPolicy.CanAcknowledgeWarning(startObserved, sceneInitialized, current, intended));
}
