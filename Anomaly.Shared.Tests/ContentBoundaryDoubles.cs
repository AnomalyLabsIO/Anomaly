using System.Linq;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Shared.Content;

namespace Anomaly.Client.Api.Scripting
{
    // Qualification itself is exercised by the real worker suite; admission sees only its published result.
    internal static class ScriptingPolicy
    {
        internal static bool RemoteExecutionEnabled = true;
        internal const string RemoteExecutionDisabledReason = "Remote worker isolation is not qualified.";
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
        internal static string FailureReason, FailureCode;
        internal static void ShowFailure(string reason, string advice, string code) { FailureReason = reason; FailureCode = code; }
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
        internal static readonly Dictionary<string, long> BudgetLocalCopies = new(StringComparer.Ordinal);
        internal static readonly List<BundleCopyBudget.DecodedDemand> BudgetLocalDecoded = new();
        internal static string BudgetKey(CatalogPack pack) => FileCache.ToHex(pack.BundleHash) + ":" + FileCache.ToHex(pack.IndexHash);
        internal static void PreflightRemoteBudget(ContentCatalog catalog)
        {
            var offered = new List<KeyValuePair<string, long>>();
            foreach (var pack in catalog.Packs) offered.Add(new KeyValuePair<string, long>(BudgetKey(pack), pack.BundleSize));
            BundleCopyBudget.Validate(BudgetLocalCopies, offered);
            BundleCopyBudget.ValidateDecoded(BudgetLocalDecoded, catalog.Packs.Select(pack =>
                new BundleCopyBudget.DecodedDemand(FileCache.ToHex(pack.BundleHash), BudgetKey(pack), pack.DecodedBytes)));
        }
        internal static int PrewarmOffers;
        internal static readonly List<string> PrewarmedPacks = new();
        internal static void BeginRemotePrewarm(ContentCatalog catalog)
        { PreflightRemoteBudget(catalog); PrewarmOffers++; PrewarmedPacks.Clear(); }
        internal static void PrewarmRemotePack(string packId) { PrewarmedPacks.Add(packId); }
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
            PrewarmedPacks.Clear();
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
        internal static bool IsRemoteQualificationPending;
        internal static bool HoldPreparation, PreparationPending;
        internal static Action<bool, string> LastPreparationCompletion;
        private static long _preparationGeneration;
        internal static bool IsConnectionPrepared => ScriptsReady && !PreparationPending;
        internal static bool ThrowOnRevoke;
        internal static bool ThrowOnPrepare;
        internal static int PrepareCalls, PreparedScriptCount;
        internal static readonly List<bool> PrepareApprovals = new();
        internal static Action<ContentCatalog, bool> ObserveExecute;
        internal static void RevokeAll()
        {
            Revocations++;
            _preparationGeneration++;
            PreparationPending = false;
            PreparedScriptCount = 0;
            if (ThrowOnRevoke) throw new InvalidOperationException("injected script cleanup failure");
        }
        internal static void BeginPrepareConnection(ContentCatalog catalog, bool approved, ulong activationId, Action<bool, string> completed)
        {
            PrepareCalls++;
            PrepareApprovals.Add(approved);
            if (ThrowOnPrepare) throw new InvalidOperationException("injected worker preparation failure");
            int scripts = catalog.ScriptPacks.Sum(pack => pack.Sources.Count);
            if (!ScriptsReady || (scripts != 0 && !approved))
            {
                completed(false, "injected worker initialization failure");
                return;
            }
            ObserveExecute?.Invoke(catalog, approved);
            long generation = ++_preparationGeneration;
            bool finished = false;
            LastPreparationCompletion = (success, error) =>
            {
                if (!finished && generation == _preparationGeneration)
                {
                    finished = true;
                    PreparationPending = false;
                    ScriptsReady = success;
                    if (success) PreparedScriptCount += scripts;
                }
                // Deliberately allow replay/stale delivery to exercise coordinator guards.
                completed(success, error);
            };
            if (scripts != 0 && HoldPreparation) PreparationPending = true;
            else LastPreparationCompletion(true, string.Empty);
        }
        internal static void ResetForTests()
        {
            Revocations = PrepareCalls = PreparedScriptCount = 0;
            ScriptsReady = true;
            ThrowOnRevoke = ThrowOnPrepare = false;
            IsRemoteQualificationPending = HoldPreparation = PreparationPending = false;
            LastPreparationCompletion = null;
            _preparationGeneration++;
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
