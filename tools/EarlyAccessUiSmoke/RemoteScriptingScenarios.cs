using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Api.Networking;
using Anomaly.ServerApiSmoke;
using Il2CppTMPro;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private bool _remoteScriptingSmoke;
    private bool _remoteWatch;
    private int _remoteInitializingFrames;
    private bool _remoteContentConsent, _remoteExecutionConsent;
    private string _remotePrompt;
    private double _remoteDecisionAt;
    private ulong _remoteActivation, _remotePreviousActivation;
    private Process _remoteProcess;
    private int _remotePreviousPid;
    private object _remoteActor;
    private readonly List<object> _remoteAdmissions = new();
    private static Type ScriptBootstrap => Type.GetType("Anomaly.Client.Internal.Bootstrap.ScriptingBootstrap, Anomaly.Client", true);
    private static object RemoteField(object owner, string name) => owner.GetType().GetField(name, All).GetValue(owner);
    private static object RemoteProperty(object owner, string name) => owner.GetType().GetProperty(name, All).GetValue(owner);
    private static bool RemotePrepared => (bool)ScriptBootstrap.GetProperty("IsConnectionPrepared", All).GetValue(null);

    private void TickRemoteScriptingSmoke()
    {
        if (!_remoteScriptingSmoke || !_remoteWatch || _failed) return;
        try
        {
            var initializing = ScriptBootstrap.GetField("_initializingRemote", All).GetValue(null);
            if (initializing == null || !(bool)RemoteProperty(initializing, "IsInitializing")) return;
            _remoteInitializingFrames++;
            if (Il2CppMirror.NetworkClient.ready || ApiClient.IsClientReady || RemotePrepared)
                throw new InvalidOperationException("Native/public Ready escaped while the required remote set was still loading.");
        }
        catch (Exception error) { Fail("Remote admission observation: " + error); }
    }

    private void EnqueueRemoteScriptingSmoke()
    {
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var endpoint) ||
            !System.Net.IPAddress.TryParse(endpoint.Host, out var address) || !System.Net.IPAddress.IsLoopback(address))
            throw new InvalidOperationException("RemoteScripting requires an explicit loopback server.");
        Add("remote-nickname", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Remote Lua Smoke"); Submit();
                Require(!IsVisible(), "Nickname was saved without importing another profile");
            }
        });
        AddWait("remote-worker-qualified", () => ScriptingPolicy.RemoteIsolationQualified, 40);
        Add("remote-native-string-preflight", ProbeNativeStringPreflight);
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            int current = cycle;
            Add("remote-connect-" + current, () =>
            {
                _remoteContentConsent = _remoteExecutionConsent = false;
                _remotePrompt = null;
                _remoteInitializingFrames = 0;
                _remoteWatch = true;
                ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server);
            });
            AddWait("remote-ready-" + current, () => PumpRemoteConsent(current), 150);
            Add("remote-isolation-" + current, () => CaptureRemoteIsolation(current));
            AddRemoteCommand("query", current, 0);
            if (current == 1)
            {
                AddRemoteCommand("show", current, 0);
                Add("remote-literal-owned-text", () =>
                {
                    var first = ScriptOutputLabels().Single(text => text.text == "remote owner A\n<color=red>literal</color>");
                    var second = ScriptOutputLabels().Single(text => text.text == "remote owner B remains independent");
                    Require(!first.richText && !second.richText && !first.raycastTarget && !second.raycastTarget,
                        "Remote output is literal TMP text and never captures input");
                    Require(Vector3.Distance(first.transform.position, second.transform.position) > 1,
                        "Independent remote script owners have independent display slots");
                    Capture("remote-literal-owned-text");
                });
                AddRemoteCommand("clear", current, 0);
                Add("remote-owned-clear", () =>
                {
                    Require(!ScriptOutputLabels().Any(text => text.text.StartsWith("remote owner A", StringComparison.Ordinal)), "Remote ClearText clears its owner");
                    Require(ScriptOutputLabels().Any(text => text.text == "remote owner B remains independent"), "Remote ClearText preserves another owner");
                    Capture("remote-owned-clear");
                });
                AddRemoteCommand("expire", current, 0);
                AddWait("remote-owned-expiry", () => ScriptOutputLabels().Length == 0, 12);
                Add("remote-expiry-capture", () => Capture("remote-owned-expired"));
                for (int batch = 1; batch <= 6; batch++) AddRemoteCommand("churn", current, batch * 8);
            }
            Add("remote-disconnect-" + current, () =>
            {
                _remoteWatch = false;
                _remotePreviousActivation = _remoteActivation;
                _remotePreviousPid = _remoteProcess.Id;
                ApiClient.Disconnect();
            });
            AddWait("remote-job-killed-" + current, () =>
                _remoteProcess.HasExited && !ApiClient.IsConnected && ApiClient.MainMenu != null, 40);
            Add("remote-revoked-proof-" + current, () =>
            {
                Require(ScriptBootstrap.GetField("_remoteScripts", All).GetValue(null) == null && !RemotePrepared,
                    "Connection revoke removes the remote actor before worker cleanup");
                Require(ScriptOutputLabels().Length == 0, "Connection revoke clears every remote owned output");
                WriteRemoteEvidence("remote-revoke-" + current, new
                {
                    activationId = _remotePreviousActivation, processId = _remotePreviousPid,
                    exited = _remoteProcess.HasExited, exitCode = _remoteProcess.ExitCode,
                    prepared = RemotePrepared, labelCount = ScriptOutputLabels().Length
                });
                _remoteProcess.Dispose(); _remoteProcess = null; _remoteActor = null;
            });
        }
        Add("remote-complete", () =>
        {
            WriteRemoteEvidence("remote-result", new { passed = true, cycles = 2, churnQueries = 48, admissions = _remoteAdmissions, screenshots = _captures,
                notice = "Real authenticated dedicated-server transfer, consent, worker job, native API and IL2CPP conversion proof. Not a native-allocation bound or visual certification." });
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Remote scripting smoke passed; two authenticated activations, isolated workers, native queries, frozen Ready, typed GetState/State and connection revoke observed. Inspect screenshots for visual certification.\n");
            Application.Quit();
        });
    }

    private bool PumpRemoteConsent(int cycle)
    {
        if (Instance() != null && IsVisible())
        {
            string label = ((TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance())).text;
            if (label == "Trust and connect") { Capture("remote-trust-" + cycle); Submit(); return false; }
            if (label == "Allow content" || label == "Run scripts")
            {
                Require(!ApiClient.IsClientReady && !Il2CppMirror.NetworkClient.ready && !RemotePrepared,
                    "Content and execution consent each hold native/public Ready");
                if (_remotePrompt != label)
                {
                    _remotePrompt = label; _remoteDecisionAt = Time.realtimeSinceStartupAsDouble + 3;
                    if (label == "Allow content") _remoteContentConsent = true;
                    else _remoteExecutionConsent = true;
                    Capture("remote-" + (label == "Allow content" ? "content-consent-" : "execution-consent-") + cycle);
                }
                if (Time.realtimeSinceStartupAsDouble >= _remoteDecisionAt) Submit();
                return false;
            }
        }
        return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId) && Il2Cpp.StaticUnityMethods.IsPlaying;
    }

    private void CaptureRemoteIsolation(int cycle)
    {
        Require(ScriptingPolicy.RemoteIsolationQualified && ScriptingPolicy.RemoteExecutionEnabled && !ScriptingPolicy.MemoryHardeningQualified,
            "Actual worker qualification enables remote execution independently of the unqualified CLR heap model");
        Require(_remoteExecutionConsent && (cycle != 1 || _remoteContentConsent),
            "First transfer has content consent and every connection has exact-source execution consent");
        Require(_remoteInitializingFrames > 0, "Native/public Ready was observed held during actual asynchronous worker loading");
        Require(_readyEvents == cycle, "Exactly one public Ready event per fully loaded remote connection");
        _remoteActor = ScriptBootstrap.GetField("_remoteScripts", All).GetValue(null);
        Require(_remoteActor != null && (bool)RemoteProperty(_remoteActor, "IsHealthy") && RemotePrepared,
            "Published remote actor is healthy before gameplay Ready");
        var runtime = ScriptingRuntime();
        Require(!((IEnumerable)RemoteField(runtime, "_sessions")).Cast<object>().Any(), "Remote packs allocate no in-process Lua VM");
        Require(ScriptBootstrap.GetField("_localScripts", All).GetValue(null) == null, "Remote connection never uses the local script set");
        _remoteActivation = (ulong)RemoteField(_remoteActor, "_activation");
        Require(_remoteActivation != 0 && _remoteActivation != _remotePreviousActivation, "Reconnect binds a fresh authenticated activation");
        object channel = RemoteField(_remoteActor, "_channel");
        object process = RemoteProperty(channel, "Process");
        var hosts = ((IEnumerable)RemoteField(_remoteActor, "_hosts")).Cast<object>().ToArray();
        Require(hosts.Length == 2, "Exact two-pack remote fixture inventory was fully loaded");
        var sources = new List<object>();
        foreach (var host in hosts)
        {
            string packId = (string)RemoteField(host, "_packId");
            string expectedVariable = packId switch
            {
                "anomaly_smoke:remote_scripts" => "ANOMALY_REMOTE_SCRIPTING_PRIMARY_SHA256",
                "anomaly_smoke:remote_owner_scripts" => "ANOMALY_REMOTE_SCRIPTING_OWNER_SHA256",
                _ => throw new InvalidOperationException("Unexpected remote fixture component: " + packId)
            };
            object data = RemoteProperty(host, "LoadData");
            object source = data.GetType().GetMethod("Required", All).Invoke(data, new object[] { "source" });
            string text = (string)RemoteProperty(source, "Text");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            string sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            Require(sha256 == Environment.GetEnvironmentVariable(expectedVariable), "Authenticated loaded Lua bytes match the exact fixture source: " + packId);
            sources.Add(new { packId, sourceBytes = bytes.Length, sha256, indexSha256 = RemoteField(host, "_indexSha256") });
        }
        int pid = (int)RemoteProperty(process, "ProcessId");
        Require(pid != Environment.ProcessId && pid != _remotePreviousPid, "Each connection executes in a fresh out-of-process worker");
        _remoteProcess = Process.GetProcessById(pid);
        _ = _remoteProcess.Handle; // Retain the OS handle so exit status remains observable after PID removal.
        Require(!_remoteProcess.HasExited, "Observed worker process is alive");
        object evidence = RemoteProperty(process, "Evidence");
        var isolation = new Dictionary<string, object>();
        foreach (string name in new[] { "AppContainer", "LessPrivilegedAppContainer", "LoopbackExempt", "NetworkCapabilities", "ProcessMemoryLimitBytes", "JobMemoryLimitBytes", "CpuRatePercent", "ActiveProcessLimit", "PeakProcessMemoryBytes" })
            isolation.Add(name, RemoteProperty(evidence, name));
        Require((bool)isolation["AppContainer"] && (bool)isolation["LessPrivilegedAppContainer"] &&
            !(bool)isolation["LoopbackExempt"] && Convert.ToInt32(isolation["NetworkCapabilities"]) == 0 &&
            Convert.ToInt32(isolation["ActiveProcessLimit"]) == 1, "Worker has actual zero-network LPAC and single-process job evidence");
        var admission = new { cycle, activationId = _remoteActivation, processId = pid, initializingFrames = _remoteInitializingFrames,
            contentConsent = _remoteContentConsent, executionConsent = _remoteExecutionConsent, publicReadyEvents = _readyEvents,
            nativeReady = Il2CppMirror.NetworkClient.ready, publicReady = ApiClient.IsClientReady, isolation, sources };
        _remoteAdmissions.Add(admission);
        WriteRemoteEvidence("remote-admission-" + cycle, admission);
        Capture("remote-connected-" + cycle);
    }

    private void AddRemoteCommand(string command, int cycle, int churnCount)
    {
        int token = 0;
        uint expected = 0;
        string name = "remote-" + cycle + "-" + command + "-" + churnCount;
        Add(name + "-send", () =>
        {
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = "remote:" + command });
        });
        AddWait(name + "-roundtrip", () =>
        {
            if (!_smokeResponses.TryGetValue(token, out var response)) return false;
            _smokeResponses.Remove(token);
            Require(response.Details.Length <= 8192, "Server proof JSON is bounded");
            Require(response.Success, "Authenticated fixture reply: " + command + " " + response.Details);
            using var document = JsonDocument.Parse(response.Details);
            var proof = document.RootElement;
            if (expected == 0) expected = proof.GetProperty("ExpectedRequestId").GetUInt32();
            if (proof.TryGetProperty("Last", out var last) && last.ValueKind == JsonValueKind.Object && last.GetProperty("RequestId").GetUInt32() == expected)
            {
                Require(last.GetProperty("Command").GetString() == command && last.GetProperty("Enabled").GetBoolean(), "Exact GetState request reached the real Lua Messages callback");
                Require(last.GetProperty("ReadyCount").GetUInt32() == 1 && last.GetProperty("ReadyFrozen").GetBoolean(), "Retained Client.Ready snapshot is frozen and delivered exactly once");
                Require(!string.IsNullOrEmpty(last.GetProperty("SceneName").GetString()), "Real native scene name crosses the event freezer");
                Require(last.GetProperty("QueryMatches").GetBoolean() && last.GetProperty("PlayerCount").GetInt32() > 0,
                    "Live Game.LocalPlayer, GetPlayer and GetPlayerAt queries resolve actual native players");
                Require(proof.GetProperty("NicknameMatches").GetBoolean() && proof.GetProperty("PositionMatches").GetBoolean(),
                    "Native-backed nickname string and detached X/Y/Z match the actual server player");
                Require(proof.GetProperty("ActivationId").GetUInt64() == _remoteActivation && proof.GetProperty("IndexSha256").GetString()?.Length == 64,
                    "State roundtrip preserves authenticated exact-index activation authority");
                if (command == "churn") Require(last.GetProperty("ChurnQueries").GetUInt32() == churnCount, "Cached native query/proxy churn remains healthy across commands");
                WriteRemoteEvidence(name, proof);
                return true;
            }
            token = ++_requestToken;
            AnomalyMessaging.Send(new SmokeRequest { Token = token, Command = "remote:status" });
            return false;
        }, 25);
    }

    private void ProbeNativeStringPreflight()
    {
        Type il2cpp = Type.GetType("Il2CppInterop.Runtime.IL2CPP, Il2CppInterop.Runtime", true);
        MethodInfo allocate = il2cpp.GetMethod("ManagedStringToIl2Cpp", All, null, new[] { typeof(string) }, null);
        MethodInfo convert = il2cpp.GetMethod("Il2CppStringToManaged", All, null, new[] { typeof(IntPtr) }, null);
        MethodInfo retain = il2cpp.GetMethod("il2cpp_gchandle_new", All);
        MethodInfo release = il2cpp.GetMethod("il2cpp_gchandle_free", All);
        string input = new string('N', 4097);
        IntPtr pointer = (IntPtr)allocate.Invoke(null, new object[] { input });
        object handle = retain.Invoke(null, new object[] { pointer, false });
        string exceptionType = null;
        bool outsideSucceeded = false;
        try
        {
            Type access = Type.GetType("Anomaly.Client.Api.Scripting.ScriptHostAccess, Anomaly.Client.Api", true);
            using ((IDisposable)access.GetMethod("Enter", All).Invoke(null, null))
            {
                try { convert.Invoke(null, new object[] { pointer }); }
                catch (TargetInvocationException error) { exceptionType = error.InnerException?.GetType().FullName; }
            }
            Require(exceptionType == "Anomaly.Client.Api.Scripting.ScriptHostAccessException",
                "Patched real IL2CPP native string conversion rejects >4096 UTF16 before managed copy inside ScriptHostAccess");
            outsideSucceeded = string.Equals((string)convert.Invoke(null, new object[] { pointer }), input, StringComparison.Ordinal);
            Require(outsideSucceeded, "The identical real native string converts normally outside the remote host scope");
        }
        finally { release.Invoke(null, new[] { handle }); }
        WriteRemoteEvidence("remote-native-string-preflight", new { utf16Length = input.Length, exceptionType, outsideSucceeded,
            actualConversionEntry = "Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(IntPtr)",
            notice = "Native string allocated before entering scope; proves pre-managed-copy rejection, not a native-allocation bound." });
    }

    private void WriteRemoteEvidence(string name, object value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > 32768) throw new InvalidOperationException("Remote smoke JSON evidence bound exceeded.");
        File.WriteAllText(Path.Combine(_output, name + ".json"), json);
    }
}
