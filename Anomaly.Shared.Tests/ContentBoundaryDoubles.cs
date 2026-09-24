using System.Linq;
using Anomaly.Shared.Content;

// ContentCatalogClient and FileTransferClient are linked production code.
// Replace persistence/UI/Unity materialization boundaries in this isolated host.
namespace Anomaly.Client.Content
{
    internal static class ContentConsentStore
    {
        internal static readonly HashSet<string> Approved = new();
        internal static void Init() { }
        private static string Key(string server, byte[] catalog, byte[] manifest) =>
            server + ":" + Convert.ToBase64String(catalog) + ":" + Convert.ToBase64String(manifest);
        internal static bool IsApproved(string server, byte[] catalog, byte[] manifest) =>
            Approved.Contains(Key(server, catalog, manifest));
        internal static bool TryRemember(string server, byte[] catalog, byte[] manifest) =>
            Approved.Add(Key(server, catalog, manifest)) || IsApproved(server, catalog, manifest);
    }

    internal static class ClientContentRegistrar
    {
        internal static int Activations;
        internal static bool Active;
        internal static bool ThrowOnReset;
        internal static bool TryActivate(ContentCatalog catalog, out List<string> failedKeys, out string error)
        {
            failedKeys = new();
            error = Active ? "old content was not reset" : string.Empty;
            if (Active) return false;
            Active = true;
            Activations++;
            return true;
        }
        internal static void Reset()
        {
            Active = false;
            if (ThrowOnReset) throw new InvalidOperationException("injected template cleanup failure");
        }
    }
}

namespace Anomaly.Client.Auth
{
    internal static class AuthHandler { internal static string CurrentServerFingerprint = "test-fingerprint"; }
    internal static class ServerTrustStore { internal static string FormatFingerprint(string value) => value; }
}

namespace Anomaly.Client.Components
{
    internal static class PasswordPrompt
    {
        internal static int Shown;
        internal static Action Approve;
        internal static Action Decline;
        internal static void Hide(string owner) { Approve = null; Decline = null; }
        internal static void ShowConfirmation(string owner, string title, string detail, string yes, string no,
            Action approve, Action decline)
        {
            Shown++;
            Approve = approve;
            Decline = decline;
        }
    }
    internal static class ConnectionInfoOverlay
    {
        internal static void ShowFailure(string reason, string advice, string code) { }
    }
}

namespace Anomaly.Client.Api.Events.Args { internal sealed class ClientDisconnectedEventArgs { } }

namespace Anomaly.Client.Api.Modding
{
    internal enum FeatureHealthStatus { Healthy, Degraded, Disabled, Failed }
    internal static class FeatureHealth { internal static void Report(string owner, string feature, FeatureHealthStatus status, string detail) { } }
}

namespace Anomaly.Client.Assets { internal static class OverrideRegistry { internal static void Clear() { } } }
namespace Anomaly.Client.Internal.Audio { internal static class AnnouncerVocabularyReceiver { internal static void Clear() { } } }

namespace Anomaly.Client.Assets
{
    // Native bundle validation/loading has its own fixture suite; this boundary exercises the coordinator.
    internal static class AssetBundleManager
    {
        internal static bool Active;
        internal static string ActivationError;
        internal static bool ThrowOnRevokeRemote, NativeLeaseOwned;
        internal static int ClearRemoteCalls;
        internal static void ActivateRemote(ContentCatalog catalog, Action<bool, string> completed)
        {
            Active = ActivationError == null;
            if (Active) NativeLeaseOwned = true;
            completed(Active, ActivationError);
        }
        internal static void RevokeRemote()
        {
            Active = false;
            if (ThrowOnRevokeRemote) throw new InvalidOperationException("injected native binding refresh failure");
        }
        internal static void ClearRemote() { Active = false; NativeLeaseOwned = false; ClearRemoteCalls++; }
        internal static void StopRemoteAudio() { }
    }
    internal static class DecorativeVisualClient { internal static void Clear() { } }
}
namespace Anomaly.Client.Patches.Audio { internal static class CassieLineSourceSchedulePatch { internal static void RestoreAll() { } } }
