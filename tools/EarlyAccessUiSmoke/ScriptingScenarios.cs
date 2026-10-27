using System.Collections;
using System.Text.Json;
using Il2CppTMPro;
using UnityEngine;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private object _scriptingTrafficSession;
    private int _scriptingTrafficFrames;

    private static TMP_Text[] ScriptOutputLabels() => Resources.FindObjectsOfTypeAll<TMP_Text>()
        .Where(text => text != null && text.gameObject.activeInHierarchy &&
            text.gameObject.name.StartsWith("Anomaly.ScriptOutput.", StringComparison.Ordinal)).ToArray();

    private static object ScriptingRuntime() => Type.GetType("Anomaly.Client.Internal.Bootstrap.ScriptingBootstrap, Anomaly.Client", true)
        .GetField("_runtime", All).GetValue(null);

    private static object ScriptSession(string owner)
    {
        var runtime = ScriptingRuntime();
        var sessions = (IEnumerable)runtime.GetType().GetField("_sessions", All).GetValue(runtime);
        var identity = "anomaly_smoke:" + owner + " mods/anomaly_smoke/" + owner + "/scripts/main.lua";
        return sessions.Cast<object>().Single(session => string.Equals((string)session.GetType()
            .GetProperty("SourceIdentity", All).GetValue(session), identity, StringComparison.Ordinal));
    }

    private static object InvokeScript(object session, string function, params object[] arguments)
    {
        var result = session.GetType().GetMethod("Invoke", All).Invoke(session, new object[] { function, arguments });
        if (!(bool)result.GetType().GetProperty("Succeeded").GetValue(result))
            throw new InvalidOperationException("Script entry failed: " + result);
        return result.GetType().GetProperty("Value").GetValue(result);
    }

    private static bool ScriptRetired(object session) => (bool)session.GetType().GetProperty("IsRetired", All).GetValue(session);

    private void TickClientScriptingSmoke()
    {
        if (_scriptingTrafficFrames == 0 || _failed) return;
        try
        {
            Require(!ScriptRetired(_scriptingTrafficSession), "Ordinary native-query callbacks remain healthy");
            var queue = _scriptingTrafficSession.GetType().GetMethod("QueueEvent", All);
            for (var index = 0; index < 2; index++)
                Require((bool)queue.Invoke(_scriptingTrafficSession, new object[] { "ReadGame", Array.Empty<object>() }),
                    "Native-query observation was queued");
            _scriptingTrafficFrames--;
        }
        catch (Exception exception) { Fail("Client scripting traffic: " + exception); }
    }

    private void EnqueueClientScriptingSmoke(string entry)
    {
        AddWait("scripting-sample-output-" + entry, () =>
        {
            SaveScriptingState(entry);
            return ScriptOutputLabels().Any(text => text.text.StartsWith("Anomaly Lua ready\n", StringComparison.Ordinal));
        }, 15);
        Add("scripting-sample-capture-" + entry, () =>
        {
            Require((bool)InvokeScript(ScriptSession("owner-a"), "ReadGame"), "Lua resolves the actual local player and detached native position");
            Require(!Anomaly.Client.Api.Scripting.ScriptingPolicy.MemoryHardeningQualified,
                "Live local execution does not qualify an in-process heap ceiling");
            Require(Anomaly.Client.Api.Scripting.ScriptingPolicy.RemoteExecutionEnabled ==
                Anomaly.Client.Api.Scripting.ScriptingPolicy.RemoteIsolationQualified,
                "Remote availability follows independent worker isolation qualification");
            Require(_readyEvents == (entry == "console" ? 1 : 2), "One public Ready transition per hosted connection");
            Capture("scripting-local-sample-" + entry);
            _scriptingTrafficSession = ScriptSession("owner-a");
            _scriptingTrafficFrames = 120;
        });
        AddWait("scripting-sustained-native-queries-" + entry, () => _scriptingTrafficFrames == 0, 30);
        Add("scripting-owned-text-" + entry, () =>
        {
            Require(!ScriptRetired(_scriptingTrafficSession), "Two native-query callbacks per frame do not retire a healthy session");
            Require((bool)InvokeScript(ScriptSession("owner-a"), "ShowOwner", "owner A\n<color=red>literal</color>", 10d), "Owner A can show bounded text");
            Require((bool)InvokeScript(ScriptSession("owner-b"), "ShowOwner", "owner B remains independent", 10d), "Owner B can show bounded text");
        });
        Add("scripting-owned-capture-" + entry, () =>
        {
            var first = ScriptOutputLabels().Single(text => text.text.StartsWith("owner A\n", StringComparison.Ordinal));
            var second = ScriptOutputLabels().Single(text => text.text == "owner B remains independent");
            Require(!first.richText && !second.richText && !first.raycastTarget && !second.raycastTarget,
                "Script output is literal plain text and cannot capture input");
            Require(first.text.Contains("<color=red>literal</color>", StringComparison.Ordinal), "Markup stays literal in actual TMP text");
            Require(Vector3.Distance(first.transform.position, second.transform.position) > 1, "Independent owners use separate screen positions");
            foreach (var label in ScriptOutputLabels())
                Require(Contains(new Rect(0, 0, Screen.width, Screen.height), NewsScreenBounds(label.rectTransform), 1),
                    "Owned output rectangle stays inside the actual scaled canvas");
            Capture("scripting-owned-text-" + entry);
        });
        Add("scripting-owner-clear-" + entry, () =>
        {
            InvokeScript(ScriptSession("owner-a"), "ClearOwner");
            Require(!ScriptOutputLabels().Any(text => text.text.StartsWith("owner A\n", StringComparison.Ordinal)), "Clear removes only owner A");
            Require(ScriptOutputLabels().Any(text => text.text == "owner B remains independent"), "Owner A clear preserves owner B");
            Require((bool)InvokeScript(ScriptSession("owner-b"), "ShowOwner", "owner B expires", 1d), "Owner B can replace its own text");
            var runtime = ScriptingRuntime();
            File.WriteAllText(Path.Combine(_output, "scripting-memory-" + entry + ".json"), JsonSerializer.Serialize(new
            {
                exercisedFrames = 120,
                queuedCallbacksPerFrame = 2,
                chargedEstimateBytes = runtime.GetType().GetProperty("MemoryChargedBytes", All).GetValue(runtime),
                totalReservedBytes = runtime.GetType().GetProperty("MemoryTotalAllocatedBytes", All).GetValue(runtime),
                remoteExecutionEnabled = Anomaly.Client.Api.Scripting.ScriptingPolicy.RemoteExecutionEnabled,
                notice = "Live native-query/resource lifecycle smoke, not a qualified CLR/native heap ceiling."
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
        AddWait("scripting-owner-expiry-" + entry, () => !ScriptOutputLabels().Any(text => text.text == "owner B expires"), 10);
        Add("scripting-expiry-capture-" + entry, () => Capture("scripting-owned-expired-" + entry));
    }

    private void SaveScriptingState(string entry)
    {
        var bootstrap = Type.GetType("Anomaly.Client.Internal.Bootstrap.ScriptingBootstrap, Anomaly.Client", true);
        var scripts = bootstrap.GetField("_localScripts", All).GetValue(null);
        var hosting = Type.GetType("Anomaly.Client.Internal.Hosting.LocalHostingRuntime, Anomaly.Client", true);
        File.WriteAllText(Path.Combine(_output, "scripting-state-" + entry + ".json"), JsonSerializer.Serialize(new
        {
            publicReady = Anomaly.Client.Api.Features.Client.IsClientReady,
            nativeReady = Il2CppMirror.NetworkClient.ready,
            nativeConnected = Il2CppMirror.NetworkClient.isConnected,
            activeHost = Il2CppMirror.NetworkClient.activeHost,
            verifiedHost = hosting.GetProperty("IsVerifiedScriptHost", All).GetValue(null),
            prepared = bootstrap.GetProperty("IsConnectionPrepared", All).GetValue(null),
            scriptSetPresent = scripts != null,
            remoteScriptSetPresent = bootstrap.GetField("_remoteScripts", All).GetValue(null) != null,
            sourcePresent = File.Exists(Path.Combine(Anomaly.Client.Api.Features.Paths.ContentPacksPath,
                "example", "panel", "scripts", "main.lua")),
            labelTexts = ScriptOutputLabels().Select(text => text.text).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
