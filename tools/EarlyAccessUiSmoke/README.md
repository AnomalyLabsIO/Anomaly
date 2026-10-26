# Early access in-game UI smoke test

This opt-in development MelonMod exercises the real Unity UI, records assertions,
and captures engine screenshots. **Never include the harness or its test profiles
in release packages.** It runs only when `ANOMALY_UI_SMOKE_DIR` is set.

**Current launch requirement:** every `Run-Smoke.ps1` call must pass
`-UseModLaunch` and `-GameDirectory 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy'`.
The runner starts only that installation's `.modlaunch\SCPSL-ModLaunch.exe`.
Never launch `SCPSL.exe` directly. Older scenario examples below retain their
historical server/evidence paths; replace their client game path and add
`-UseModLaunch` before using them.

## Inputs and isolation

Use PowerShell 7 on Windows, the repository's .NET build prerequisites, and a
graphics-capable game session. The seed client must be the supported SCP:SL
14.2.7/MelonLoader 0.7.3 installation, started once to generate its IL2CPP
references. `Prepare-Game.ps1` requires `SCPSL.exe`, `GameAssembly.dll`,
`version.dll`, `MelonLoader/net6/MelonLoader.dll`, and
`MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` under that seed root.

Builds also need the supported server reference tree: game managed assemblies,
`BouncyCastle.Cryptography.dll`, and `LabApi.dll`. Server Harmony 2.2.2 is
restored from NuGet; the server runtime resolves BouncyCastle from the game's
Managed directory. See
[`Directory.Build.props`](../../Directory.Build.props) for the reference-path
properties. Even a client-only smoke build uses server crypto references through
`Anomaly.Shared`. Running a local server additionally requires a supported server
installation and the built Anomaly server package inputs.

Preparation requires **new destination directories**, separate from and outside
the source installations. The scripts read the source installations and stage
runtime packages from [`release/package-layout.json`](../../release/package-layout.json);
they do not install into or update the sources. Static executables, libraries,
and game assets are hard-linked, so source and destination must support hard
links on the same volume. Pass `-CopyStaticAssets` to preparation for a completely
physical copy, including when sandbox access prevents hard-link creation. Loader files and other writable files are copied.
Hard links are shared files, not a read-only filesystem boundary: never edit,
patch, update, or run an installer over linked static files in the smoke copies.

A launcher-managed seed may have no `version.dll` while it is closed. In that
case pass `-LoaderProxyPath` with an existing matching MelonLoader proxy.
Preparation verifies the proxy's major/minor/build version against the seed's
loader DLL, physically copies it into the private destination, and records its
source and SHA-256 in `smoke-staging.json`. It excludes hidden launcher/cache
directories and never installs that proxy into the source game.

Historical private preparation starts with fresh `UserData`, without importing the source
profile, trust store, mods, or plugins. Preparation and each run write explicit
hash approvals in the test instance's `UserData/Anomaly/mod-trust.json` for the
DLLs in its `Mods` directory. Keep that directory limited to the staged Anomaly
runtime, ordinary starter, and harness. These local test approvals do not qualify
official package signatures or the launcher's delivery workflow.

Stock SCP:SL client settings still use the Windows user's Roaming profile even
when the game directory and Anomaly `UserData` are isolated. A smoke run can
update stock files such as `registry.txt` and `iphistory.txt`; use a disposable
Windows account when stock profile isolation is required.

## Build and run through ModLaunch

Run these templates from the repository root. Replace the seed paths
with your reference layout. Environment properties are used here because
`Run-Smoke.ps1` invokes its own builds and does not accept extra MSBuild arguments.

```powershell
$env:AnomalyClientRoot = 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy'
$env:AnomalyServerRoot = [IO.Path]::GetFullPath('../Server')

dotnet build Anomaly.slnx -c Release -p:DeployLocal=false
dotnet build tools/EarlyAccessUiSmoke/EarlyAccessUiSmoke.csproj -c Release -p:DeployLocal=false

./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory $env:AnomalyClientRoot `
    -EvidenceDirectory ./artifacts/news-ui-smoke/modlaunch-run-01 `
    -UseModLaunch -NewsOnly
```

