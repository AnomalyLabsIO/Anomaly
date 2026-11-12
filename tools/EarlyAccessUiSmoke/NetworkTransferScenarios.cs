using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror.LiteNetLib4Mirror;
using Il2CppTMPro;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private bool _transferWatching, _transferPinsChecked, _transferFinished, _transferExecutionApproved, _transferExpectBudgetRejection;
    private bool _transferExpectAdmissionRejection, _transferSawConnected;
    private double _transferAdmissionRejectionMaxSeconds;
    private long _transferAdmissionDisconnectedAt, _transferFailureEventAt, _transferPeakConsumedFrames;
    private int _transferFailureEventFiles;
    private long _transferConnectAt, _transferConsentAt, _transferFirstByteAt, _transferCompleteAt, _transferReadyAt, _transferLastFrame, _transferLastSample;
    private long _transferQueueEnteredAt, _transferAdmittedAt;
    private double? _transferMinQueueSeconds, _transferMaxQueueSeconds;
    private double? _transferMaxConsentReadySeconds;
    private string _transferLastObservedState;
    private string _transferExpectedDisconnectText, _transferDisconnectReason;
    private string _transferExpectedNoticeCode, _transferExpectedNoticeText;
    private NetworkTransferHold _transferHold;
    private readonly List<double> _transferFrameGaps = new();
    private int _transferRtt, _transferLoss;
    private IntPtr _transferConfiguredHost;
    private ulong _transferLaneMask;
    private long _transferPeakPendingBytes, _transferPeakPendingFrames, _transferReceived, _transferRequested;
    private long _transferPeakReceived, _transferPeakRequested;
    private long _transferCombinedDecodedBytes;
    private int _transferDomainsUsed;
    private FileManifestMessage _transferManifest;
    private JsonDocument _transferExpected;
    private Task<object> _transferVerification;
    private static Type TransferType => Type.GetType("Anomaly.Client.FileTransfer.FileTransferClient, Anomaly.Client", true);
    private static object TransferRead(object owner, string name) => owner?.GetType().GetField(name, All)?.GetValue(owner) ?? owner?.GetType().GetProperty(name, All)?.GetValue(owner);
    private static object TransferStatic(Type type, string name) => type.GetField(name, All)?.GetValue(null) ?? type.GetProperty(name, All)?.GetValue(null);
    private static double TransferSeconds(long first, long last) => first == 0 || last == 0 ? 0 : (last - first) / (double)Stopwatch.Frequency;

    private bool RejectUnsupportedDisconnectPayloadCapture()
    {
        if (Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_DISCONNECT_PAYLOAD_PROBE") != "1") return false;
        const string reason = "Immediate foreign interception of the protected authentication disconnect hook is unsupported. The optional probe has been removed; use the supported production diagnostics and visible terminal notice.";
        File.WriteAllText(Path.Combine(_output, "disconnect-payload-capture-status.json"), JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, requested = true, captureAvailable = false,
            retiredFlag = "ANOMALY_TRANSFER_DISCONNECT_PAYLOAD_PROBE", reason,
            helperPatchAttempted = false, helperConnectionAttempted = false,
            requiredAction = "Unset the retired diagnostic flag. Visible-notice acceptance remains a separate functional check." }));
        Fail(reason);
        return true;
    }

    private void EnqueueNetworkTransferSmoke(string server)
    {
        string expectedPath = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECTED_CONTENT");
        Require(!string.IsNullOrWhiteSpace(expectedPath) && File.Exists(expectedPath), "Pinned generated fixture description exists");
        _transferExpected = JsonDocument.Parse(File.ReadAllBytes(expectedPath));
        _transferExpectBudgetRejection = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_BUDGET_REJECTION") == "1";
        _transferExpectAdmissionRejection = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_ADMISSION_REJECTION") == "1";
        _transferExpectedDisconnectText = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_DISCONNECT_TEXT");
        _transferExpectedNoticeCode = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_NOTICE_CODE");
        _transferExpectedNoticeText = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_NOTICE_TEXT");
        Require(string.IsNullOrEmpty(_transferExpectedNoticeCode) || string.IsNullOrEmpty(_transferExpectedDisconnectText),
            "Terminal-notice acceptance and stock static-reason acceptance use separate private flags");
        Require(string.IsNullOrEmpty(_transferExpectedNoticeText) || !string.IsNullOrEmpty(_transferExpectedNoticeCode),
            "Expected terminal-notice text is paired with an explicit notice code");
        Require(!_transferExpectBudgetRejection || !_transferExpectAdmissionRejection,
            "Pre-consent capacity rejection and post-consent admission rejection are separate private scenarios");
        InitializeNativeTransferCancellation(server);
        if (_transferExpectAdmissionRejection)
        {
            _transferAdmissionRejectionMaxSeconds = double.Parse(
                Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_ADMISSION_REJECTION_MAX_SECONDS") ?? "10",
                System.Globalization.CultureInfo.InvariantCulture);
            Require(!double.IsNaN(_transferAdmissionRejectionMaxSeconds) && !double.IsInfinity(_transferAdmissionRejectionMaxSeconds) &&
                _transferAdmissionRejectionMaxSeconds > 0 && _transferAdmissionRejectionMaxSeconds <= 30,
                "Admission rejection has an explicit short acceptance bound, default ten seconds and at most thirty seconds");
        }
        _transferRtt = int.Parse(Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_RTT_MS") ?? "0");
        _transferLoss = int.Parse(Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_LOSS_PERCENT") ?? "0");
        _transferMinQueueSeconds = ReadNetworkTransferQueueBound("ANOMALY_TRANSFER_MIN_QUEUE_SECONDS", false);
        _transferMaxQueueSeconds = ReadNetworkTransferQueueBound("ANOMALY_TRANSFER_MAX_QUEUE_SECONDS", true);
        string maxConsentReady = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_MAX_SECONDS");
        if (!string.IsNullOrEmpty(maxConsentReady))
        {
            double value = double.Parse(maxConsentReady, System.Globalization.CultureInfo.InvariantCulture);
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value > 0,
                "Consent-to-Ready acceptance bound is finite and positive");
            _transferMaxConsentReadySeconds = value;
        }
        Require(!_transferMinQueueSeconds.HasValue || !_transferMaxQueueSeconds.HasValue ||
            _transferMinQueueSeconds.Value <= _transferMaxQueueSeconds.Value, "Configured native queue acceptance bounds are consistent");
        Require(_transferRtt >= 0 && _transferRtt <= 100 && _transferRtt % 2 == 0 && _transferLoss >= 0 && _transferLoss <= 20,
            "Simulation parameters are bounded; each endpoint delays incoming packets by half the target added RTT");
        FileTransferEvents.Complete += OnNetworkTransferComplete;
        AnomalyMessaging.MessageReceived += ObserveTransferReadinessMessage;
        Add("transfer-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            { Input().SetTextWithoutNotify("Transfer Smoke"); Submit(); }
        });
        string connectGate = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_CONNECT_GATE");
        if (!string.IsNullOrEmpty(connectGate))
        {
            connectGate = Path.GetFullPath(connectGate);
            string ownedOutput = Path.GetFullPath(_output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Require(connectGate.StartsWith(ownedOutput, StringComparison.OrdinalIgnoreCase),
                "Connection gate stays inside this private run's evidence directory");
            Add("transfer-connect-gate", () =>
            {
                Require(!File.Exists(connectGate), "Connection gate starts closed before native readiness is published");
                Require(!ApiClient.IsConnected && !ApiClient.IsClientReady, "Connection gate holds the native client before connecting");
                // Install the normal harness wait and its readiness marker in the same step.
                _waitName = "transfer-connect-gate";
                _wait = () => File.Exists(connectGate);
                _waitDeadline = Time.realtimeSinceStartupAsDouble + 240;
                File.WriteAllText(Path.Combine(_output, "transfer-connect-gate-ready.json"), JsonSerializer.Serialize(new {
                    utc = DateTime.UtcNow, gatePath = connectGate, monotonicTimestamp = Stopwatch.GetTimestamp(),
                    stopwatchFrequency = Stopwatch.Frequency, waitTimeoutSeconds = 240, waitInstalled = true,
                    connected = ApiClient.IsConnected, ready = ApiClient.IsClientReady }));
            });
        }
        Add("transfer-connect", () =>
        {
            _transferWatching = true;
            _transferConnectAt = Stopwatch.GetTimestamp();
            var transport = LiteNetLib4MirrorTransport.Singleton;
            transport.simulateLatency = _transferRtt != 0;
            transport.simulationMinLatency = transport.simulationMaxLatency = _transferRtt / 2;
            transport.simulatePacketLoss = _transferLoss != 0;
            transport.simulationPacketLossChance = _transferLoss;
            ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server);
        });
        if (_transferExpectBudgetRejection)
        {
            EnqueueNetworkTransferCapacityRejection();
            return;
        }
        if (_transferExpectAdmissionRejection)
        {
            EnqueueNetworkTransferAdmissionRejection();
            return;
        }
        AddWait("transfer-ready", PumpNetworkTransferConsent, 600);
        Add("transfer-verify-start", () =>
        {
            if (Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_NO_LOCAL_ASSETS") == "1")
            {
                Type manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
                object local = TransferStatic(manager, "_local");
                Require(TransferRead(local, "Packs") is ICollection packs && packs.Count == 0,
                    "Maximum remote-content control has no active local pack leases");
            }
            string expectedCombined = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_COMBINED_DECODED_BYTES");
            if (!string.IsNullOrEmpty(expectedCombined))
            {
                Type budgetManager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
                var nativePacks = (IDictionary)TransferStatic(budgetManager, "NativePacks");
                foreach (DictionaryEntry nativePack in nativePacks)
                    _transferCombinedDecodedBytes = checked(_transferCombinedDecodedBytes +
                        Convert.ToInt64(TransferRead(TransferRead(TransferRead(nativePack.Value, "Prepared"), "Copy"), "DecodedBytes")));
                Require(_transferCombinedDecodedBytes == long.Parse(expectedCombined, System.Globalization.CultureInfo.InvariantCulture),
                    "Measured native residency matches the exact combined local/server capacity fixture");
                if (Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_NO_LOCAL_ASSETS") != "1")
                    Require(TransferRead(TransferStatic(budgetManager, "_local"), "Packs") is ICollection localPacks && localPacks.Count > 0,
                        "Near-capacity positive case retains normally packaged local assets");
            }
            Require(_transferPinsChecked && _transferFinished && _transferExecutionApproved, "Pinned cold content transferred and explicit fixture script execution approved before Ready");
            Require(RemotePrepared, "Nonempty fixture script set is prepared at Ready");
            Require(_transferReceived == _transferRequested, "Every requested byte reached the disk consumer");
            string expectedMode = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_MODE") ?? "pool";
            Require(expectedMode == "pool" || expectedMode == "fallback", "Expected transport mode is explicit");
            Require(expectedMode == "pool" ? _transferLaneMask != 0 : _transferLaneMask == 0, "Negotiation selected the expected transport mode");
            if (_transferRequested != 0 && expectedMode == "pool") Require(_transferDomainsUsed > 1, "Multiple negotiated bulk domains delivered and released actual file frames");
            if (Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_COLD") != "0")
                Require(_transferRequested > 0 && _transferRequested == _transferManifest.Files.Sum(file => (long)file.Size), "Cold run requested every manifest byte");
            Require(_transferPeakPendingBytes <= 2 * 1024 * 1024 && _transferPeakPendingFrames <= 128, "Observed receiver queue stays within declared byte/frame bounds");
            VerifyNativeTransferCancellationReady();
            if (_transferMaxConsentReadySeconds.HasValue)
                Require(TransferSeconds(_transferConsentAt, _transferReadyAt) <= _transferMaxConsentReadySeconds.Value,
                    "Consent-to-Ready meets the explicitly selected acceptance deadline");
            double queueWaitSeconds = TransferSeconds(_transferQueueEnteredAt, _transferAdmittedAt);
            if (_transferQueueEnteredAt != 0)
                Require(_transferAdmittedAt >= _transferQueueEnteredAt, "Observed native queue admission follows the first queued frame");
            if (_transferMinQueueSeconds.HasValue)
                Require((_transferMinQueueSeconds.Value == 0 || _transferQueueEnteredAt != 0 && _transferAdmittedAt != 0) &&
                    queueWaitSeconds >= _transferMinQueueSeconds.Value, "Observed native queue wait meets the configured minimum acceptance time");
            if (_transferMaxQueueSeconds.HasValue)
                Require(queueWaitSeconds <= _transferMaxQueueSeconds.Value, "Observed native queue wait stays within the configured maximum acceptance time");
            var files = _transferManifest.Files.Select(file => new { hash = file.Hash, bytes = file.Size, path = file.Path,
                cachePath = (string)Type.GetType("Anomaly.Client.Api.FileTransfer.FileCache, Anomaly.Client.Api", true)
                    .GetMethod("GetFilePath", All).Invoke(null, new object[] { file.Hash }) }).ToArray();
            _transferVerification = Task.Run<object>(() =>
            {
                long verified = 0;
                foreach (var file in files)
                {
                    using var input = File.OpenRead(file.cachePath);
                    using var sha = SHA256.Create();
                    if (input.Length != file.bytes || !sha.ComputeHash(input).SequenceEqual(file.hash))
                        throw new InvalidDataException("Transferred cache file failed independent size/hash verification: " + file.path);
                    verified += file.bytes;
                }
                return new { verifiedBytes = verified, files = files.Length };
            });
        });
        AddWait("transfer-verified", () => _transferVerification.IsCompleted, 90);
        Add("transfer-result", () =>
        {
            object verification = _transferVerification.GetAwaiter().GetResult();
            object nativeCancellation = WriteNativeTransferCancellationResult(verification);
            double queueWaitSeconds = TransferSeconds(_transferQueueEnteredAt, _transferAdmittedAt);
            _transferWatching = false;
            _transferFrameGaps.Sort();
            double Percentile(double p) => _transferFrameGaps.Count == 0 ? 0 : _transferFrameGaps[Math.Min(_transferFrameGaps.Count - 1, (int)Math.Ceiling(_transferFrameGaps.Count * p) - 1)];
            var result = new { nominalMiB = _transferExpected.RootElement.GetProperty("NominalMiB").GetInt32(),
                rttSimulationMs = _transferRtt, incomingLossPercent = _transferLoss, laneMask = _transferLaneMask,
                mode = _transferLaneMask == 0 ? "bounded-fallback" : "negotiated-bulk-pool", requestedBytes = _transferRequested,
                domainsUsed = _transferDomainsUsed,
                receivedBytes = _transferReceived, verification, connectToReadySeconds = TransferSeconds(_transferConnectAt, _transferReadyAt),
                consentToReadySeconds = TransferSeconds(_transferConsentAt, _transferReadyAt),
                queueEnteredTimestamp = _transferQueueEnteredAt == 0 ? (long?)null : _transferQueueEnteredAt,
                admittedTimestamp = _transferAdmittedAt == 0 ? (long?)null : _transferAdmittedAt,
                queueEnteredSecondsFromConnect = _transferQueueEnteredAt == 0 ? (double?)null : TransferSeconds(_transferConnectAt, _transferQueueEnteredAt),
                admittedSecondsFromConnect = _transferAdmittedAt == 0 ? (double?)null : TransferSeconds(_transferConnectAt, _transferAdmittedAt),
                queueWaitSeconds, minimumQueueAcceptanceSeconds = _transferMinQueueSeconds, maximumQueueAcceptanceSeconds = _transferMaxQueueSeconds,
                queueTimestampFrequency = Stopwatch.Frequency,
                firstByteToCompleteSeconds = TransferSeconds(_transferFirstByteAt, _transferCompleteAt),
                completeToReadySeconds = TransferSeconds(_transferCompleteAt, _transferReadyAt),
                frameSamples = _transferFrameGaps.Count, frameP95Ms = Percentile(.95), frameP99Ms = Percentile(.99), frameMaxMs = Percentile(1),
                peakPendingBytes = _transferPeakPendingBytes, peakPendingFrames = _transferPeakPendingFrames, scriptsPrepared = RemotePrepared,
                combinedDecodedBytes = _transferCombinedDecodedBytes,
                nativeCancellation,
                note = "Native simulation settings are requested metadata only: shipped release call sites are omitted. Actual relay impairment, if used, is recorded separately. Frame gaps include loading and observer; queue peaks are sampled. Queue timestamps are first per-frame observations on the monotonic clock. Configured queue acceptance bounds do not change production timers." };
            File.WriteAllText(Path.Combine(_output, "transfer-result.json"), JsonSerializer.Serialize(result));
        });
        AddSmokeRequest("transfer-server-report", "transfer-report", json =>
        {
            Require(json.GetProperty("valid").GetBoolean(), "Server instrumentation completed without missing-field or observation errors");
            if (_transferRequested != 0 && _transferLaneMask != 0)
                Require(json.GetProperty("laneBytes").EnumerateObject().Count() > 1, "Actual unicast batches used multiple physical bulk lanes");
            File.WriteAllText(Path.Combine(_output, "transfer-server-report.json"), json.GetRawText());
        });
        AddNetworkTransferHold();
        Add("transfer-disconnect", () => ApiClient.Disconnect());
        AddWait("transfer-disconnected", () => !ApiClient.IsConnected && !ApiClient.IsClientReady, 40);
        Add("transfer-complete", () =>
        {
            FileTransferEvents.Complete -= OnNetworkTransferComplete;
            AnomalyMessaging.MessageReceived -= ObserveTransferReadinessMessage;
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Network transfer smoke passed; independently hash-verified cached files and prepared nonempty scripts before Ready. See measured transfer-result.json.\n");
            Application.Quit();
        });
    }

    private void ObserveTransferReadinessMessage(Anomaly.Shared.Networking.IAnomalyMessage message)
    {
        try
        {
            var registry = Type.GetType("Anomaly.Client.Api.Internal.Wrappers.PlayerRegistry, Anomaly.Client.Api", true);
            object[] arguments = { null };
            bool hasHub = (bool)registry.GetMethod("TryGetLocalHub", All).Invoke(null, arguments);
            var bootstrap = Type.GetType("Anomaly.Client.Content.ConnectionContentBootstrap, Anomaly.Client", true);
            File.AppendAllText(Path.Combine(_output, "readiness-messages.jsonl"), JsonSerializer.Serialize(new {
                utc = DateTime.UtcNow, valid = true, message = message.MessageName,
                channel = message.TransportChannel.ToString(), localHubAvailable = hasHub,
                contentAdmitted = (bool)TransferStatic(bootstrap, "IsAdmitted"),
                nativeReady = Il2CppMirror.NetworkClient.ready, apiReady = ApiClient.IsClientReady,
                scope = "Normal API event observation after authenticated dispatch; no message interception or payload capture." }) + "\n");
        }
        catch (Exception exception)
        {
            File.AppendAllText(Path.Combine(_output, "readiness-messages.jsonl"), JsonSerializer.Serialize(new {
                utc = DateTime.UtcNow, valid = false, error = exception.ToString() }) + "\n");
        }
    }

    private double? ReadNetworkTransferQueueBound(string name, bool positive)
    {
        string text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(text)) return null;
        bool parsed = double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value);
        Require(parsed && !double.IsNaN(value) && !double.IsInfinity(value) && (positive ? value > 0 : value >= 0) &&
            value <= FileTransferStatusMessage.MaxQueueWaitMilliseconds / 1000d,
            name + " is an explicit acceptance bound within the protocol's existing sixty-second queue limit");
        return value;
    }

    private void WriteNetworkTransferQueueEvidence(long now, string state)
    {
        File.WriteAllText(Path.Combine(_output, "transfer-queue.json"), JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, state, monotonicTimestamp = now, stopwatchFrequency = Stopwatch.Frequency,
            queueEnteredTimestamp = _transferQueueEnteredAt == 0 ? (long?)null : _transferQueueEnteredAt,
            admittedTimestamp = _transferAdmittedAt == 0 ? (long?)null : _transferAdmittedAt,
            queueWaitSeconds = _transferQueueEnteredAt == 0 ? 0 :
                TransferSeconds(_transferQueueEnteredAt, _transferAdmittedAt == 0 ? now : _transferAdmittedAt),
            minimumQueueAcceptanceSeconds = _transferMinQueueSeconds, maximumQueueAcceptanceSeconds = _transferMaxQueueSeconds,
            note = "First per-frame observations; repeated queued/admitted states cannot renew these timestamps or production timers." }));
    }

    private void EnqueueNetworkTransferCapacityRejection()
    {
        string expectedCode = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_REJECTION_CODE") ?? "CONTENT-BUNDLE-BUDGET";
        string expectedText = Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_EXPECT_REJECTION_TEXT");
        string notice = null;
        AddWait("transfer-capacity-rejection-notice", () =>
        {
            if (Instance() == null || !IsVisible()) return false;
            var prompt = Instance();
            string label = ((TMP_Text)prompt.GetType().GetField("_acceptLabel", All).GetValue(prompt)).text;
            if (label == "Trust and connect") { Submit(); return false; }
            if (label == "Allow content" || label == "Run scripts")
                throw new InvalidOperationException("An incompatible content offer reached approval before capacity rejection.");
            string details = ((TMP_Text)prompt.GetType().GetField("_details", All).GetValue(prompt)).text;
            if (!details.Contains(expectedCode, StringComparison.Ordinal)) return false;
            Require(string.IsNullOrEmpty(expectedText) || details.Contains(expectedText, StringComparison.Ordinal),
                "Capacity notice identifies the explicitly selected capacity limit");
            notice = details;
            return true;
        }, 90);
        AddWait("transfer-capacity-disconnected", () => !ApiClient.IsConnected && !ApiClient.IsClientReady, 30);
        Add("transfer-capacity-rejection-result", () =>
        {
            Require(_transferConsentAt == 0 && !_transferExecutionApproved && !_transferFinished,
                "Capacity rejection precedes content/script approval and transfer completion");
            Require(_transferPeakRequested == 0 && _transferPeakReceived == 0 && _transferDomainsUsed == 0,
                "No requested or disk-consumed file bytes were observed before capacity rejection");
            Require(_transferReadyAt == 0 && !Il2CppMirror.NetworkClient.ready && !RemotePrepared,
                "Capacity rejection never releases native Ready or prepared scripts");
            Type manager = Type.GetType("Anomaly.Client.Assets.AssetBundleManager, Anomaly.Client", true);
            object local = TransferStatic(manager, "_local");
            Require(TransferRead(local, "Packs") is ICollection packs && packs.Count > 0,
                "Capacity rejection includes the normally staged local skin pack");
            File.WriteAllText(Path.Combine(_output, "capacity-rejection.json"), JsonSerializer.Serialize(new {
                expectedCode, expectedText, notice, peakRequestedBytes = _transferPeakRequested, peakConsumedBytes = _transferPeakReceived,
                domainsUsed = _transferDomainsUsed, ready = ApiClient.IsClientReady, nativeReady = Il2CppMirror.NetworkClient.ready,
                scriptsPrepared = RemotePrepared, connectToRejectionSeconds = TransferSeconds(_transferConnectAt, Stopwatch.GetTimestamp()),
                note = "Native negative case. Independently inspect server physical-lane totals to corroborate zero body transfer." }));
            _transferWatching = false;
            FileTransferEvents.Complete -= OnNetworkTransferComplete;
            AnomalyMessaging.MessageReceived -= ObserveTransferReadinessMessage;
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Native capacity rejection passed before content approval, file acquisition or Ready.\n");
            Application.Quit();
        });
    }

    private void EnqueueNetworkTransferAdmissionRejection()
    {
        AddWait("transfer-admission-rejection-observation", () =>
        {
            PumpNetworkTransferConsent(); // Trust normally, then approve only the exact pinned content offer.
            return _transferAdmissionDisconnectedAt != 0;
        }, 90);
        if (!string.IsNullOrEmpty(_transferExpectedNoticeCode))
            AddWait("transfer-admission-rejection-terminal-notice", () =>
                CaptureNetworkTransferAdmissionNotice("waiting-for-terminal-notice").Matches(_transferExpectedNoticeCode, _transferExpectedNoticeText), 10);
        Add("transfer-admission-rejection-capture", () =>
        {
            var notice = CaptureNetworkTransferAdmissionNotice("before-screenshot");
            if (!string.IsNullOrEmpty(_transferExpectedNoticeCode))
                Require(notice.Matches(_transferExpectedNoticeCode, _transferExpectedNoticeText),
                    "The visible terminal failure notice contains the selected code and reason text before screenshot");
            Capture("admission-rejection-disconnect");
        });
        Add("transfer-admission-rejection-result", () =>
        {
            WriteNetworkTransferAdmissionObservation("before-client-assertions", Stopwatch.GetTimestamp());
            string reasonAtAssertion = LiteNetLib4MirrorClient.LastDisconnectReason;
            var notice = CaptureNetworkTransferAdmissionNotice("before-client-assertions");
            Require(_transferPinsChecked && _transferConsentAt != 0 && _transferPeakRequested > 0 && _transferSawConnected,
                "Admission-rejection candidate followed pinned approval and an actual positive file request on the connected client");
            Require(!ApiClient.IsConnected && !ApiClient.IsClientReady && !Il2CppMirror.NetworkClient.ready && _transferReadyAt == 0,
                "Admission-rejection candidate disconnected without releasing native Ready");
            Require(!_transferFinished && !_transferExecutionApproved && !RemotePrepared && _transferCompleteAt == 0,
                "Admission-rejection candidate did not complete content or approve/prepare remote scripts");
            Require(_transferPeakReceived == 0 && _transferPeakConsumedFrames == 0 && _transferDomainsUsed == 0 &&
                _transferPeakPendingBytes == 0 && _transferPeakPendingFrames == 0 && _transferFailureEventFiles == 0,
                "No consumed or pending file frames were observed in the admission-rejection candidate");
            Require(_transferQueueEnteredAt == 0 && _transferAdmittedAt == 0 && _transferFirstByteAt == 0,
                "Admission-rejection candidate never entered queue/admission/body reception");
            double elapsed = TransferSeconds(_transferConsentAt, _transferAdmissionDisconnectedAt);
            Require(_transferAdmissionDisconnectedAt >= _transferConsentAt && elapsed <= _transferAdmissionRejectionMaxSeconds,
                "Observed post-consent disconnect meets the short configured admission-rejection acceptance bound");
            Require(string.IsNullOrEmpty(_transferExpectedDisconnectText) ||
                !string.IsNullOrEmpty(reasonAtAssertion) && reasonAtAssertion.Contains(_transferExpectedDisconnectText, StringComparison.Ordinal),
                "Stock decoded client disconnect reason contains the explicitly selected text when configured");
            if (!string.IsNullOrEmpty(_transferExpectedNoticeCode))
                Require(notice.Matches(_transferExpectedNoticeCode, _transferExpectedNoticeText),
                    "The same visible terminal notice survives through disconnected-state assertions");
            object validated = WriteNetworkTransferAdmissionObservation("client-assertions-passed", Stopwatch.GetTimestamp(), true);
            File.WriteAllText(Path.Combine(_output, "admission-rejection-candidate.json"), JsonSerializer.Serialize(validated));
            _transferWatching = false;
            FileTransferEvents.Complete -= OnNetworkTransferComplete;
            AnomalyMessaging.MessageReceived -= ObserveTransferReadinessMessage;
            File.WriteAllText(Path.Combine(_output, "result.txt"),
                "Post-consent admission-rejection candidate observed before queue/body/Ready. CPU or capacity attribution requires matching server rejection reason and measured AdmissionCapacity.\n");
            Application.Quit();
        });
    }

    private void ObserveNetworkTransferAdmissionRejection(long now)
    {
        if (!_transferExpectAdmissionRejection) return;
        if (ApiClient.IsConnected) _transferSawConnected = true;
        if (ApiClient.IsClientReady || Il2CppMirror.NetworkClient.ready || _transferReadyAt != 0 ||
            _transferFinished || _transferExecutionApproved || RemotePrepared)
        {
            WriteNetworkTransferAdmissionObservation("unexpected-ready-or-script-state", now);
            throw new InvalidOperationException("Expected admission rejection reached native Ready, completed content, or script execution.");
        }
        if (_transferQueueEnteredAt != 0 || _transferAdmittedAt != 0 || _transferPeakReceived != 0 ||
            _transferPeakConsumedFrames != 0 || _transferPeakPendingBytes != 0 || _transferPeakPendingFrames != 0 || _transferFailureEventFiles != 0)
        {
            WriteNetworkTransferAdmissionObservation("unexpected-queue-or-file-state", now);
            throw new InvalidOperationException("Expected immediate admission rejection entered queue/admission/body reception.");
        }
        if (_transferConsentAt == 0) return;
        if (_transferAdmissionDisconnectedAt == 0 && _transferSawConnected && _transferPeakRequested > 0 && !ApiClient.IsConnected)
        {
            _transferAdmissionDisconnectedAt = now;
            _transferDisconnectReason = LiteNetLib4MirrorClient.LastDisconnectReason;
            WriteNetworkTransferAdmissionObservation("disconnect-observed", now);
        }
        if (_transferAdmissionDisconnectedAt == 0 && TransferSeconds(_transferConsentAt, now) > _transferAdmissionRejectionMaxSeconds)
        {
            WriteNetworkTransferAdmissionObservation("post-consent-timeout", now);
            throw new TimeoutException("Post-consent admission rejection did not disconnect within the explicitly selected short acceptance bound.");
        }
    }

    private object WriteNetworkTransferAdmissionObservation(string stage, long now, bool assertionsPassed = false)
    {
        object observation = new {
            utc = DateTime.UtcNow, stage, candidateObservation = assertionsPassed, clientAssertionsPassed = assertionsPassed,
            serverAttributionConfirmed = false,
            requiredServerEvidence = "Match this offered session to admission-rejections.jsonl and inspect the exact rejection reason and measured AdmissionCapacity. A client disconnect alone does not prove CPU admission rejection.",
            sessionId = _transferManifest?.SessionId, offeredFiles = _transferManifest?.Files.Count,
            offeredBytes = _transferManifest?.Files.Sum(file => (long)file.Size), peakRequestedBytes = _transferPeakRequested,
            peakConsumedBytes = _transferPeakReceived, peakConsumedFrames = _transferPeakConsumedFrames,
            peakPendingBytes = _transferPeakPendingBytes, peakPendingFrames = _transferPeakPendingFrames,
            monotonicTimestamp = now, consentTimestamp = _transferConsentAt, disconnectedTimestamp = _transferAdmissionDisconnectedAt,
            stopwatchFrequency = Stopwatch.Frequency,
            consentToDisconnectSeconds = TransferSeconds(_transferConsentAt, _transferAdmissionDisconnectedAt),
            consentToObservationSeconds = TransferSeconds(_transferConsentAt, now),
            maximumConsentToDisconnectSeconds = _transferAdmissionRejectionMaxSeconds,
            stockLastDisconnectReasonAtFirstDisconnectedFrame = _transferDisconnectReason,
            stockLastDisconnectReason = LiteNetLib4MirrorClient.LastDisconnectReason, expectedDisconnectText = _transferExpectedDisconnectText,
            acceptanceMode = !string.IsNullOrEmpty(_transferExpectedNoticeCode) ? "anomaly-terminal-notice" :
                !string.IsNullOrEmpty(_transferExpectedDisconnectText) ? "stock-static-reason" : "candidate-observation-only",
            expectedNoticeCode = _transferExpectedNoticeCode, expectedNoticeText = _transferExpectedNoticeText,
            visibleNotice = ReadNetworkTransferAdmissionNotice(),
            immediateAdditionalDataCaptureAvailable = false,
            immediateCaptureScope = "Private foreign auth interception is unsupported and removed. This observation uses the real visible notice and stock static fields; no independent immediate payload capture is claimed.",
            currentFileState = TransferStatic(TransferType, "_state")?.ToString(), lastObservedFileState = _transferLastObservedState,
            disconnectScreenshot = "admission-rejection-disconnect", screenshotTextProgrammaticallyAsserted = false,
            failureEventObserved = _transferFailureEventAt != 0,
            failureEventTimestamp = _transferFailureEventAt == 0 ? (long?)null : _transferFailureEventAt,
            failureEventCompletedFiles = _transferFailureEventFiles,
            queueEntered = _transferQueueEnteredAt != 0, admitted = _transferAdmittedAt != 0,
            connected = ApiClient.IsConnected, ready = ApiClient.IsClientReady, nativeReady = Il2CppMirror.NetworkClient.ready,
            scriptsPrepared = RemotePrepared,
            note = "Raw observations persist before client assertions. A failed assertion does not validate this candidate or attribute its cause. Production timers, policy and wire messages are unchanged." };
        File.WriteAllText(Path.Combine(_output, "admission-rejection-observation.json"), JsonSerializer.Serialize(observation));
        return observation;
    }

    private NetworkTransferAdmissionNotice CaptureNetworkTransferAdmissionNotice(string stage)
    {
        var notice = ReadNetworkTransferAdmissionNotice();
        File.WriteAllText(Path.Combine(_output, "admission-rejection-notice.json"), JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, monotonicTimestamp = Stopwatch.GetTimestamp(), stage, notice,
            expectedCode = _transferExpectedNoticeCode, expectedText = _transferExpectedNoticeText,
            matches = notice.Matches(_transferExpectedNoticeCode, _transferExpectedNoticeText) }));
        return notice;
    }

    private static NetworkTransferAdmissionNotice ReadNetworkTransferAdmissionNotice()
    {
        var instance = Instance();
        object lease = Prompt.GetField("LeaseGate", All)?.GetValue(null)
            ?? throw new MissingMemberException(Prompt.FullName, "LeaseGate");
        object sync = lease.GetType().GetField("_sync", All)?.GetValue(lease)
            ?? throw new MissingMemberException(lease.GetType().FullName, "_sync");
        string owner;
        lock (sync)
            owner = (lease.GetType().GetField("_owner", All) ?? throw new MissingFieldException(lease.GetType().FullName, "_owner")).GetValue(lease) as string;
        var root = TransferRead(instance, "_root") as GameObject;
        var details = TransferRead(instance, "_details") as TMP_Text;
        return new NetworkTransferAdmissionNotice {
            Visible = instance != null && IsVisible(), RootActive = root != null && root.activeInHierarchy, Owner = owner,
            Title = (TransferRead(instance, "_title") as TMP_Text)?.text, Details = details?.text,
            DetailsRichText = details != null && details.richText,
            InputActive = (TransferRead(instance, "_input") as TMP_InputField)?.gameObject.activeInHierarchy == true,
            AcceptActive = (TransferRead(instance, "_accept") as UnityEngine.UI.Button)?.gameObject.activeInHierarchy == true,
            DismissActive = (TransferRead(instance, "_cancel") as UnityEngine.UI.Button)?.gameObject.activeInHierarchy == true };
    }

    private sealed class NetworkTransferAdmissionNotice
    {
        public bool Visible { get; set; }
        public bool RootActive { get; set; }
        public string Owner { get; set; }
        public string Title { get; set; }
        public string Details { get; set; }
        public bool DetailsRichText { get; set; }
        public bool InputActive { get; set; }
        public bool AcceptActive { get; set; }
        public bool DismissActive { get; set; }
        internal bool Matches(string code, string text) => !string.IsNullOrEmpty(code) && Visible && RootActive &&
            Owner == "connection-failure" && !DetailsRichText && !InputActive && !AcceptActive && DismissActive &&
            Details != null && Details.Contains(code, StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(text) || Details.Contains(text, StringComparison.Ordinal));
    }

    // Holds the real game ready while separate protocol peers acquire content.
    // Ordinary encrypted control requests measure responsiveness on the shared socket.
    private void AddNetworkTransferHold()
    {
        int seconds = int.Parse(Environment.GetEnvironmentVariable("ANOMALY_TRANSFER_HOLD_SECONDS") ?? "0");
        Require(seconds == 0 || seconds >= 5 && seconds <= 600, "Established-player hold is disabled or lasts five seconds to ten minutes");
        if (seconds == 0) return;
        var hold = new NetworkTransferHold { Seconds = seconds };
        AddSmokeRequest("transfer-hold-player-role", "prepare", _ => { });
        Add("transfer-hold-start", () =>
        {
            hold.Started = hold.PreviousFrame = Stopwatch.GetTimestamp();
            _transferHold = hold;
            File.WriteAllText(Path.Combine(_output, "transfer-hold-ready.json"),
                JsonSerializer.Serialize(new { utc = DateTime.UtcNow, seconds, ready = ApiClient.IsClientReady,
                    monotonicTimestamp = hold.Started, stopwatchFrequency = Stopwatch.Frequency,
                    sampling = "Frames and control replies observed on every game update; harness step polling only waits for termination." }));
        });
        AddWait("transfer-established-player-hold", () => hold.FinishedAt != 0, seconds + 30);
        Add("transfer-hold-report", () =>
        {
            bool enough = hold.Replies.Count >= 5;
            WriteNetworkTransferHoldReport(hold, enough, enough ? null : "Fewer than five actual control responses were observed.");
            Require(enough, "Established-player hold includes at least five actual control responses");
        });
    }

    private void WriteNetworkTransferHoldReport(NetworkTransferHold hold, bool success, string failureReason)
    {
        // Persist chronological data before any final assertion or Fail can quit the game.
        File.WriteAllText(Path.Combine(_output, "transfer-established-player-frames.json"), JsonSerializer.Serialize(new {
            success, status = success ? "Completed" : "Failed", failureReason,
            startedTimestamp = hold.Started, finishedTimestamp = hold.FinishedAt, stopwatchFrequency = Stopwatch.Frequency,
            sampling = "Every native game update, independently of the 500ms harness wait poll.",
            frames = hold.Frames.Select((gap, index) => new { seconds = hold.FrameSeconds[index], gapMs = gap }) }));
        double[] replies = hold.Replies.ToArray(), frames = hold.Frames.ToArray();
        Array.Sort(replies); Array.Sort(frames);
        double Percentile(double[] values, double p) => values.Length == 0 ? 0 :
            values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * p) - 1)];
        File.WriteAllText(Path.Combine(_output, "transfer-established-player-summary.json"), JsonSerializer.Serialize(new {
            success, status = success ? "Completed" : "Failed", failureReason,
            seconds = TransferSeconds(hold.Started, hold.FinishedAt), responses = replies.Length,
            controlP95Ms = Percentile(replies, .95), controlP99Ms = Percentile(replies, .99), controlMaxMs = Percentile(replies, 1),
            frameSamples = frames.Length, frameP95Ms = Percentile(frames, .95), frameP99Ms = Percentile(frames, .99), frameMaxMs = Percentile(frames, 1),
            pendingControlRequest = hold.Pending, pendingControlAgeMs = hold.Pending == 0 ? 0 : TransferSeconds(hold.Sent, hold.FinishedAt) * 1000,
            connected = ApiClient.IsConnected, ready = ApiClient.IsClientReady, nativeReady = Il2CppMirror.NetworkClient.ready,
            stockLastDisconnectReason = LiteNetLib4MirrorClient.LastDisconnectReason,
            startedTimestamp = hold.Started, finishedTimestamp = hold.FinishedAt, stopwatchFrequency = Stopwatch.Frequency,
            note = "Actual native player and ordinary encrypted control messages, observed each game update. Failed reports contain partial observations through the failure and are not qualification passes. Response times include up to one native frame of observer delay. Shared-process synthetic peers add fixture CPU/GC/disk load. Does not qualify voice quality." }));
    }

    private void PollNetworkTransferHold()
    {
        var hold = _transferHold;
        if (hold == null || _failed) return;
        try
        {
            long now = Stopwatch.GetTimestamp();
            if (hold.Frames.Count < 200000)
            {
                hold.Frames.Add(TransferSeconds(hold.PreviousFrame, now) * 1000);
                hold.FrameSeconds.Add(TransferSeconds(hold.Started, now));
            }
            hold.PreviousFrame = now;
            if (!ApiClient.IsConnected || !ApiClient.IsClientReady || !RemotePrepared)
                throw new InvalidOperationException("Established native player lost connection or readiness during concurrent acquisition.");
            if (hold.Pending != 0 && _smokeResponses.TryGetValue(hold.Pending, out var response))
            {
                _smokeResponses.Remove(hold.Pending);
                Require(response.Success, "Established player control response succeeds");
                double elapsed = TransferSeconds(hold.Sent, now) * 1000;
                hold.Replies.Add(elapsed);
                File.AppendAllText(Path.Combine(_output, "transfer-established-player.jsonl"), JsonSerializer.Serialize(new {
                    utc = DateTime.UtcNow, monotonicTimestamp = now, seconds = TransferSeconds(hold.Started, now), responseMs = elapsed,
                    peerPingMs = LiteNetLib4MirrorCore.Host?.FirstPeer?.Ping,
                    peerRoundTripTimeMs = LiteNetLib4MirrorCore.Host?.FirstPeer?.RoundTripTime,
                    ready = ApiClient.IsClientReady, sampling = "Per native game update; peerPingMs is half RTT." }) + "\n");
                hold.Pending = 0;
            }
            if (hold.Pending != 0 && TransferSeconds(hold.Sent, now) > 10)
                throw new TimeoutException("Established-player encrypted control response exceeded ten seconds.");
            bool finish = TransferSeconds(hold.Started, now) >= hold.Seconds || File.Exists(Path.Combine(_output, "release-hold.flag"));
            if (finish && hold.Pending == 0) { hold.FinishedAt = now; _transferHold = null; return; }
            if (!finish && hold.Pending == 0 && now >= hold.NextSend)
            {
                hold.Pending = ++_requestToken; hold.Sent = now; hold.NextSend = now + Stopwatch.Frequency;
                AnomalyMessaging.Send(new SmokeRequest { Token = hold.Pending, Command = "ping" });
            }
        }
        catch (Exception error)
        {
            hold.FinishedAt = Stopwatch.GetTimestamp();
            _transferHold = null;
            string failure = "Established-player per-frame observer: " + error;
            try { WriteNetworkTransferHoldReport(hold, false, failure); }
            catch (Exception reporting) { failure += "\nPartial HOLD evidence write failed: " + reporting; }
            Fail(failure);
        }
    }

    private sealed class NetworkTransferHold
    {
        internal int Seconds, Pending;
        internal long Started, PreviousFrame, Sent, NextSend, FinishedAt;
        internal readonly List<double> Replies = new(), Frames = new(), FrameSeconds = new();
    }

    private void OnNetworkTransferComplete(FileTransferCompleteEventArgs args)
    {
        if (!_transferWatching) return;
        if (!args.Success)
        {
            if (IsIntentionalNativeTransferCancellation)
            {
                RecordNativeTransferCancellationFailureEvent(args.FileCount);
                return;
            }
            if (_transferExpectAdmissionRejection)
            {
                try
                {
                    _transferFailureEventAt = Stopwatch.GetTimestamp();
                    _transferFailureEventFiles = Math.Max(_transferFailureEventFiles, args.FileCount);
                    _transferPeakRequested = Math.Max(_transferPeakRequested, Convert.ToInt64(TransferStatic(TransferType, "_totalBytes")));
                    _transferPeakReceived = Math.Max(_transferPeakReceived, Convert.ToInt64(TransferStatic(TransferType, "_receivedBytes")));
                    if (TransferStatic(TransferType, "_releasedFrames") is uint[] released)
                        _transferPeakConsumedFrames = Math.Max(_transferPeakConsumedFrames, released.Sum(count => (long)count));
                    File.AppendAllText(Path.Combine(_output, "admission-rejection-events.jsonl"), JsonSerializer.Serialize(new {
                        utc = DateTime.UtcNow, monotonicTimestamp = _transferFailureEventAt, success = false, completedFiles = args.FileCount,
                        peakRequestedBytes = _transferPeakRequested, peakConsumedBytes = _transferPeakReceived,
                        peakConsumedFrames = _transferPeakConsumedFrames, note = "Failure event captured; waiting for actual disconnect evidence, without attributing its cause." }) + "\n");
                }
                catch (Exception error) { Fail("Admission rejection event observer: " + error); }
            }
            else if (!_transferExpectBudgetRejection)
            {
                try
                {
                    WriteNetworkTransferFailureContext("completion-failure-event", Stopwatch.GetTimestamp(),
                        TransferStatic(TransferType, "_state")?.ToString(), _transferLastObservedState);
                }
                catch (Exception error) { Fail("Transfer failure-context observer: " + error); return; }
                Fail("Network transfer completed unsuccessfully; see transfer-terminal-context.json.");
            }
            return;
        }
        if (_transferExpectAdmissionRejection) { Fail("Expected admission rejection completed file acquisition instead."); return; }
        Require(true, "Transfer completion succeeded");
        _transferFinished = args.Success;
        _transferCompleteAt = Stopwatch.GetTimestamp();
    }

    private bool PumpNetworkTransferConsent()
    {
        if (IsIntentionalNativeTransferCancellation) return false;
        if (Instance() != null && IsVisible())
        {
            string label = ((TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance())).text;
            if (label == "Trust and connect") { Submit(); return false; }
            if (label == "Allow content" || label == "Run scripts")
            {
                if (_transferExpectAdmissionRejection && label == "Run scripts")
                    throw new InvalidOperationException("Expected admission rejection reached the script execution prompt.");
                VerifyNetworkTransferPins();
                if (label == "Allow content") _transferConsentAt = Stopwatch.GetTimestamp();
                else _transferExecutionApproved = true;
                Submit();
                return false;
            }
        }
        return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) && Il2Cpp.StaticUnityMethods.IsPlaying;
    }

    private void VerifyNetworkTransferPins()
    {
        Type type = Type.GetType("Anomaly.Client.Content.ContentCatalogClient, Anomaly.Client", true);
        var catalog = (ContentCatalog)TransferStatic(type, "_catalog");
        var manifest = (FileManifestMessage)TransferStatic(type, "_manifest");
        Require(catalog != null && manifest != null, "Authenticated catalog and manifest are available before fixture approval");
        JsonElement expected = _transferExpected.RootElement;
        var packs = expected.GetProperty("AssetPacks").EnumerateArray().ToArray();
        Require(catalog.Packs.Count == packs.Length, "Only the pinned fixture asset packs are offered");
        foreach (var pack in packs)
        {
            string id = pack.GetProperty("PackId").GetString();
            var actual = catalog.Packs.Single(value => value.PackId == id);
            Require(Convert.ToHexString(actual.BundleHash).Equals(pack.GetProperty("BundleSha256").GetString(), StringComparison.OrdinalIgnoreCase) &&
                Convert.ToHexString(actual.IndexHash).Equals(pack.GetProperty("IndexSha256").GetString(), StringComparison.OrdinalIgnoreCase), "Pinned fixture bundle and index: " + id);
            if (pack.TryGetProperty("DecodedBytes", out var decodedBytes))
                Require(actual.DecodedBytes == decodedBytes.GetInt64(), "Pinned measured decoded capacity: " + id);
        }
        Require(catalog.ScriptPacks.Count == 1 && catalog.ScriptPacks[0].PackId == expected.GetProperty("ScriptPackId").GetString() &&
            catalog.ScriptPacks[0].Sources.Count == 1 && Convert.ToHexString(catalog.ScriptPacks[0].Sources[0].Hash)
                .Equals(expected.GetProperty("ScriptSha256").GetString(), StringComparison.OrdinalIgnoreCase), "Only the exact generated benign Lua source may execute");
        if (expected.TryGetProperty("ManifestFiles", out var manifestPins))
        {
            var pins = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in manifestPins.EnumerateArray())
                pins.Add(pin.GetProperty("hash").GetString(), pin.GetProperty("bytes").GetInt64());
            Require(manifest.Files.Count == pins.Count && manifest.Files.All(file => pins.TryGetValue(Convert.ToHexString(file.Hash), out long bytes) && bytes == file.Size),
                "Entire wire manifest matches production-codec fixture pins, including the generated script index");
        }
        _transferManifest = manifest;
        File.WriteAllText(Path.Combine(_output, "transfer-manifest.json"), JsonSerializer.Serialize(new {
            sessionId = manifest.SessionId, files = manifest.Files.Select(file => new { hash = Convert.ToHexString(file.Hash).ToLowerInvariant(),
                bytes = file.Size, path = file.Path, kind = file.Kind.ToString() }) }));
        _transferPinsChecked = true;
        VerifyNativeTransferCancellationScope();
    }

    private void PollNetworkTransferSmoke()
    {
        // The step runner polls waits at 500ms; hold latency/frame measurements must not use that clock.
        PollNetworkTransferHold();
        if (!_transferWatching || _failed) return;
        try
        {
            long now = Stopwatch.GetTimestamp();
            if (ApiClient.IsConnected) _transferSawConnected = true;
            if (_transferLastFrame != 0 && _transferFrameGaps.Count < 200000)
                _transferFrameGaps.Add((now - _transferLastFrame) * 1000d / Stopwatch.Frequency);
            _transferLastFrame = now;
            var host = LiteNetLib4MirrorCore.Host;
            if (host != null && host.Pointer != _transferConfiguredHost)
            {
                host.SimulateLatency = _transferRtt != 0;
                host.SimulationMinLatency = host.SimulationMaxLatency = _transferRtt / 2;
                host.SimulatePacketLoss = _transferLoss != 0; host.SimulationPacketLossChance = _transferLoss;
                host.EnableStatistics = true;
                _transferConfiguredHost = host.Pointer;
            }
            _transferReceived = Convert.ToInt64(TransferStatic(TransferType, "_receivedBytes"));
            _transferRequested = Convert.ToInt64(TransferStatic(TransferType, "_totalBytes"));
            _transferPeakReceived = Math.Max(_transferPeakReceived, _transferReceived);
            _transferPeakRequested = Math.Max(_transferPeakRequested, _transferRequested);
            if (_transferReceived > 0 && _transferFirstByteAt == 0) _transferFirstByteAt = now;
            _transferLaneMask = Convert.ToUInt64(TransferStatic(TransferType, "_selectedLaneMask"));
            if (TransferStatic(TransferType, "_releasedFrames") is uint[] released)
            {
                _transferDomainsUsed = Math.Max(_transferDomainsUsed, released.Count(count => count != 0));
                _transferPeakConsumedFrames = Math.Max(_transferPeakConsumedFrames, released.Sum(count => (long)count));
            }
            string state = TransferStatic(TransferType, "_state")?.ToString();
            string previousState = _transferLastObservedState;
            _transferLastObservedState = state;
            if (state == "Queued" && _transferQueueEnteredAt == 0)
            {
                if (_transferAdmittedAt != 0) throw new InvalidOperationException("Native file transfer re-entered the queue after observed admission.");
                _transferQueueEnteredAt = now;
                WriteNetworkTransferQueueEvidence(now, state);
            }
            if (_transferAdmittedAt == 0 && (state == "Receiving" || state == "Complete" || _transferReceived > 0))
            {
                _transferAdmittedAt = now;
                WriteNetworkTransferQueueEvidence(now, state);
            }
            double queueWaitSeconds = _transferQueueEnteredAt == 0 ? 0 :
                TransferSeconds(_transferQueueEnteredAt, _transferAdmittedAt == 0 ? now : _transferAdmittedAt);
            if (_transferMaxQueueSeconds.HasValue && _transferQueueEnteredAt != 0 && queueWaitSeconds > _transferMaxQueueSeconds.Value)
                throw new TimeoutException("Observed native queue wait exceeded the explicitly configured acceptance bound.");
            if ((state == "Receiving" || state == "Preparing" || state == "AwaitingAdmission" || state == "Queued") &&
                (ApiClient.IsClientReady || Il2CppMirror.NetworkClient.ready))
                throw new InvalidOperationException("Ready escaped while file acquisition was incomplete.");
            object worker = TransferStatic(TransferType, "_worker");
            long pendingBytes = 0, pendingFrames = 0;
            if (worker != null)
            {
                lock (TransferRead(worker, "_gate"))
                {
                    pendingBytes = Convert.ToInt64(TransferRead(worker, "_pendingBytes"));
                    pendingFrames = Convert.ToInt64(TransferRead(worker, "_pendingFrames"));
                }
            }
            _transferPeakPendingBytes = Math.Max(_transferPeakPendingBytes, pendingBytes);
            _transferPeakPendingFrames = Math.Max(_transferPeakPendingFrames, pendingFrames);
            if (PollNativeTransferCancellation(now, state)) return;
            if (ApiClient.IsClientReady && _transferReadyAt == 0) _transferReadyAt = now;
            ObserveNetworkTransferPositiveTerminal(now, state, previousState);
            ObserveNetworkTransferAdmissionRejection(now);
            if ((now - _transferLastSample) / (double)Stopwatch.Frequency < .25) return;
            _transferLastSample = now;
            var peer = host?.FirstPeer;
            var sample = new { utc = DateTime.UtcNow, state, receivedBytes = _transferReceived, requestedBytes = _transferRequested,
                queueEnteredTimestamp = _transferQueueEnteredAt == 0 ? (long?)null : _transferQueueEnteredAt,
                admittedTimestamp = _transferAdmittedAt == 0 ? (long?)null : _transferAdmittedAt,
                queueWaitSeconds, minimumQueueAcceptanceSeconds = _transferMinQueueSeconds, maximumQueueAcceptanceSeconds = _transferMaxQueueSeconds,
                pendingBytes, pendingFrames, laneMask = _transferLaneMask, channels = host?.ChannelsCount,
                updateTimeMs = host?.UpdateTime, mtu = peer?.Mtu, ping = peer?.Ping, roundTripTimeMs = peer?.RoundTripTime,
                nativeReady = Il2CppMirror.NetworkClient.ready,
                ready = ApiClient.IsClientReady, scriptsPrepared = RemotePrepared,
                domainReleasedFrames = TransferStatic(TransferType, "_releasedFrames"), creditSequence = TransferStatic(TransferType, "_creditSequence") };
            File.AppendAllText(Path.Combine(_output, "transfer-client.jsonl"), JsonSerializer.Serialize(sample) + "\n");
        }
        catch (Exception error) { Fail("Transfer observer: " + error); }
    }

    private void ObserveNetworkTransferPositiveTerminal(long now, string state, string previousState)
    {
        if (_transferExpectBudgetRejection || _transferExpectAdmissionRejection || IsIntentionalNativeTransferCancellation ||
            _transferConsentAt == 0 || _transferReadyAt != 0) return;
        if (_transferSawConnected && !ApiClient.IsConnected)
        {
            WriteNetworkTransferFailureContext("post-consent-disconnect-before-ready", now, state, previousState);
            throw new InvalidOperationException("Positive transfer disconnected after consent before Ready; see transfer-terminal-context.json and correlate the server rejection reason.");
        }
        if (_transferMaxConsentReadySeconds.HasValue && TransferSeconds(_transferConsentAt, now) > _transferMaxConsentReadySeconds.Value)
        {
            WriteNetworkTransferFailureContext("consent-to-ready-acceptance-deadline", now, state, previousState);
            throw new TimeoutException("Positive transfer exceeded the configured consent-to-Ready acceptance bound while waiting for Ready.");
        }
    }

    private void WriteNetworkTransferFailureContext(string kind, long now, string state, string previousState)
    {
        File.WriteAllText(Path.Combine(_output, "transfer-terminal-context.json"), JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, kind, monotonicTimestamp = now, stopwatchFrequency = Stopwatch.Frequency,
            sessionId = _transferManifest?.SessionId, currentObservedState = state, previousObservedState = previousState,
            consentTimestamp = _transferConsentAt == 0 ? (long?)null : _transferConsentAt,
            consentToFailureSeconds = TransferSeconds(_transferConsentAt, now), maximumConsentToReadySeconds = _transferMaxConsentReadySeconds,
            peakRequestedBytes = _transferPeakRequested, peakConsumedBytes = _transferPeakReceived,
            peakConsumedFrames = _transferPeakConsumedFrames, peakPendingBytes = _transferPeakPendingBytes,
            peakPendingFrames = _transferPeakPendingFrames, domainsUsed = _transferDomainsUsed,
            queueEnteredTimestamp = _transferQueueEnteredAt == 0 ? (long?)null : _transferQueueEnteredAt,
            admittedTimestamp = _transferAdmittedAt == 0 ? (long?)null : _transferAdmittedAt,
            connected = ApiClient.IsConnected, ready = ApiClient.IsClientReady, nativeReady = Il2CppMirror.NetworkClient.ready,
            stockLastDisconnectReason = LiteNetLib4MirrorClient.LastDisconnectReason,
            scriptsPrepared = RemotePrepared, note = "Stock decoded disconnect reason is captured when available. Correlate this exact offered session and reason with server admission-rejections.jsonl; CPU attribution additionally requires the measured capacity snapshot." }));
    }
}
