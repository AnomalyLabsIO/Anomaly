using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ApiClient = Anomaly.Client.Api.Features.Client;

namespace Anomaly.EarlyAccessUiSmoke;

public sealed partial class Core
{
    private void EnqueueAdmissionProbe()
    {
        var server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        var expected = Environment.GetEnvironmentVariable("ANOMALY_MOD_ADMISSION_EXPECT");
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(expected))
            throw new InvalidOperationException("Admission probe requires a loopback server and expected notice text.");

        var lastTrustPresentation = 0L;
        var trustActionAt = 0d;
        var visibleUntil = 0d;
        var noticePresentation = 0L;
        Add("admission-connect", () =>
        {
            ApiClient.MainMenu.ChangeMenu(0);
            ApiClient.Connect(server);
        });
        AddWait("admission-rejection-notice", () =>
        {
            if (Instance() == null || !IsVisible()) return false;
            var prompt = Instance();
            var label = (TMP_Text)prompt.GetType().GetField("_acceptLabel", All).GetValue(prompt);
            if (label.text == "Trust and connect")
            {
                var presentation = (long)prompt.GetType().GetField("_presentation", All).GetValue(prompt);
                if (lastTrustPresentation != presentation)
                {
                    lastTrustPresentation = presentation;
                    trustActionAt = Time.realtimeSinceStartupAsDouble + 2;
                }
                if (Time.realtimeSinceStartupAsDouble >= trustActionAt) Submit();
                return false;
            }
            var details = ((TMP_Text)prompt.GetType().GetField("_details", All).GetValue(prompt)).text;
            return details.Contains(expected, StringComparison.Ordinal);
        }, 90);
        Add("admission-rejection-capture", () =>
        {
            var prompt = Instance();
            var details = ((TMP_Text)prompt.GetType().GetField("_details", All).GetValue(prompt)).text;
            File.WriteAllText(Path.Combine(_output, "admission-notice.txt"), details);
            Require(!ApiClient.IsConnected && !ApiClient.IsClientReady && ApiClient.EnrollmentId == null,
                "Phase-two rejection leaves no active Anomaly connection or enrollment");
            noticePresentation = (long)prompt.GetType().GetField("_presentation", All).GetValue(prompt);
            visibleUntil = Time.realtimeSinceStartupAsDouble + 6;
            Capture("admission-rejection");
        });
        AddWait("admission-notice-persistence-delay", () => Time.realtimeSinceStartupAsDouble >= visibleUntil, 10);
        Add("admission-notice-dismiss", () =>
        {
            Require(IsVisible() &&
                (long)Instance().GetType().GetField("_presentation", All).GetValue(Instance()) == noticePresentation,
                "Admission notice remains visible after phase-two disconnect cleanup");
            PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
            Require(!IsVisible(), "Admission notice closes on explicit dismissal");
        });
        Add("admission-complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), "Admission rejection and notice persistence passed.\n");
            Application.Quit();
        });
    }
}
