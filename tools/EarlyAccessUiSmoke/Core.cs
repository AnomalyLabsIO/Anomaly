using System.Reflection;
using System.Collections;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.UI;
using Anomaly.Client.Api.UI.ModSettings;
using Il2CppTMPro;
using Il2CppUserSettings.GUIElements;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ApiClient = Anomaly.Client.Api.Features.Client;

[assembly: MelonInfo(typeof(Anomaly.EarlyAccessUiSmoke.Core), "Anomaly UI Smoke", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: MelonAdditionalDependencies("Anomaly.Client", "HelloAnomalyMod")]
[assembly: SupportedGameVersion("14.2.7^")]

namespace Anomaly.EarlyAccessUiSmoke;

// Opt-in test-only MelonMod. Never included in runtime release packages.
public sealed partial class Core : MelonMod
{
    private readonly Queue<(string Name, Action Action)> _steps = new();
    private string _output;
    private double _next;
    private bool _started;
    private bool _failed;
    private int _captures;
    private Func<bool> _wait;
    private double _waitDeadline;
    private string _waitName;
    private MelonPreferences_Entry<int> _quality;
    private string _preferencesPath;
    private int _canvasInstanceId;
    private int _readyEvents;
    private int _disconnectedEvents;
    private int _disconnectedEventsBeforeStop;
    private long _connectionPromptPresentation;
    private double _connectionPromptActionAt;
    private int _accepted;
    private int _cancelled;
    private GameObject _selectionBeforePrompt;
    private bool _movementBeforePrompt;
    private bool _needsNickname;
    private bool _dedicatedDummyControlSmoke;
    private static Type Prompt => Type.GetType("Anomaly.Client.Components.PasswordPrompt, Anomaly.Client", true);
    private static BindingFlags All => BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public override void OnInitializeMelon()
    {
        _output = Environment.GetEnvironmentVariable("ANOMALY_UI_SMOKE_DIR");
        if (string.IsNullOrEmpty(_output)) return;
        Directory.CreateDirectory(_output);
        Application.runInBackground = true;
        FacilityCatalogFailureProbe.Initialize(_output);
        _localHostingSmoke = Environment.GetEnvironmentVariable("ANOMALY_LOCAL_HOSTING_SMOKE") == "1";
        _dummyVisibilitySmoke = Environment.GetEnvironmentVariable("ANOMALY_DUMMY_VISIBILITY_SMOKE") == "1";
        _dedicatedDummyControlSmoke = Environment.GetEnvironmentVariable("ANOMALY_DEDICATED_DUMMY_SMOKE") == "1";
        if (_localHostingSmoke || _dedicatedDummyControlSmoke || FacilityCatalogFailureProbe.Enabled)
        {
            ClientEvents.CanvasReady += OnCanvasReady;
            ClientEvents.Ready += OnReady;
            ClientEvents.Disconnected += OnDisconnected;
            _next = Time.realtimeSinceStartupAsDouble + 90;
            return;
        }
        var category = MelonPreferences.CreateCategory("example_smoke", "Early access UI verification");
        _preferencesPath = Path.Combine(Anomaly.Client.Api.Features.Paths.AnomalyPath, "smoke-preferences.cfg");
        category.SetFilePath(_preferencesPath);
        _quality = category.CreateEntry("quality", 1, "Quality", "A numeric dropdown stores indices even when its labels are words.");
        ModSettings.RegisterEntry(_quality, new ModSettingMetadata { Choices = new[] { "Low", "Medium", "High" }, Group = "Behavior", Order = 0 });
        category.CreateEntry("enabled", true, "Show extra information", "A deliberately long description checks wrapping and row growth without reducing the font size or hiding the controls.");
        category.CreateEntry("nickname", "A very long example preference", "Text preference", "Editing this test preference must not affect the real Anomaly profile.");
        category.SaveToFile(printmsg: false);
        ClientEvents.CanvasReady += OnCanvasReady;
        ClientEvents.Ready += OnReady;
        ClientEvents.Disconnected += OnDisconnected;
        InitializeServerApiSmoke();
        _next = Time.realtimeSinceStartupAsDouble + 90;
    }

    public override void OnDeinitializeMelon()
    {
        ClientEvents.CanvasReady -= OnCanvasReady;
        ClientEvents.Ready -= OnReady;
        ClientEvents.Disconnected -= OnDisconnected;
        ReleaseDummyInputs();
        FacilityCatalogFailureProbe.Shutdown();
        ShutdownServerApiSmoke();
    }

    private void OnReady(ClientReadyEventArgs _) => _readyEvents++;
    private void OnDisconnected(ClientDisconnectedEventArgs _) => _disconnectedEvents++;

    private void OnCanvasReady(CanvasReadyEventArgs _)
    {
        if (_started || string.IsNullOrEmpty(_output)) return;
        _started = true;
        _canvasInstanceId = UiCanvas.Root.GetInstanceID();
        _next = Time.realtimeSinceStartupAsDouble + 8;
        if (FacilityCatalogFailureProbe.Enabled) { EnqueueFacilityCatalogFailureProbe(); return; }
        if (_localHostingSmoke) { EnqueueLocalHostingSmoke(); return; }
        if (_dedicatedDummyControlSmoke) { EnqueueDedicatedDummyControlSmoke(); return; }
        if (_serverApiSmoke) { EnqueueServerApiSmoke(); return; }
        string integrityMode = Environment.GetEnvironmentVariable("ANOMALY_INTEGRITY_SMOKE_MODE");
        if (!string.IsNullOrEmpty(integrityMode)) { EnqueueIntegrityProbe(integrityMode); return; }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANOMALY_MOD_ADMISSION_EXPECT")))
        {
            EnqueueAdmissionProbe();
            return;
        }
        var sizes = Environment.GetEnvironmentVariable("ANOMALY_UI_SMOKE_CONNECTION_ONLY") == "1"
            ? Array.Empty<(int, int)>() : new[] { (1280, 720), (1920, 1080), (3440, 1440) };
        foreach (var size in sizes)
        {
            var width = size.Item1;
            var height = size.Item2;
            Add($"resolution-{width}", () => Screen.SetResolution(width, height, false));
            if (width == 1280)
            {
                Add("first-run", () =>
                {
                    _needsNickname = Instance() != null && IsVisible() && Input().gameObject.activeInHierarchy;
                    Capture(_needsNickname ? "first-run" : "main-menu-existing-profile");
                    if (!_needsNickname)
                        File.AppendAllText(Path.Combine(_output, "steps.log"), "SKIP nickname fixture: existing profile requires no nickname prompt.\n");
                });
                Add("nickname-invalid", () =>
                {
                    if (!_needsNickname) return;
                    Input().SetTextWithoutNotify("");
                    Submit();
                    Require(IsVisible(), "Invalid nickname keeps prompt open");
                    var error = (TMP_Text)Instance().GetType().GetField("_error", All).GetValue(Instance());
                    Require(!string.IsNullOrEmpty(error.text), "Nickname validation is visible");
                });
                Add("nickname-validation", () => { if (_needsNickname) Capture("nickname-validation"); });
                Add("nickname-write-failure", () =>
                {
                    if (!_needsNickname) return;
                    string profile = Path.Combine(Anomaly.Client.Api.Features.Paths.AnomalyPath, "user.json");
                    byte[] before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(profile));
                    Input().SetTextWithoutNotify("Retained after save failure");
                    // Deny deletion/replacement of this isolated test profile while allowing reads.
                    // The production atomic writer must preserve existing bytes on this failure.
                    using (var locked = new FileStream(profile, FileMode.Open, FileAccess.Read, FileShare.Read)) Submit();
                    Require(IsVisible(), "Nickname write failure keeps the prompt open");
                    Require(Input().text == "Retained after save failure", "Nickname input survives a real write failure");
                    Require(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(profile))),
                        "Nickname write failure preserves the existing profile bytes");
                });
                Add("nickname-save-error", () => { if (_needsNickname) Capture("nickname-save-error"); });
                Add("nickname-save", () =>
                {
                    if (!_needsNickname) return;
                    Input().SetTextWithoutNotify("EarlyAccess Smoke");
                    Submit();
                    Require(!IsVisible(), "Successful nickname save closes prompt");
                });
                Add("main-menu", () =>
                {
                    Require(InputRegistry.GetBinding("example.hello.greet") != null, "Packaged ordinary mod registered its input");
                    Capture("main-menu");
                });
            }
            Add("settings-open", ShowSettings);
            Add("settings", () => Capture("settings"));
            Add("settings-focus", () => EventSystem.current.SetSelectedGameObject(QualityDropdown().gameObject));
            Add("settings-rebuild", () => ModSettings.RegisterEntry(_quality, new ModSettingMetadata
            {
                Choices = new[] { "Low", "Medium", "High" }, Group = "Behavior", Order = 0,
                DisplayName = "Quality with a deliberately long translated setting label that must wrap without hiding the controls",
                Description = "Changed while visible — this long translated-style description must wrap, with controls still bound after rebuilding."
            }));
            Add("settings-choice", () =>
            {
                var dropdown = QualityDropdown();
                Require(EventSystem.current.currentSelectedGameObject == dropdown.gameObject,
                    "Settings rebuild restores focus to the replacement control");
                dropdown.value = 2;
                Require(_quality.Value == 2, "Rebuilt numeric dropdown writes selected index");
            });
            Add("settings-rebuilt", () => Capture("settings-rebuilt"));
            Add("settings-reset", () =>
            {
                var row = GameObject.Find("ModSettings.Row.example_smoke.quality");
                var reset = row.GetComponentsInChildren<Button>().First(button => button.name.Contains("Reset"));
                EventSystem.current.SetSelectedGameObject(reset.gameObject);
                reset.OnSubmit(new BaseEventData(EventSystem.current));
                Require(_quality.Value == 1, "Reset restores declared Melon default");
                Require(!reset.IsInteractable(), "Reset becomes unavailable at the default value");
            });
            if (width == 1280)
            {
                Add("settings-write-failure", () =>
                {
                    byte[] before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_preferencesPath));
                    using (var locked = new FileStream(_preferencesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        QualityDropdown().value = 2;
                        SettingsCall("Flush");
                    }
                    Require(!string.IsNullOrEmpty((string)SettingsCall("GetError", _quality.Category)),
                        "A real preference write failure is visible beside the setting");
                    Require((bool)SettingsCall("CanRetry", _quality.Category), "A scheduler-observed write error supports explicit retry");
                    Require(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_preferencesPath))),
                        "Failed preference write preserves existing file bytes");
                });
                Add("settings-save-error-focus", () =>
                {
                    var row = GameObject.Find("ModSettings.Row.example_smoke.quality");
                    var retry = row.GetComponentsInChildren<Button>().Single(button => button.name == "RetrySave");
                    EventSystem.current.SetSelectedGameObject(retry.gameObject);
                });
                Add("settings-save-error", () => Capture("settings-save-error"));
                Add("settings-save-retry", () =>
                {
                    var row = GameObject.Find("ModSettings.Row.example_smoke.quality");
                    var retry = row.GetComponentsInChildren<Button>().Single(button => button.name == "RetrySave");
                    Require(retry.gameObject.activeInHierarchy && retry.IsInteractable(), "Retry save is an available control");
                    EventSystem.current.SetSelectedGameObject(retry.gameObject);
                    retry.OnSubmit(new BaseEventData(EventSystem.current));
                    Require(SettingsCall("GetError", _quality.Category) == null, "Explicit retry clears the write error after storage recovers");
                    Require(File.ReadAllText(_preferencesPath).Contains("quality = 2", StringComparison.Ordinal),
                        "Explicit retry persists the unsaved edited value");
                    QualityDropdown().value = 1;
                });
            }
            Add("trust-open", () =>
            {
                BeginPromptFixture();
                Call("ShowConfirmation", "ui-smoke", "Trust this test server?",
                "UI fixture only — no remote connection.\n\nServer: loopback test\nFingerprint:\n" +
                "01234567 89abcdef 01234567 89abcdef\n01234567 89abcdef 01234567 89abcdef\n\n" +
                "Remembering this key identifies the server on later connections. Confirm only if you expected this server.",
                "Trust and connect", "Cancel", (Action)(() => _accepted++), (Action)(() => _cancelled++));
            });
            Add("trust", () =>
            {
                Require(EventSystem.current.currentSelectedGameObject.name.Contains("Cancel"), "Trust approval starts on Cancel");
                var cursorOwner = (Il2CppCursorManagement.ICursorOverride)Instance().GetType().GetField("_cursorOverride", All).GetValue(Instance());
                Il2CppCursorManagement.CursorManager.ProcessOverrides(out var cursorMode, out var movementRequested);
                File.AppendAllText(Path.Combine(_output, "steps.log"), $"Cursor: registered={Il2CppCursorManagement.CursorManager.Overrides.Contains(cursorOwner)}, interfaceLock={cursorOwner.LockMovement}, interfaceMode={cursorOwner.CursorOverride}, processedMode={cursorMode}, processedLock={movementRequested}, appliedLock={Il2CppCursorManagement.CursorManager.MovementLocked}\n");
                Capture("trust");
                Require(Il2CppCursorManagement.CursorManager.Overrides.Contains(cursorOwner) && cursorOwner.LockMovement,
                    "Modal registers a native cursor owner requesting movement capture");
                Require(cursorOwner.CursorOverride == Il2CppCursorManagement.CursorOverrideMode.Free, "Native cursor interface requests a free cursor");
                Require(!Il2Cpp.StaticUnityMethods.IsPlaying || Il2CppCursorManagement.CursorManager.MovementLocked,
                    "Modal captures movement when the stock manager is processing gameplay overrides");
            });
            Add("trust-navigation-cancel", () =>
            {
                var cancel = PromptButton("_cancel");
                var accept = PromptButton("_accept");
                MoveSelected(MoveDirection.Right, Vector2.right);
                Require(EventSystem.current.currentSelectedGameObject == accept.gameObject,
                    "Synthetic move event navigates Cancel to Trust and connect");
                MoveSelected(MoveDirection.Left, Vector2.left);
                Require(EventSystem.current.currentSelectedGameObject == cancel.gameObject,
                    "Synthetic move event navigates back to Cancel");
                // Exercise the Tab traversal handler directly without claiming hardware key polling.
                Instance().GetType().GetMethod("CycleFocus", All).Invoke(Instance(), new object[] { false });
                Require(EventSystem.current.currentSelectedGameObject == accept.gameObject,
                    "Synthetic forward focus handler selects the approval action");
                Instance().GetType().GetMethod("CycleFocus", All).Invoke(Instance(), new object[] { true });
                Require(EventSystem.current.currentSelectedGameObject == cancel.gameObject,
                    "Synthetic backward focus handler restores Cancel");
                cancel.OnSubmit(new BaseEventData(EventSystem.current));
                Require(_cancelled == 1 && _accepted == 0, "Submit on Cancel invokes only cancellation once");
                AssertPromptClosed();
            });
            Add("content-open", () =>
            {
                BeginPromptFixture();
                Call("ShowConfirmation", "ui-smoke", "Optional server content",
                "UI fixture only.\n\n12 custom items · 3 files · 1.5 MiB\n\nAllow content to download these icons and audio. Skip content to continue without the optional content.\n\nServer name with literal markup: <b>not bold</b>",
                "Allow content", "Skip content", (Action)(() => _accepted++), (Action)(() => _cancelled++));
            });
            Add("content", () => Capture("content"));
            Add("content-navigation-submit", () =>
            {
                Require(EventSystem.current.currentSelectedGameObject == PromptButton("_cancel").gameObject,
                    "Content approval starts on Skip content");
                MoveSelected(MoveDirection.Right, Vector2.right);
                Require(EventSystem.current.currentSelectedGameObject == PromptButton("_accept").gameObject,
                    "Synthetic move event selects Allow content");
                PromptButton("_accept").OnSubmit(new BaseEventData(EventSystem.current));
                Require(_accepted == 1 && _cancelled == 0, "Submit on Allow content invokes only approval once");
                AssertPromptClosed();
            });
            Add("failure-open", () => Call("ShowNotice", "ui-smoke", "Cannot connect",
                "UI fixture only.\n\nThe server uses a different Anomaly protocol. Update the client and server to the same release.\n\nDiagnostic code: ANOMALY-PROTOCOL", (Action)(() => { })));
            Add("failure", () => Capture("failure"));
            Add("close", () => Call("HideAll"));
        }
        Add("status", () =>
        {
            Require(Anomaly.Client.Api.Commands.CommandRegistry.Execute("anomaly.status",
                text => File.AppendAllText(Path.Combine(_output, "status.txt"), text + "\n")), "Status command executes");
        });
        string server = Environment.GetEnvironmentVariable("ANOMALY_SMOKE_SERVER");
        if (!string.IsNullOrEmpty(server))
        {
            if (!Uri.TryCreate("udp://" + server, UriKind.Absolute, out var uri) ||
                !System.Net.IPAddress.TryParse(uri.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
                throw new InvalidOperationException("Smoke server must be an explicit loopback IP endpoint.");
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                int number = attempt;
                Add("connect-" + number, () => { ApiClient.MainMenu.ChangeMenu(0); ApiClient.Connect(server); });
                AddWait("ready-" + number, () =>
                {
                    if (Instance() != null && IsVisible())
                    {
                        var accept = (TMP_Text)Instance().GetType().GetField("_acceptLabel", All).GetValue(Instance());
                        if (accept.text is "Trust and connect" or "Allow content")
                        {
                            long presentation = (long)Instance().GetType().GetField("_presentation", All).GetValue(Instance());
                            if (_connectionPromptPresentation != presentation)
                            {
                                _connectionPromptPresentation = presentation;
                                _connectionPromptActionAt = Time.realtimeSinceStartupAsDouble + 2;
                                Capture(accept.text == "Trust and connect" ? "loopback-trust-" + number : "loopback-content-" + number);
                                return false;
                            }
                            if (Time.realtimeSinceStartupAsDouble < _connectionPromptActionAt) return false;
                            if (accept.text == "Trust and connect") Submit();
                            else PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
                            return false;
                        }
                    }
                    // Ready is a scene/lifecycle signal; the enrollment publication and local
                    // player spawn can arrive later. Wait for all gameplay prerequisites.
                    return ApiClient.IsClientReady && !string.IsNullOrEmpty(ApiClient.EnrollmentId)
                        && Il2Cpp.StaticUnityMethods.IsPlaying;
                }, 90);
                Add("connected-" + number, () =>
                {
                    Require(!string.IsNullOrEmpty(ApiClient.EnrollmentId), "Authenticated enrollment present (not recorded)");
                    Require(_readyEvents == number, "One Ready event per completed connection");
                    AssertCanvasLifetime();
                    Call("ShowNotice", "ui-smoke", "Connected validation",
                        "Authenticated loopback connection completed. This test also verifies modal cursor and movement capture during gameplay.", (Action)(() => { }));
                });
                Add("connected-modal-" + number, () =>
                {
                    Require(Il2Cpp.StaticUnityMethods.IsPlaying, "Stock game reports an active gameplay session");
                    Require(Il2CppCursorManagement.CursorManager.MovementLocked, "Modal locks movement during actual gameplay");
                    Capture("connected-" + number);
                });
                Add("disconnect-" + number, () =>
                {
                    Call("HideAll");
                    _disconnectedEventsBeforeStop = _disconnectedEvents;
                    ApiClient.Disconnect();
                });
                AddWait("disconnected-" + number, () => !ApiClient.IsConnected && ApiClient.MainMenu != null, 45);
                Add("cleanup-" + number, () =>
                {
                    Require(ApiClient.EnrollmentId == null, "Enrollment cleared on disconnect");
                    Require(!ApiClient.IsClientReady, "Readiness cleared on disconnect");
                    Require(_disconnectedEvents == _disconnectedEventsBeforeStop + 1,
                        "One Disconnected event for the explicit disconnect (handshake retries counted separately)");
                    Require(!IsVisible(), "Normal disconnect leaves no stale modal");
                    Require(!(bool)Instance().GetType().GetProperty("LockMovement", All).GetValue(Instance()),
                        "Hidden modal no longer requests movement capture");
                    Require(InputRegistry.GetBinding("example.hello.greet") != null,
                        "Ordinary mod registration survives returning to the menu");
                    AssertCanvasLifetime();
                    ShowSettings();
                });
                Add("returned-settings-" + number, () =>
                {
                    var dropdown = QualityDropdown();
                    dropdown.value = 2;
                    Require(_quality.Value == 2, "Recreated menu settings retain a working value binding");
                    dropdown.value = 1;
                    Require(_quality.Value == 1, "Recreated menu settings restore the test default");
                    Capture("returned-settings-" + number);
                });
            }
            long terminalPresentation = 0;
            double failureVisibleUntil = 0;
            Add("unavailable-loopback", () =>
            {
                int unusedPort;
                using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
                    unusedPort = ((System.Net.IPEndPoint)reservation.Client.LocalEndPoint).Port;
                ApiClient.MainMenu.ChangeMenu(0);
                ApiClient.Connect("127.0.0.1:" + unusedPort);
            });
            AddWait("terminal-connection-failure", () => IsVisible() &&
                ((TMP_Text)Instance().GetType().GetField("_details", All).GetValue(Instance())).text.Contains("AUTH-NO-RESPONSE", StringComparison.Ordinal), 60);
            Add("terminal-failure-capture", () =>
            {
                terminalPresentation = (long)Instance().GetType().GetField("_presentation", All).GetValue(Instance());
                failureVisibleUntil = Time.realtimeSinceStartupAsDouble + 6;
                Capture("actual-connection-failure");
            });
            AddWait("terminal-failure-persistence-delay", () => Time.realtimeSinceStartupAsDouble >= failureVisibleUntil, 10);
            Add("terminal-failure-dismiss", () =>
            {
                Require(IsVisible() && (long)Instance().GetType().GetField("_presentation", All).GetValue(Instance()) == terminalPresentation,
                    "Real terminal connection failure remains visible until dismissal");
                PromptButton("_cancel").OnSubmit(new BaseEventData(EventSystem.current));
                Require(!IsVisible(), "Explicit dismissal closes the real terminal failure");
            });
        }
        Add("complete", () =>
        {
            File.WriteAllText(Path.Combine(_output, "result.txt"), $"UI smoke passed; {_captures} screenshots. Scripted UI and synthetic EventSystem/focus-handler actions; physical keyboard, Tab/Escape polling, and clipboard interaction are not validated.\n");
            Application.Quit();
        });
    }

    public override void OnUpdate()
    {
        if ((_dummyVisibilitySmoke || _dedicatedDummyControlSmoke) && !_failed)
        {
            try { MuteDummyMenuMusicContinuous(); }
            catch (Exception ex) { Fail("Menu music mute: " + ex); return; }
        }
        if (_localHostingSmoke && _watchHostingWarning && !_failed)
        {
            try { CheckHostingWarning(); }
            catch (Exception ex) { Fail("Local hosting warning: " + ex); return; }
        }
        if (string.IsNullOrEmpty(_output) || _failed || Time.realtimeSinceStartupAsDouble < _next) return;
        if (!_started) { Fail("Main menu did not become ready within 90 seconds."); return; }
        if (_wait != null)
        {
            try
            {
                if (_wait()) { Require(true, _waitName); _wait = null; }
                else if (Time.realtimeSinceStartupAsDouble > _waitDeadline) { Fail("Timed out: " + _waitName); return; }
                else { _next = Time.realtimeSinceStartupAsDouble + 0.5; return; }
            }
            catch (Exception ex) { Fail(_waitName + ": " + ex); return; }
        }
        if (_steps.Count == 0) return;
        var step = _steps.Dequeue();
        try
        {
            File.AppendAllText(Path.Combine(_output, "steps.log"), step.Name + "\n");
            step.Action();
        }
        catch (Exception ex) { Fail(step.Name + ": " + ex); }
        _next = Time.realtimeSinceStartupAsDouble + 2;
    }

    private void ShowSettings()
    {
        Call("HideAll");
        var menu = ApiClient.MainMenu;
        int index = Array.FindIndex(menu.submenus.ToArray(), item => item.name.Contains("Setting", StringComparison.OrdinalIgnoreCase));
        Require(index >= 0, "Main menu contains settings submenu");
        menu.ChangeMenu(index);
        var controller = Resources.FindObjectsOfTypeAll<UserSettingsCategories>().First(item => item.gameObject.activeInHierarchy);
        Require(controller._categories.Count(category => category.Activator != null && category.Activator.name == "ModsTab") == 1,
            "Settings controller contains exactly one Mods tab");
        controller.SelectCategory(controller._categories.Length - 1);
    }

    private static TMP_Dropdown QualityDropdown() =>
        GameObject.Find("ModSettings.Row.example_smoke.quality").GetComponentInChildren<TMP_Dropdown>();

    private static Button PromptButton(string field) =>
        (Button)Instance().GetType().GetField(field, All).GetValue(Instance());

    private void BeginPromptFixture()
    {
        _accepted = _cancelled = 0;
        _selectionBeforePrompt = EventSystem.current.currentSelectedGameObject;
        _movementBeforePrompt = Il2CppCursorManagement.CursorManager.MovementLocked;
    }

    private void AssertPromptClosed()
    {
        Require(!IsVisible(), "Submitted prompt closes");
        Require(string.IsNullOrEmpty(Input().text), "Closed prompt clears editable text");
        Require(!(bool)Instance().GetType().GetProperty("LockMovement", All).GetValue(Instance()),
            "Closed prompt releases its movement capture request");
        Require(Il2CppCursorManagement.CursorManager.MovementLocked == _movementBeforePrompt,
            "Closing prompt restores the prior movement-lock state");
        var expectedSelection = _selectionBeforePrompt != null && _selectionBeforePrompt.activeInHierarchy
            ? _selectionBeforePrompt : null;
        Require(EventSystem.current.currentSelectedGameObject == expectedSelection,
            "Closing prompt restores the prior live selection");
    }

    private static void MoveSelected(MoveDirection direction, Vector2 vector)
    {
        var events = EventSystem.current;
        var selected = events.currentSelectedGameObject.GetComponent<Selectable>();
        selected.OnMove(new AxisEventData(events) { moveDir = direction, moveVector = vector });
    }

    private void AssertCanvasLifetime()
    {
        Require(UiCanvas.IsReady && UiCanvas.Root.GetInstanceID() == _canvasInstanceId,
            "Framework canvas survives scene transitions with the same identity");
        Require(Resources.FindObjectsOfTypeAll<Canvas>().Count(canvas => canvas != null && canvas.name == "Anomaly.Root") == 1,
            "Exactly one framework root canvas exists");
        int modalCount = Resources.FindObjectsOfTypeAll<Canvas>().Count(canvas => canvas != null && canvas.name == "AnomalyModalCanvas");
        Require(modalCount == (Instance() == null ? 0 : 1),
            "The lazily created modal canvas has exactly one owner");
    }

    private static object Instance() => Prompt.GetField("_instance", All).GetValue(null);
    private static TMP_InputField Input() => (TMP_InputField)Instance().GetType().GetField("_input", All).GetValue(Instance());
    private static bool IsVisible() => (bool)Instance().GetType().GetField("_isVisible", All).GetValue(Instance());
    private static void Submit() => Instance().GetType().GetMethod("Submit", All).Invoke(Instance(), null);
    private static void Call(string name, params object[] args) => Prompt.GetMethod(name, All).Invoke(null, args);
    private static object SettingsCall(string name, params object[] args) =>
        Type.GetType("Anomaly.Client.Patches.Menu.ModSettings.ModSettingsSaveScheduler, Anomaly.Client", true)
            .GetMethod(name, All).Invoke(null, args);
    private void Add(string name, Action action) => _steps.Enqueue((name, action));
    private void AddWait(string name, Func<bool> condition, int seconds) => Add(name, () =>
    {
        _waitName = name;
        _wait = condition;
        _waitDeadline = Time.realtimeSinceStartupAsDouble + seconds;
    });
    private void Require(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        File.AppendAllText(Path.Combine(_output, "assertions.log"), "PASS " + text + "\n");
    }
    private void Capture(string name)
    {
        string path = Path.Combine(_output, $"{Screen.width}x{Screen.height}-{name}.png");
        MelonCoroutines.Start(CaptureAtEndOfFrame(path));
    }
    private IEnumerator CaptureAtEndOfFrame(string path)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, ScreenshotPng.Encode(texture.width, texture.height, texture.format, texture.GetRawTextureData().ToArray()));
            _captures++;
        }
        catch (Exception ex) { Fail("Screenshot: " + ex); }
        finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
    }
    private void Fail(string message)
    {
        _failed = true;
        File.WriteAllText(Path.Combine(_output, "failure.txt"), message);
        MelonLogger.Error(message);
        Application.Quit();
    }
}