The solution build includes the runtime, bootstrap, and ordinary starter required
by preparation. If the reference layout differs, set `AnomalyClientInteropDir`,
`AnomalyClientMelonLibDir`, `AnomalyServerGameDir`, `AnomalyServerReferenceDir`,
or `AnomalyLabApiDir` as needed before building. Check every
build succeeds before staging.

`Run-Smoke.ps1` rebuilds with deployment disabled, backs up each file it will
temporarily replace, then stages runtime DLLs and the starter/harness directly
in the authorized game's `Mods`, `Plugins`, and `UserLibs`. ModLaunch preserves
these user-owned folders; it does not materialize an Anomaly loadout over them.
The runner merges only the three test mod hashes into the existing trust policy,
preserving its other fields and approvals. Existing identities are retained.

Backups are saved under `artifacts/news-ui-smoke/modlaunch-backup-<evidence-name>`
(or an explicit `-BackupDirectory` within that directory). The manifest includes
original runtime/package files, identities and trust files, extracted translations,
loader/mod preferences, and stock `registry.txt`/`iphistory.txt` preferences.
Native game and unrelated mod hashes are recorded for restoration verification.
Backup contents may contain private profiles; keep them local.

The runner starts exactly
`E:\Steam\steamapps\common\SCP Secret Laboratory - Copy\.modlaunch\SCPSL-ModLaunch.exe`
with the game arguments and harness environment. The launcher passes that
environment to its game child. The runner writes `launcher.pid` and returns
after launch; this alone does not certify completion. It does not install a proxy,
alter launcher code, or change another loadout.

After the game **and** launcher exit and ModLaunch restores its original native
files, restore the test deployment:

```powershell
./tools/EarlyAccessUiSmoke/Restore-ModLaunchSmoke.ps1 `
    -BackupDirectory ./artifacts/news-ui-smoke/modlaunch-backup-modlaunch-run-01
```

Restore refuses while either process is running, including a child remaining
after its launcher exits, or while a session journal/proxy remains. If necessary,
recover through the same launcher with `--modlaunch-recover-only` after its child
exits; never replace native files manually. Restore copies original bytes,
removes only newly added listed test files, verifies every restored hash, and
writes `restored.json` with native/unrelated-file mismatch results.

`-IntegrityMode` runs a focused authentication guard scenario against the required
`-LoopbackServer` endpoint: `clean`, `blocked`, `allowed-rejected`,
`allowed-accepted`, `gap-rejected`, `gap-accepted`, or `late`. Configure the client
external-modification preference and server risk policy before each fresh process.
The `late` scenario starts clean, applies a harmless external patch after admission,
checks disconnection, and checks a fresh nonce on the subsequent connection.
Inspect `result.txt` or `failure.txt`, `integrity.log`, and
`integrity-diagnostics.txt`. For rejection scenarios, also verify the isolated
server's integrity-policy log: a client-side refusal alone does not establish the
server admission decision. Keep the fixture DLL out of release packages.

The `-DummyVisibility` variant uses the primary display resolution instead of
1280×720 and confirms that resolution in the running game before gameplay capture.

Use `-NewsOnly` for the focused installed-mods and diagnostics display checks.
This mode requires `-UseModLaunch` and cannot be combined with connection/hosting
scenarios:

```powershell
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy' `
    -EvidenceDirectory ./artifacts/news-ui-smoke/modlaunch-run-01 `
    -UseModLaunch -NewsOnly
```

The News suite captures the actual collected inventory at 1280×720, 1920×1080,
3440×1440, 1024×768, and 1280×1024. It then switches the process-local
translation service to Russian, reloads the actual main-menu scene, and repeats
the matrix with explicit long-name, long-version, many-entry, missing-dependency,
unattributed-error, and historical-report fixtures. These fixtures do not load
extra executable mods or manufacture real failure evidence. Both the diagnostics
service and presenter receive the same detached fixture snapshot through internal
reflection hooks available to this opt-in harness.

