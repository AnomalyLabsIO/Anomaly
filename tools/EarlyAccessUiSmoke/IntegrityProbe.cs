using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppLiteNetLib.Utils;
using Il2CppTMPro;
using UnityEngine;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private void EnqueueIntegrityProbe(string mode)
    {
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (string.IsNullOrEmpty(server))
            throw new InvalidOperationException("Integrity qualification requires an isolated loopback server.");
        bool reject = mode.EndsWith("rejected", StringComparison.Ordinal);
        bool late = mode == "late";
        byte[] previousNonce = null;
        Add("integrity-prepare", () =>
        {
            if (Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy)
            {
                Input().SetTextWithoutNotify("Integrity Smoke");
                Submit();
            }
            WriteIntegrityEvidence("fresh-process");
            Require((ReadGuardFlags() & 15) == 0, "Fresh process has no risky evidence before fixture modification");
            if (mode.StartsWith("gap-", StringComparison.Ordinal))
                GuardType().GetMethod("ReportCoverageGap", All).Invoke(null, new object[] { "Isolated smoke required-target coverage failure" });
            else if (mode != "clean" && !late)
                ApplyIntegrityFixture(expectBlocked: mode == "blocked");
            WriteIntegrityEvidence("before-connect");
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("integrity-admission", () =>
        {
            AcceptIntegrityTrust();
            if (!reject && IntegrityNoticeVisible())
            {
                WriteIntegrityEvidence("unexpected-integrity-rejection");
                throw new InvalidOperationException("A connection expected to succeed was rejected with AUTH-INTEGRITY.");
            }
            return reject ? IntegrityNoticeVisible() :
                ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId);
        }, 90);
        Add("integrity-admission-result", () =>
        {
            if (reject)
            {
                Require(!ApiClient.IsConnected && ApiClient.EnrollmentId == null,
                    "Integrity rejection leaves no enrolled connection");
                File.WriteAllText(Path.Combine(_output, "integrity-notice.txt"),
                    ((TMP_Text)Instance().GetType().GetField("_details", All).GetValue(Instance())).text);
            }
            else
                Require(ApiClient.IsClientReady && ApiClient.EnrollmentId != null,
                    "Server accepted authenticated inventory and audit");
            if (late)
            {
                previousNonce = ReadHandshakeNonce();
                ApplyIntegrityFixture(expectBlocked: false);
            }
            WriteIntegrityEvidence("admission-result");
        });
        if (late)
        {
            AddWait("integrity-late-disconnect", () => !ApiClient.IsConnected && IntegrityNoticeVisible(), 15);
            Add("integrity-fresh-connect", () =>
            {
                Call("HideAll");
                ApiClient.MainMenu.ChangeMenu(0);
                ApiClient.Connect(server);
            });
            AddWait("integrity-fresh-admission", () =>
            {
                AcceptIntegrityTrust();
                return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId);
            }, 90);
            Add("integrity-fresh-nonce", () =>
            {
                Require(!previousNonce.SequenceEqual(ReadHandshakeNonce()),
                    "Changed admission uses a fresh handshake nonce");
                WriteIntegrityEvidence("fresh-admission");
            });
        }
        Add("integrity-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Integrity smoke passed: " + mode + "\n");
            ApiClient.Disconnect();
            Application.Quit();
        });
    }

    private void ApplyIntegrityFixture(bool expectBlocked)
    {
        var target = AccessTools.DeclaredMethod(typeof(NetDataWriter), nameof(NetDataWriter.PutBytesWithLength),
            new[] { typeof(Il2CppStructArray<byte>) });
        if (target == null) throw new MissingMethodException("PutBytesWithLength fixture target is unavailable.");
        var prefix = typeof(Core).GetMethod(nameof(IntegrityNoOpPrefix), All);
        HarmonyInstance.Patch(target, prefix: new HarmonyMethod(prefix));
        bool present = HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Any(patch => patch.PatchMethod == prefix) == true;
        Require(present != expectBlocked, expectBlocked ? "External patch was blocked" : "External patch was applied and audited");
        ushort flags = ReadGuardFlags();
        Require((flags & (expectBlocked ? 16 : 1)) != 0, "The corresponding audit flag was recorded");
    }

    // Deliberately changes no bytes: the fixture exercises patch admission itself.
    private static void IntegrityNoOpPrefix() { }

    private static Type GuardType() => Type.GetType("Anomaly.Bootstrap.RuntimeGuard, Anomaly.Bootstrap", true);
    private static object GuardSnapshot() => GuardType().GetProperty("Snapshot", All).GetValue(null);
    private static ushort ReadGuardFlags()
    {
        object snapshot = GuardSnapshot();
        return (ushort)snapshot.GetType().GetProperty("Flags", All).GetValue(snapshot);
    }

    private void WriteIntegrityEvidence(string phase)
    {
        object snapshot = GuardSnapshot();
        var diagnostics = (string[])GuardType().GetProperty("Diagnostics", All).GetValue(null);
        File.WriteAllLines(Path.Combine(_output, "integrity-diagnostics.txt"), diagnostics);
        File.AppendAllText(Path.Combine(_output, "integrity.log"),
            phase + " flags=" + ReadGuardFlags() +
            " generation=" + snapshot.GetType().GetProperty("Generation", All).GetValue(snapshot) +
            " healthy=" + snapshot.GetType().GetProperty("Healthy", All).GetValue(snapshot) +
            " allowExternal=" + snapshot.GetType().GetProperty("AllowExternalModifications", All).GetValue(snapshot) + "\n");
    }

    private static byte[] ReadHandshakeNonce()
    {
        var handler = Type.GetType("Anomaly.Client.Auth.AuthHandler, Anomaly.Client", true);
        var connections = (System.Collections.IDictionary)handler.GetField("_connections", All).GetValue(null);
        object connection = connections.Values.Cast<object>().Single();
        object session = connection.GetType().GetProperty("Session", All).GetValue(connection);
        object handshake = session.GetType().GetMethod("CreateHandshake", All).Invoke(session, new object[] { null });
        return ((byte[])handshake.GetType().GetField("nonce", All).GetValue(handshake)).ToArray();
    }

    private bool IntegrityNoticeVisible() => Instance() != null && IsVisible() &&
        ((TMP_Text)Instance().GetType().GetField("_details", All).GetValue(Instance())).text.Contains("AUTH-INTEGRITY", StringComparison.Ordinal);

    private void AcceptIntegrityTrust()
    {
        if (Instance() == null || !IsVisible()) return;
        var label = (TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance());
        if (label.text == "Trust and connect") Submit();
    }
}
