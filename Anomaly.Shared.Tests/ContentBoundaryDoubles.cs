using System.Linq;
using Anomaly.Shared.Content;

namespace Anomaly.Client.Api.Scripting
{
    // Tests may exercise future qualified execution without opening the real policy gate.
    internal static class ScriptingPolicy
    {
        internal static bool RemoteExecutionEnabled = true;
        internal const string RemoteExecutionDisabledReason = "Remote script memory containment is not qualified.";
    }
}

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

    internal static class ScriptPackManager
    {
        internal static string PreparationError;
        internal static int Preparations;
        internal static void PrepareRemote(ContentCatalog catalog)
        {
            Preparations++;
            if (PreparationError != null) throw new InvalidDataException(PreparationError);
        }
        internal static void Reset() { }
    }
}

namespace Anomaly.Client.Auth
{
    internal static class AuthHandler
    {
        internal static string CurrentServerFingerprint = "test-fingerprint";
        internal static bool UsingAnomalyAuth;
    }
    internal static class ServerTrustStore { internal static string FormatFingerprint(string value) => value; }
}

namespace Anomaly.Client.Components
{
    internal static class PasswordPrompt
    {
        internal static int Shown;
        internal static Action Approve;
        internal static Action Decline;
        internal static string Owner, Title, Detail;
        internal static readonly List<string> Owners = new();
        internal static void Hide(string owner)
        {
            if (Owner != owner) return;
            Owner = Title = Detail = null;
            Approve = null; Decline = null;
        }
        internal static void ShowConfirmation(string owner, string title, string detail, string yes, string no,
            Action approve, Action decline)
        {
            Shown++;
            Owner = owner; Title = title; Detail = detail;
            Owners.Add(owner);
            Approve = approve;
            Decline = decline;
        }
        internal static void ResetForTests()
        {
            Shown = 0; Owner = Title = Detail = null;
            Approve = Decline = null; Owners.Clear();
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
        internal static Action<bool, string> LastActivationCallback;
        internal static void ActivateRemote(ContentCatalog catalog, Action<bool, string> completed)
        {
            LastActivationCallback = completed;
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

namespace Il2Cpp { internal sealed class CustomNetworkManager { } }
namespace Anomaly.Client.Internal.Bootstrap
{
    internal static class ScriptingBootstrap
    {
        internal static int Revocations;
        internal static bool ScriptsReady = true;
        internal static bool IsConnectionPrepared => ScriptsReady;
        internal static bool ThrowOnRevoke;
        internal static bool ThrowOnPrepare;
        internal static int PrepareCalls, PreparedScriptCount;
        internal static readonly List<bool> PrepareApprovals = new();
        internal static Action<ContentCatalog, bool> ObserveExecute;
        internal static void RevokeAll()
        {
            Revocations++;
            if (ThrowOnRevoke) throw new InvalidOperationException("injected script cleanup failure");
        }
        internal static bool PrepareConnection(ContentCatalog catalog, bool approved, ulong activationId = 0)
        {
            PrepareCalls++;
            PrepareApprovals.Add(approved);
            if (ThrowOnPrepare) throw new InvalidOperationException("injected VM preparation failure");
            int scripts = catalog.ScriptPacks.Sum(pack => pack.Sources.Count);
            if (!ScriptsReady || (scripts != 0 && !approved)) return false;
            PreparedScriptCount += scripts;
            ObserveExecute?.Invoke(catalog, approved);
            return true;
        }
        internal static void ResetForTests()
        {
            Revocations = PrepareCalls = PreparedScriptCount = 0;
            ScriptsReady = true;
            ThrowOnRevoke = ThrowOnPrepare = false;
            ObserveExecute = null;
            PrepareApprovals.Clear();
        }
    }
    internal static class ClientRuntime
    {
        internal static int ReadyPublications;
        internal static Il2Cpp.CustomNetworkManager LastReadyManager;
        internal static void HandleClientReady(Il2Cpp.CustomNetworkManager manager)
        {
            ReadyPublications++;
            LastReadyManager = manager;
        }
    }
}
namespace Anomaly.Client.Internal.Hosting
{
    // The actual reciprocal native connection proof is covered by LocalScriptHostGate tests.
    internal static class LocalHostingRuntime { internal static bool IsVerifiedScriptHost; }
}
namespace Anomaly.Client.Internal.Map
{
    internal static class FacilityRecipeClient { internal static bool IsReady; }
}