Assertions use four transformed rectangle corners to check screen containment,
row overlap, text height, sidebar separation, and viewport width. They check
the first fully collected live overview is already at the top before any test
scroll assignment, with the current-launch issues heading visible. Native social
validation requires all ten stock `SocialMediaButton` handlers and their tile
wrappers inside the owned grid, including nested image bounds within each tile.
`news-*-hierarchy.json` records actual IL2CPP component types and rectangles for
diagnosing custom/nested native controls. No social check accepts an empty grid.
The suite also checks
scrolling to the final content edge, retained foldouts and scroll position after
tab navigation and a real window resize, a valid `NewsRaw` response through native
article `TextProcessor` and `ShowAnnouncement`,
idempotent installation, and a second actual main-menu scene reload. The reload
must produce one owned root and retain the native social buttons. Per-resolution
`news-*-layout.json` accompanies top/bottom screenshots. The harness restores the
original translation service and clears fixtures on completion or deinitialization.
It submits the actual Copy Summary button through a synthetic EventSystem event,
reads the engine clipboard, checks runtime/dependency information and redaction,
and restores the original clipboard contents. Long diagnostic evidence stays
behind Details with a fixed-size compact overview; its expansion is checked too.
Gameplay reconnect coverage remains in the optional loopback scenario, which
also checks and captures News after returning to the menu.

The runner refuses to refresh the authorized game's binaries while its launcher
or game child is running. `runtime-inputs.json` records the exact staged hashes and
source commit plus whether the source checkout was dirty. Require the completion
receipt and inspect screenshots and logs; a build or successful launch alone
does not establish visual acceptance.

Use a new evidence and backup directory for every run, and restore before another
test deployment. An existing profile skips first-run nickname fixtures; this
qualification does not create a new identity to repeat first-run coverage.

## Local hosting and automatic admin

Use `-LocalHosting` to run the focused hosting scenario independently of the UI
and server API fixtures. It saves a first-run nickname when needed, enters `host`
through the stock console, opens Remote Admin, sends a read-only `version` query
through the stock client/server Remote Admin channel, disconnects, and repeats
through `NewMainMenu.StartServer()` (the Create Server menu action).

```powershell
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/local-host-smoke/game `
    -EvidenceDirectory ./artifacts/local-host-smoke/run-01 `
    -LocalHosting
```

Use a newly prepared game as described above. The runner creates private stock
game data and configuration under the staged game's `LocalHostingSmoke/AppData`
and supplies `-appdatapath` / `-configpath`. The test copy binds to `127.0.0.1` and
`::1`, disables UPnP forwarding and the query listener, and runs with offline
authentication. These are test-instance settings; production hosting keeps its
existing configuration. Verify the actual UDP listeners belong to the recorded
`client.pid` and use loopback addresses during the run.

The harness checks the warning every frame while hosting, both local/host hub
identities and reciprocal in-process connections, the stock full permission mask,
admin chat and gameplay-data flags, Remote Admin command synchronization, the
logged-in Remote Admin panel, and a successful command reply. It never enters
`adminme` or calls the automatic-grant implementation. Inspect
`local-host-console.json`, `local-host-menu.json`, the screenshots, assertions,
and result alongside the player/loader logs.

### Client scripting and owned text

Add `-ClientScripting` to a local-host run through the authorized ModLaunch install:

```powershell
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy' `
    -EvidenceDirectory ./artifacts/scripting-readiness/local-output-run `
    -UseModLaunch -LocalHosting -ClientScripting
```

The local script source tree must be empty; unrelated owner scripts are never
replaced. The reversible backup includes the staged sample Lua/descriptor and
two ownership fixtures. The production host reads these through its normal local
pack validator. The harness checks sample output, real player/position queries,
120 frames of two queued native-query callbacks per frame, independent literal
text/raycast settings, owner-specific clear/expiry, and disconnect/rehost cleanup.
Inspect `scripting-state-*.json`, `scripting-memory-*.json`, assertions, screenshots
and the completion receipt. Memory values are charged estimates, not native/CLR heap
measurements. This scenario neither enables remote execution nor qualifies hostile
memory containment. Restore the backup after the launcher and game have exited.


