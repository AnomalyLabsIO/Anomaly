using Anomaly.Client.Api.Modding;

namespace Anomaly.Client.Diagnostics.News;

/// <summary>Exact compatibility notices excluded from the issue list, but retained in loader logs.</summary>
internal static class NewsDiagnosticPolicy
{
    internal const string OptionalByteSendFeature = "encrypted-channel-byte-send";
    internal const string OptionalByteSendTarget =
        "EncryptedChannelManager.TrySendMessageToServer(byte[], EncryptedChannel)";

    internal static bool IsExpectedWarning(string section, string message)
    {
        if (section == "Il2CppInterop")
        {
            // The loader successfully selects mono_class_instance_size as its substitute.
            if (message == "Class::Init signatures have been exhausted, using a substitute!") return true;
            // This value-type constructor retains managed wrapper handlers. Native role
            // receipts are also observed by the separate ReadRoleSyncInfo patch.
            if (message == "Failed to init IL2CPP patch backend for void Il2CppPlayerRoles.RoleSyncInfo::" +
                ".ctor(Il2CppMirror.NetworkReader reader), using normal patch handlers: " +
                "Derived classes must provide an implementation.") return true;
        }

        // Recognize the old optional probe notices in earlier logs as well. Other
        // missing targets and backend failures remain actionable.
        if (string.IsNullOrEmpty(section) && message ==
            "AccessTools.Method: Could not find method for type Il2Cpp.EncryptedChannelManager and name " +
            "TrySendMessageToServer and parameters (byte[], Il2Cpp.EncryptedChannelManager+EncryptedChannel)") return true;
        return (section == "Anomaly" || section == "anomaly.client:Anomaly") && message ==
            "Harmony degraded: Optional " + OptionalByteSendTarget +
            " was not present; byte-array encrypted sends are disabled while string sends remain active.";
    }

    internal static bool IsActionableFeature(FeatureHealthRecord feature) =>
        feature.Status != FeatureHealthStatus.Healthy &&
        !(feature.OwnerId == "anomaly" && feature.FeatureId == OptionalByteSendFeature &&
          feature.Status == FeatureHealthStatus.Disabled && feature.ExpectedTarget == OptionalByteSendTarget &&
          string.IsNullOrEmpty(feature.ResolvedTarget));
}
