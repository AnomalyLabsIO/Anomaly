using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Auth;
using Il2CppMirror.LiteNetLib4Mirror;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private NativeTransferCancellation _nativeCancellation;
    private bool IsIntentionalNativeTransferCancellation => _nativeCancellation is { Attempt: 1, CancelRequested: true };

    private void InitializeNativeTransferCancellation(string server)
    {
        string threshold = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_NATIVE_CANCEL_AFTER_BYTES");
        if (string.IsNullOrEmpty(threshold)) return;
        long bytes = long.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture);
        Require(bytes >= 16 * 1024 && bytes < 256L * 1024 * 1024 && !_transferExpectBudgetRejection && !_transferExpectAdmissionRejection,
            "Native cancellation is one bounded positive-transfer lifecycle scenario");
        Require(Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_COLD") == "0",
            "Native cancellation explicitly permits honest partial-cache reuse on rejoin; the first attempt is independently asserted fully cold");
        string gate = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_NATIVE_RECONNECT_GATE");
        if (!string.IsNullOrEmpty(gate))
        {
            gate = Path.GetFullPath(gate);
            string output = Path.GetFullPath(_output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Require(gate.StartsWith(output, StringComparison.OrdinalIgnoreCase) && !File.Exists(gate),
                "Native reconnect gate starts closed inside this fresh evidence directory");
        }
        _nativeCancellation = new NativeTransferCancellation { Threshold = bytes, Server = server, Gate = gate };
    }

    private bool PollNativeTransferCancellation(long now, string state)
    {
        var run = _nativeCancellation;
        if (run == null) return false;
        if (run.Attempt == 2)
        {
            // The first exact content approval may legitimately be remembered on rejoin.
            // Observe that approval instead of fabricating another user consent click.
            if (!_transferPinsChecked && (state == "Preparing" || state == "AwaitingAdmission" || state == "Queued" || state == "Receiving"))
                VerifyNetworkTransferPins();
            if (_transferConsentAt == 0 && (state == "Preparing" || state == "AwaitingAdmission" || state == "Queued" || state == "Receiving"))
            {
                _transferConsentAt = now;
                run.RememberedApprovalObserved = true;
            }
            return false;
        }
        if (!run.CancelRequested && _transferReceived >= run.Threshold)
        {
            Require(_transferPinsChecked && _transferManifest != null && _transferConsentAt != 0 && !_transferFinished &&
                !ApiClient.IsClientReady && !Il2CppMirror.NetworkClient.ready && !_transferExecutionApproved,
                "Native cancellation interrupts an approved in-progress acquisition before scripts or Ready");
            Require(_transferRequested == _transferManifest.Files.Sum(file => (long)file.Size) && run.Threshold < _transferRequested,
                "The first native cancellation attempt requested every pinned cold manifest byte");
            run.OldSession = _transferManifest.SessionId;
            run.Identity = NativeTransferIdentityFingerprint();
            run.OldCrypto = NativeTransferCryptoFingerprint(true);
            run.CancelAt = now;
            run.FirstConnectAt = _transferConnectAt;
            run.CancelledAttempt = new {
                sessionId = run.OldSession, identityPublicFingerprint = run.Identity, cryptoSessionFingerprint = run.OldCrypto,
                connectTimestamp = _transferConnectAt, consentTimestamp = _transferConsentAt, cancelTimestamp = now,
                thresholdBytes = run.Threshold, requestedBytes = _transferRequested, consumedBytes = _transferReceived,
                consumedFrames = _transferPeakConsumedFrames, finished = _transferFinished,
                ready = ApiClient.IsClientReady, nativeReady = Il2CppMirror.NetworkClient.ready,
                firstAttemptFullyCold = true, scope = "Normal native Disconnect; no profile import, key access or cache deletion." };
            File.WriteAllText(Path.Combine(_output, "native-cancellation-request.json"), JsonSerializer.Serialize(run.CancelledAttempt));
            run.CancelRequested = true;
            ApiClient.Disconnect();
            return true;
        }
        if (!run.CancelRequested) return false;
        if (run.ResetAt == 0)
        {
            if (TransferSeconds(run.CancelAt, now) > 10)
                throw new TimeoutException("Intentional native disconnect did not reset its file/crypto owners within ten seconds.");
            if (ApiClient.IsConnected || ApiClient.IsClientReady || Il2CppMirror.NetworkClient.ready || state != "Idle") return true;
            if (TransferRequiredStatic(TransferType, "_worker") != null ||
                Convert.ToUInt64(TransferRequiredStatic(TransferType, "_sessionId")) != 0 ||
                Convert.ToInt64(TransferRequiredStatic(TransferType, "_totalBytes")) != 0 ||
                Convert.ToInt64(TransferRequiredStatic(TransferType, "_receivedBytes")) != 0 ||
                TransferRequiredStatic(TransferType, "_manifestSnapshot") != null ||
                TransferRequiredStatic(TransferType, "_receiveWindow") != null ||
                TransferRequiredStatic(TransferType, "_requested") is not ICollection<string> requested || requested.Count != 0 ||
                TransferRequiredStatic(TransferType, "_buffers") is not ICollection buffers || buffers.Count != 0 ||
                NativeTransferCryptoFingerprint(false) != null) return true;
            Require(NativeTransferIdentityFingerprint() == run.Identity, "Normal disconnect preserves the same native profile identity");
            run.ResetAt = now;
            run.ResetEvidence = new {
                utc = DateTime.UtcNow, oldSessionId = run.OldSession, oldSessionIdText = run.OldSession.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancelTimestamp = run.CancelAt, resetTimestamp = now, stopwatchFrequency = Stopwatch.Frequency,
                disconnected = true, nativeReady = false, fileState = "Idle", fileWorkerAbsent = true,
                fileSessionId = 0, requestedFiles = 0, receiveBuffers = 0, manifestSnapshotAbsent = true,
                detachedCryptoSessionAbsent = true, sameIdentity = true,
                reconnectGate = run.Gate, gateTimeoutSeconds = 30,
                requiredControllerEvidence = "Observe the old server session, pump, content admission, authenticated connection and transport mapping retired on the main thread before releasing the gate." };
            File.WriteAllText(Path.Combine(_output, "native-cancellation-reset-ready.json"), JsonSerializer.Serialize(run.ResetEvidence));
        }
        if (TransferSeconds(run.ResetAt, now) > 30)
            throw new TimeoutException("Native reconnect gate was not released within its fixed thirty-second fixture bound.");
        if (!string.IsNullOrEmpty(run.Gate) ? !File.Exists(run.Gate) : TransferSeconds(run.ResetAt, now) < 1) return true;
        ResetNativeTransferObserverForRejoin();
        run.Attempt = 2; run.ReconnectAt = Stopwatch.GetTimestamp();
        _transferConnectAt = run.ReconnectAt;
        ApiClient.MainMenu.ChangeMenu(0);
        ApiClient.Connect(run.Server);
        return true;
    }

    private void ResetNativeTransferObserverForRejoin()
    {
        // This resets private measurements only. Normal disconnect owns production teardown.
        _transferConnectAt = _transferConsentAt = _transferFirstByteAt = _transferCompleteAt = _transferReadyAt = 0;
        _transferLastFrame = _transferLastSample = _transferQueueEnteredAt = _transferAdmittedAt = 0;
        _transferAdmissionDisconnectedAt = _transferFailureEventAt = 0;
        _transferPeakPendingBytes = _transferPeakPendingFrames = _transferReceived = _transferRequested = 0;
        _transferPeakReceived = _transferPeakRequested = _transferPeakConsumedFrames = _transferCombinedDecodedBytes = 0;
        _transferFailureEventFiles = _transferDomainsUsed = 0;
        _transferLaneMask = 0; _transferConfiguredHost = IntPtr.Zero;
        _transferPinsChecked = _transferFinished = _transferExecutionApproved = _transferSawConnected = false;
        _transferManifest = null; _transferVerification = null; _transferLastObservedState = _transferDisconnectReason = null;
        _transferFrameGaps.Clear();
    }

    private void VerifyNativeTransferCancellationScope()
    {
        var run = _nativeCancellation;
        if (run == null) return;
        Require(run.Threshold < _transferManifest.Files.Sum(file => (long)file.Size), "Native cancellation threshold is smaller than the pinned content inventory");
        if (run.Attempt != 2) return;
        run.NewSession = _transferManifest.SessionId;
        run.NewCrypto = NativeTransferCryptoFingerprint(true);
        Require(run.NewSession != 0 && run.NewSession != run.OldSession && run.NewCrypto != run.OldCrypto &&
            NativeTransferIdentityFingerprint() == run.Identity,
            "Native reconnect keeps the same public identity and creates distinct authenticated crypto/content sessions");
        run.NewScopeVerified = true;
    }

    private void VerifyNativeTransferCancellationReady()
    {
        if (_nativeCancellation == null) return;
        Require(_nativeCancellation.Attempt == 2 && _nativeCancellation.NewScopeVerified && _nativeCancellation.ResetAt != 0 &&
            _transferFinished && _transferExecutionApproved && RemotePrepared && ApiClient.IsClientReady,
            "Native cancellation is followed by fresh-session acquisition, script preparation and actual Ready");
    }

    private object WriteNativeTransferCancellationResult(object verification)
    {
        var run = _nativeCancellation;
        if (run == null) return null;
        VerifyNativeTransferCancellationReady();
        object report = new {
            cancelledAttempt = run.CancelledAttempt, clientResetEvidence = run.ResetEvidence,
            failureEventFilesOnCancelledAttempt = run.CancelledFailureEventFiles,
            reconnectTimestamp = run.ReconnectAt, newSessionId = run.NewSession, newCryptoSessionFingerprint = run.NewCrypto,
            identityPublicFingerprint = run.Identity, sameIdentity = true, distinctContentSession = true, distinctCryptoSession = true,
            serverCleanupGateUsed = !string.IsNullOrEmpty(run.Gate), serverCleanupGateReleasedByController = !string.IsNullOrEmpty(run.Gate),
            serverCleanupProofRequiredSeparately = true, rememberedExactApprovalObserved = run.RememberedApprovalObserved,
            rejoinRequestedBytes = _transferRequested, rejoinConsumedBytes = _transferReceived, verification,
            rejoinPinsMatch = _transferPinsChecked, rejoinScriptsPrepared = RemotePrepared, rejoinReady = ApiClient.IsClientReady,
            cohortConnectToReadySeconds = TransferSeconds(run.FirstConnectAt, _transferReadyAt),
            note = "First attempt is fully cold; completed files may be genuinely reused on rejoin. Every final manifest file is independently hashed. Neither connection deadline is extended; production auth and file owners use normal Disconnect/Connect." };
        File.WriteAllText(Path.Combine(_output, "native-cancellation-result.json"), JsonSerializer.Serialize(report));
        return report;
    }

    private void RecordNativeTransferCancellationFailureEvent(int files)
    {
        _nativeCancellation.CancelledFailureEventFiles = Math.Max(_nativeCancellation.CancelledFailureEventFiles, files);
    }

    private static object TransferRequiredStatic(Type type, string name)
    {
        var field = type.GetField(name, All);
        if (field != null) return field.GetValue(null);
        var property = type.GetProperty(name, All) ?? throw new MissingMemberException(type.FullName, name);
        return property.GetValue(null);
    }

    private static string NativeTransferCryptoFingerprint(bool required)
    {
        byte[] detached = null;
        object sync = TransferRequiredStatic(typeof(AnomalyMessaging), "TransportSync") ?? throw new InvalidOperationException("Missing transport-session lock.");
        lock (sync)
        {
            object value = TransferRequiredStatic(typeof(AnomalyMessaging), "_sessionId");
            if (value == null && !required) return null;
            if (value is not byte[] id || id.Length != AuthTransportCrypto.SessionIdSize)
                throw new InvalidOperationException("No detached authenticated crypto session identifier is available.");
            detached = (byte[])id.Clone(); // Identifier only: never read either directional secret key.
        }
        try { return Convert.ToHexString(SHA256.HashData(detached)); }
        finally { Array.Clear(detached, 0, detached.Length); }
    }

    private static string NativeTransferIdentityFingerprint()
    {
        Type auth = Type.GetType("Anomaly.Client.Auth.AuthHandler, Anomaly.Client", true);
        object profile = TransferRequiredStatic(auth, "PlayerAuth") ?? throw new InvalidOperationException("No current native profile.");
        string encoded = TransferRead(profile, "IdentityPublic") as string;
        byte[] publicIdentity = Convert.FromBase64String(encoded ?? throw new InvalidOperationException("No public identity metadata."));
        if (publicIdentity.Length != 65 || publicIdentity[0] != 4) throw new InvalidDataException("Invalid public identity metadata.");
        return Convert.ToHexString(SHA256.HashData(publicIdentity));
    }

    private sealed class NativeTransferCancellation
    {
        internal long Threshold, FirstConnectAt, CancelAt, ResetAt, ReconnectAt;
        internal int Attempt = 1, CancelledFailureEventFiles;
        internal string Server, Gate, Identity, OldCrypto, NewCrypto;
        internal ulong OldSession, NewSession;
        internal bool CancelRequested, NewScopeVerified, RememberedApprovalObserved;
        internal object CancelledAttempt, ResetEvidence;
    }
}