Add `-DummyVisibility` to the `-LocalHosting` invocation for the focused dummy
reproduction. The harness uses stock text Remote Admin to force-start the round,
lock the round, set the host to FacilityGuard, spawn a named dummy, set the dummy
to FacilityGuard, bring it to the host, enable follow, and destroy it. It closes
Remote Admin and moves the host through bound game input to verify follow
motion. It then toggles follow off through Remote Admin and moves away so
the gameplay screenshot has a clear view from at least 2.5 m. The test unlocks
the round before disconnect and repeats the sequence for console `host` and
Create Server. The ordinary `-LocalHosting` run keeps the shorter admin fixture.

```powershell
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/local-host-smoke/game `
    -EvidenceDirectory ./artifacts/local-host-smoke/dummy-run-01 `
    -LocalHosting -DummyVisibility
```

Inspect `dummy-commands-console.jsonl` and `dummy-commands-menu.jsonl` for the
actual Remote Admin replies. Stage JSON files record the host and dummy roles,
netIds, positions, local-player flags, host observer, `NetworkClient.spawned`,
character model owner and visibility, renderer state, culling state, and camera
viewport coordinates and line of sight. `dummy-<entry>-gameplay-view.json` and the
corresponding `dummy-gameplay-<entry>.png` must both show the dummy in front of
the host; the JSON model and spawn probes alone cannot certify that it rendered.
The scenario mutes test-process menu music sources without changing shared stock
settings. Failure leaves the last stage JSON, command replies, and `failure.txt`
for diagnosis. For a dedicated control with an explicitly authorized test admin,
start an isolated loopback server and run the same sequence with:

```powershell
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/local-host-smoke/game `
    -EvidenceDirectory ./artifacts/local-host-smoke/dedicated-run-01 `
    -LoopbackServer '127.0.0.1:27993' -DedicatedDummyControl
```

The dedicated mode records stock Remote Admin replies, role, movement, client
spawn/model state, and a gameplay screenshot. Inspect the screenshot before
claiming the dummy is visually present. The loopback endpoint and owner mapping
must refer to that isolated server; this mode was prepared but not executed in
the local-host visibility investigation.

The 2026-09-23 isolated 14.2.7 run in `artifacts/local-host-smoke/dummy-before-06`
did not reproduce an invisible dummy. Both local entry points reached gameplay
captures with a FacilityGuard dummy, a host observer, a spawned client identity,
an active model, and clear camera line of sight at 3.01 m (console) and 5.06 m
(Create Server). The console screenshot was visually inspected and showed the
guard; the user also confirmed the bot was visible. Dummy removal and round
unlock passed in both entries. The final Create Server return-to-menu wait
timed out after disconnect, so that cleanup check is not certified. No client
behavior patch was made from this run.

Optionally add `-LoopbackServer '127.0.0.1:27988'` after preparing and starting the
separate loopback server described below. After the two local hosts, the harness
connects to that server using normal Anomaly trust/authentication, skips optional
content, verifies authenticated readiness, and checks that the ordinary client
has no Remote Admin permissions. Use a fresh test server with no player admin
assignments. Without `-LoopbackServer`, the result explicitly leaves remote-client
exclusion unverified. `-LocalHosting` cannot be combined with `-ConnectionOnly`
or `-ServerApi`; no physical console keypress or menu mouse click is claimed.

## Optional loopback connection cycles

`Prepare-Server.ps1` stages a separate server, excludes the source server's
AppData/plugins, and creates local test configuration with IPv4 `127.0.0.1` and
IPv6 `::1` bind addresses. The port defaults to `27988`. It does not start the
server. There is **no native `-loopback` switch** in this workflow; isolation comes
from the generated bind configuration and verification of the actual listener.

```powershell
./tools/EarlyAccessUiSmoke/Prepare-Server.ps1 `
    -SourceServerRoot ./../Server/server `
    -OutputDirectory ./artifacts/early-access/smoke-server-01 `
    -Port 27988

