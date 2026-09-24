namespace Anomaly.LaunchProfiler;

internal static class ReadinessPolicy
{
    // Zero is a valid repeat-view duration only after native Start has selected it.
    internal static bool CanAcknowledgeWarning(bool startObserved, bool sceneInitialized, float current, float intended)
        => startObserved && sceneInitialized && float.IsFinite(current) && float.IsFinite(intended)
            && current >= 0 && intended >= 0 && current >= intended;
}
