# Early access in-game UI smoke test

This opt-in development MelonMod exercises the real Unity UI, records assertions,
and captures engine screenshots. **Never include the harness or its test profiles
in release packages.** It runs only when `ANOMALY_UI_SMOKE_DIR` is set.

## Inputs and isolation

Use PowerShell 7 on Windows, the repository's .NET build prerequisites, and a
graphics-capable game session. The seed client must be the supported SCP:SL
14.2.7/MelonLoader 0.7.2 installation, started once to generate its IL2CPP
references. `Prepare-Game.ps1` requires `SCPSL.exe`, `GameAssembly.dll`,
`version.dll`, `MelonLoader/net6/MelonLoader.dll`, and
`MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` under that seed root.

Builds also need the supported server reference tree: game managed assemblies,
`BouncyCastle.Cryptography.dll`, `LabApi.dll`, and server `0Harmony.dll`. See
[`Directory.Build.props`](../../Directory.Build.props) for the reference-path
properties. Even a client-only smoke build uses server crypto references through
`Anomaly.Shared`. Running a local server additionally requires a supported server
installation and the built Anomaly server package inputs.

Preparation requires **new destination directories**, separate from and outside
the source installations. The scripts read the source installations and stage
runtime packages from [`release/package-layout.json`](../../release/package-layout.json);
they do not install into or update the sources. Static executables, libraries,
and game assets are hard-linked, so source and destination must support hard
links on the same volume. Loader files and other writable files are copied.
Hard links are shared files, not a read-only filesystem boundary: never edit,
patch, update, or run an installer over linked static files in the smoke copies.

The client starts with fresh private `UserData`, without importing the source
profile, trust store, mods, or plugins. Preparation and each run write explicit
hash approvals in the test instance's `UserData/Anomaly/mod-trust.json` for the
DLLs in its `Mods` directory. Keep that directory limited to the staged Anomaly
runtime, ordinary starter, and harness. These local test approvals do not qualify
official package signatures or the launcher's delivery workflow.

## Prepare and run the client

Run these templates from the repository root. Replace the relative seed paths
with your reference layout. Environment properties are used here because
`Run-Smoke.ps1` invokes its own builds and does not accept extra MSBuild arguments.

```powershell
$env:AnomalyClientRoot = [IO.Path]::GetFullPath('../Client')
$env:AnomalyServerRoot = [IO.Path]::GetFullPath('../Server')

dotnet build Anomaly.slnx -c Release -p:DeployLocal=false
dotnet build tools/EarlyAccessUiSmoke/EarlyAccessUiSmoke.csproj -c Release -p:DeployLocal=false

./tools/EarlyAccessUiSmoke/Prepare-Game.ps1 `
    -SourceGameRoot $env:AnomalyClientRoot `
    -OutputDirectory ./artifacts/early-access/smoke-game-01

./tools/EarlyAccessUiSmoke/Run-Smoke.ps1 `
    -GameDirectory ./artifacts/early-access/smoke-game-01 `
    -EvidenceDirectory ./artifacts/early-access/ui-run-01
```

The solution build includes the runtime, bootstrap, and ordinary starter required
by preparation. If the reference layout differs, set `AnomalyClientInteropDir`,
`AnomalyClientMelonLibDir`, `AnomalyServerGameDir`, `AnomalyServerReferenceDir`,
`AnomalyLabApiDir`, or `AnomalyHarmonyPath` as needed before building. Check every
build succeeds before staging.

`Run-Smoke.ps1` accepts only a game created by `Prepare-Game.ps1` with a matching
`smoke-staging.json`. It rebuilds the client, starter, and harness with deployment
disabled, refreshes the isolated runtime and local approvals, then starts that
test game's `SCPSL.exe` with `-noauth -screen-fullscreen 0 -screen-width 1280
-screen-height 720 -logFile <evidence>/player.log`. It sets the harness environment
only for the child process and writes `client.pid`. The command returns after
launch; it does not wait for or certify completion.

Use a new evidence directory for every run. Reusing a staged game preserves its
test profile; the harness skips nickname fixtures when an existing profile no
longer needs a nickname. Prepare another new game directory to repeat first-run
and nickname write-failure coverage.

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
three-resolution fixtures. Keep the earlier screenshots as separate evidence.

## Evidence and limits

The harness captures menu, settings, trust, content, and failure fixtures at
1280×720, 1920×1080, and 3440×1440, plus applicable first-run and connection-cycle
screens. It records `steps.log`, `assertions.log`, `status.txt`, and either
`result.txt` or `failure.txt`. A successful process launch or exit code is
insufficient: require `result.txt`, no `failure.txt`, expected assertions and
screenshots, correct image dimensions, and inspect the images and game/loader
logs. A logged first-run skip is not first-run evidence.

Navigation uses **synthetic EventSystem move/submit events and direct focus-handler
calls**. It checks selection, callbacks, resets, rebuild behavior, and cleanup;
it does not prove physical keyboard input, Tab/Shift-Tab/Escape key polling,
clipboard behavior, or accessibility. Fixture trust/content panels do not perform
a remote content transfer or constitute end-to-end consent validation. Launcher
installation/signature delivery and Proton behavior require separate qualification.

Run output, loader/server logs, staging metadata, and private test profiles may
contain paths, identifiers, or other private diagnostics. Review and redact
evidence before sharing it; do not distribute the test profile or entire staged
game/server directory.