$smokeServer = [IO.Path]::GetFullPath('./artifacts/early-access/smoke-server-01')
$serverInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $smokeServer 'SCPSL.exe'))
$serverInfo.WorkingDirectory = $smokeServer
$serverInfo.UseShellExecute = $false
$serverInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
foreach ($argument in @(
    '-batchmode', '-nographics', '-stdout', '-port27988',
    '-appdatapath', (Join-Path $smokeServer 'AppData'),
    '-configpath', (Join-Path $smokeServer 'AppData/config/27988'),
    '-logFile', (Join-Path $smokeServer 'server-player.log')
)) {
    $serverInfo.ArgumentList.Add($argument)
}
$serverProcess = [Diagnostics.Process]::Start($serverInfo)

# Check once startup has completed; both listeners must remain on loopback.
Get-NetUDPEndpoint -OwningProcess $serverProcess.Id |
    Select-Object LocalAddress, LocalPort

./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/early-access/smoke-game-01 `
    -EvidenceDirectory ./artifacts/early-access/ui-loopback-01 `
    -LoopbackServer '127.0.0.1:27988'
```

Check `server-player.log` for successful startup and confirm the owning server
process listens on `127.0.0.1` / `::1`, not a wildcard or external interface,
before launching the client. If changing the port, change preparation, launch,
config-directory, and endpoint arguments together. Stop the recorded test server
after the run; client completion does not stop it.

`-LoopbackServer` requires an explicit loopback IP endpoint. It enables two real
connect/disconnect cycles after the UI fixtures, including local trust approval,
authenticated readiness/enrollment checks, event counts, canvas lifetime, and
settings behavior after returning to the menu. The harness automatically accepts
the local test server's trust prompt. Do not point this workflow at a remote
server. Without this option, the UI-only run makes no claim about authentication
or scene-transition cleanup.

It then tries a freshly released loopback UDP port to verify a real timeout
notice, its diagnostic code, persistence for six seconds, and explicit dismissal.
The test skips optional content through the actual consent callback.

After inspecting a complete UI fixture run, use `-ConnectionOnly` with
`-LoopbackServer` to repeat the two connection cycles without replaying all
five-resolution fixtures. Keep the earlier screenshots as separate evidence.

For a dedicated server rejection, use `-AdmissionExpect '<text>'` with
`-LoopbackServer`. The isolated client connects, accepts a first-seen trust
prompt, waits for that text in Anomaly's connection notice, records
`admission-notice.txt` and a screenshot, checks phase-two connection cleanup,
then confirms the notice remains visible until dismissed. Use a fresh evidence
directory for each rejection case. The test server runner accepts
`-AdmissionRequirements` with semicolon-separated
`modId|minimumVersion|maximumVersionExclusive` triples, using an empty third
field for no upper bound. It also accepts `-BlockAnyAdditionalClientMods`,
`-BlockedClientModIds`, and `-PlainRejection` to exercise each denial path in a
private staged server. These are local fixture settings only.

## Server API and content cycles

Use `-ServerApi` with the ordinary LabAPI fixture described in
[`ServerApiSmoke`](../ServerApiSmoke/README.md). This selects the server API
scenarios instead of the UI fixture sequence and requires `-LoopbackServer`.
Start with a newly prepared client so cached consent cannot skip the first prompt.
Prepare a new server with `Prepare-Server.ps1` as above, then run:

```powershell
./tools/ServerApiSmoke/Prepare-Assets.ps1 `
    -OutputDirectory ./artifacts/server-api/assets-01

./tools/ServerApiSmoke/Run-Server.ps1 `
    -ServerDirectory ./artifacts/early-access/smoke-server-01 `
    -EvidenceDirectory ./artifacts/server-api/server-run-01 `
    -AssetDirectory ./artifacts/server-api/assets-01 `
    -Port 27988

$serverPid = [int](Get-Content ./artifacts/server-api/server-run-01/server.pid)
Get-NetUDPEndpoint -OwningProcess $serverPid |
    Select-Object LocalAddress, LocalPort

# Continue only after startup and the loopback listeners have been verified.
./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/early-access/smoke-game-01 `
    -EvidenceDirectory ./artifacts/server-api/client-run-01 `
    -LoopbackServer '127.0.0.1:27988' `
    -ServerApi -ServerApiMode full
```

The server runner builds and stages the runtime and fixture only in the isolated
server, configures its asset directory, and records its PID and runtime hashes.
It returns after launch. Check its `server-player.log` before starting the client.

`full` performs three authenticated connections: remain pending for 35 seconds
then decline; approve and activate the custom item/assets; reconnect with cached
approval. Approved cycles use the real item-use request, apply and clear the test
texture, exercise owned motion, and capture views at four yaw angles. The harness
also checks capability reporting, content readiness, and disconnect cleanup.
`approve` runs a single approval cycle. `connection` runs two connection cycles
with content declined; it does not exercise the item or texture path.

Keep both client and server evidence directories. Inspect the client assertions,
screenshots, facility snapshots and reports alongside server `events.jsonl`,
`rooms.txt`, `facility.json`, and runtime logs. For fixed-seed generation mutation,
late-join and restart qualification, follow the separate scenario procedure in
the [server fixture README](../ServerApiSmoke/README.md). A completed content cycle
does not establish those additional facility scenarios. Stop the recorded isolated
server after the run.

## Client facility catalog rejection probe

Use a private loopback server running `ANOMALY_SERVER_SMOKE_SCENARIO=planned-noop`.
Set that variable before `tools/ServerApiSmoke/Run-Server.ps1`; clear
`ANOMALY_SERVER_SMOKE_FAILURE_PROBE` so the server completes its own generation.
Once its planned snapshot is published, run one staged client:

```powershell
$env:ANOMALY_CLIENT_CATALOG_FAILURE_PROBE = '1'
try {
    ./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
        -GameDirectory ./artifacts/facility-plan-20260923/game `
        -EvidenceDirectory ./artifacts/facility-plan-20260923/catalog-client-run-01 `
        -LoopbackServer 127.0.0.1:27992 -ConnectionOnly
}
finally { Remove-Item Env:ANOMALY_CLIENT_CATALOG_FAILURE_PROBE -ErrorAction SilentlyContinue }
```

The environment-only fixture overrides the ordinary connection scenario. It requires
matching `smoke-staging.json` metadata, shortens the in-memory LCZ prefab catalog in
an earliest `SeedSynchronizer.Awake` prefix, and observes the production rejection.
No production runtime code or asset files are changed. Require `result.txt`, no
`failure.txt`, and all checks passing in `catalog-failure-probe.json`: an authenticated
planned snapshot rejected for catalog mismatch, no atlas/native fallback or planned
materialization, no successful Mirror Ready/AddPlayer or local player hub/enrollment,
and a terminal disconnect. Public `ClientEvents.Ready` is a scene callback and is
recorded as an observation rather than proof of gameplay admission.
Keep `catalog-injection.json`, `catalog-rejection.json`, logs,
runtime input hashes, and exact client/server PID teardown evidence with the result.
Run this only after other smoke clients using the staged game have exited; stop only
the server PID started for this probe.

## Evidence and limits

The UI suite captures News, menu, settings, trust, content, and failure fixtures at
1280×720, 1920×1080, 3440×1440, 1024×768, and 1280×1024, plus applicable first-run and connection-cycle
screens. It records `steps.log`, `assertions.log`, `status.txt`, and either
`result.txt` or `failure.txt`. A successful process launch or exit code is
insufficient: require `result.txt`, no `failure.txt`, expected assertions and
screenshots, correct image dimensions, and inspect the images and game/loader
logs. A logged first-run skip is not first-run evidence.

Navigation uses **synthetic EventSystem move/submit events and direct focus-handler
calls**. It checks selection, callbacks, resets, rebuild behavior, and cleanup;
it does not prove physical keyboard input, Tab/Shift-Tab/Escape key polling,
clipboard behavior, or accessibility. UI-only fixture trust/content panels do not perform
a remote content transfer or constitute end-to-end consent validation. Launcher
installation/signature delivery and Proton behavior require separate qualification.

Run output, loader/server logs, staging metadata, and private test profiles may
contain paths, identifiers, or other private diagnostics. Review and redact
evidence before sharing it; do not distribute the test profile or entire staged
game/server directory.
