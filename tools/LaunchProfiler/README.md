# Client launch profiler

Opt-in diagnostic tooling for the local Windows SCP:SL 14.2.7 / Unity
6000.0.43f1 / MelonLoader 0.7.2 client. Nothing here is included in the runtime
package. Normal Anomaly, launcher, and game source behavior is unchanged.

## Measurement contract

The headline interval is QPC immediately before `Process.Start` to `menu.ready`.
This is elapsed launch-request-to-menu time, not exact process CPU execution time.
The plugin also records loader milestones, scene transitions, first-run prompts,
instrumentation installation, and settings first-use latency. Detailed mode adds
selected inclusive method spans and coroutine execution/lifetime observations.
Nested spans and simultaneous thread durations must not be added as total time.

Loader methods are protected by PatchShield. The profiler uses their public
lifecycle events and reports support setup/type registration/automatic patching
as a combined interval. It never disables loader protection. The early plugin
code does not use Unity or generated game types. Game probes start only after
the loader support module is ready.

Readiness requires the normal main menu, no blocking loading/warning/prompt,
interactable controls reachable through UI raycasting, an active input module,
settled dimensions, and three consecutive frame intervals no longer than 100 ms.
A small all-mode postfix observes the stock warning's successful `Start`; a zero
repeat-view duration is accepted only after that initialization and scene startup.
Fixture actions use stock
UI handlers; they do not certify physical mouse/keyboard input. Engine screenshots
must be inspected before accepting a new fixture. `visual-review.json` can reject
a structurally successful capture. A hidden overlay is not a usable menu.

`milestones` is for headline comparisons. `detailed` is for attribution and can
resolve/JIT additional types or install detours, so compare its overhead separately.
The runner hashes inputs before launch; **the OS file cache is therefore warmed**.
Fresh profile and fresh generated assemblies are separate classifications, not
claims of an OS-cold launch.

## Build and prepare

Use PowerShell 7. Build the core, Bootstrap, and profiler with local deployment off:

```powershell
dotnet build Anomaly.Client/Anomaly.Client.csproj -c Release -p:DeployLocal=false
dotnet build Anomaly.Bootstrap/Anomaly.Bootstrap.csproj -c Release -p:DeployLocal=false
dotnet build tools/LaunchProfiler/LaunchProfiler.csproj -c Release -p:DeployLocal=false
dotnet test tools/LaunchProfiler/Tests/LaunchProfiler.Tests.csproj -c Release

pwsh -NoProfile -File tools/LaunchProfiler/Prepare-Game.ps1 `
  -SourceGameRoot E:/Projects/AnomalyLabs/ModdedFiles/games/latest `
  -OutputDirectory artifacts/launch-profiler/game-core

pwsh -NoProfile -File tools/LaunchProfiler/Set-PrivateDisplay.ps1 `
  -GameDirectory artifacts/launch-profiler/game-core
```

Use `-p:AnomalyClientRoot=<seed>` when the seed's generated references differ from
the repository default reference client. Build all measured binaries against that
same reference set. `Prepare-Game.ps1` accepts `-Loadout LoaderOnly` and `-Loadout
Dev -DevLocalDirectory <directory>` for comparison stages. The marker records what
was staged; manifests list actual loaded-file hashes. Do not infer compatibility
from matching assembly version numbers alone.

The destination must be new and separate from the seed. Loader/generated files,
profiles, and writable data are private copies. Static assets may be hardlinked:
never patch, update, or run an installer over a prepared stage's static files.
Launcher patch/preparation experiments require physical copies of files they mutate.
No real identity, trust profile, or consent history is imported.
`Set-PrivateDisplay.ps1` selects stock Windowed mode only in the private redirected
`registry.txt`, retaining a backup. This avoids the stock exclusive-fullscreen
focus transition changing dimensions between runs. Command-line dimensions can
be superseded by stock settings; acceptance compares the observed rendered size.

## Run and compare

```powershell
pwsh -NoProfile -File tools/LaunchProfiler/Run-Profile.ps1 `
  -GameDirectory artifacts/launch-profiler/game-core `
  -EvidenceDirectory artifacts/launch-profiler/core-detailed `
  -Mode detailed

pwsh -NoProfile -File tools/LaunchProfiler/Run-Matrix.ps1 `
  -GameDirectory artifacts/launch-profiler/game-core `
  -EvidenceDirectory artifacts/launch-profiler/core-offline `
  -Experiments offline -Pairs 5

pwsh -NoProfile -File tools/LaunchProfiler/New-Report.ps1 `
  -EvidenceDirectory artifacts/launch-profiler/core-offline
```

The runner enables fixture-only nickname submission, stock warning acknowledgement
after its minimum viewing time, and graceful exit. It does not toggle persistent
warning preferences. The profiler is inert without both its environment variable
and the exact matching `launch-profiler-staging.json` marker. It does not start a
server or connect to remote servers. Timeout cleanup targets only its owned child.

The matrix performs one unscored warmup, captures a private profile snapshot, and
restores it for each alternating baseline/candidate pair. It retains the generated
interop cache. Old per-run profiles are parked in the evidence directory, never
deleted. Run directories and private profile snapshots contain private diagnostics
and generated test identities; publish the reviewed report, not the whole stage.
Place `stop-request.json` in the matrix evidence directory to stop between runs.
The runner records normal exit separately from readiness; forced termination,
lost profiler events, visual rejection, or incomplete treatment invalidate a run.

Experiments and private source variants are described in [EXPERIMENTS.md](EXPERIMENTS.md).
The source transforms are pinned to reviewed hashes and fail if their anchors drift.
They never edit production source. Measured candidates must use the manifest's
tested DLL; the runner restores the baseline DLL and staged trust record afterward.

## Other capture lanes

- `Run-LoadoutMatrix.ps1` compares two private stages in five interleaved pairs.
  Pass `-BaselineGameDirectory`, `-CandidateGameDirectory`, their distinct labels,
  and a new evidence directory. It requires matching game/loader/profiler hashes,
  matching rendered dimensions, and stable inputs within each condition. Profiles
  and deliberate mod inventories differ between conditions and are reported.
- Prepare the optional VR subtraction with `Prepare-Game.ps1 -Loadout Dev
  -DevLocalDirectory <dev/local> -ExcludedModGroups VR`. It removes the complete
  five-library group and retains skins, translations and override assets.
- `Run-OverheadMatrix.ps1` compares milestone and detailed observation using one
  fixed private profile. Installation and observer self-time are also retained;
  these are not a complete estimate of JIT/cache perturbation.
- `Capture-FirstGeneration.ps1` forces supported assembly regeneration, then
  restores and verifies the private cache. Its tool packages and OS file cache
  are warm: this is not a first-download benchmark. Keep this lane separate.
- [NATIVE.md](NATIVE.md) describes the bounded Windows CPU/disk trace and analyzer.
- [LauncherBench/README.md](LauncherBench/README.md) describes preparation-only
  tests in private launcher fixtures, including their network limitations.
- `Run-GameplayValidation.ps1` reuses the existing smoke harness in a private
  client/server pair, preserving candidate binaries and restoring profiles with
  a recovery journal. `-DedicatedDummyControl` additionally exercises actual
  virtual input movement; the default connection-only lane does not. Runtime-only
  deferral hooks are unsupported in this separate lane. Use `-PrepareOnly` to
  review the exact staged validation before launch and `-Recover` after interruption.

## Evidence and acceptance

- `runtime-inputs.json`: source state, arguments, input/profile hashes, external
  QPC frequency/origin, and exact game PID.
- `events.json`, `events.csv`, `trace.json`: bounded capture, dropped-event count,
  exact marked wall intervals, and a Chrome/Perfetto-compatible timeline.
- `result.json`, `runner-result.json`: independent menu/UI and process-exit outcomes.
- Engine PNGs: menu, Mods/settings, and returned menu; inspect them.
- `report.md`, `report.json`, `runs.csv`: raw failed and successful runs, median,
  range, matched pairs, and conservative local noise classification.

A consistent local timing improvement is still a candidate. Before proposing it
for production, verify first connection, controllable gameplay, disconnect, and
menu return against a private loopback server, and report any cost moved into
settings, asset first use, or joining. No public API, persistence migration, wire
protocol change, or shipping optimization is part of this tool.

To roll back a test, exit its owned game process and stop using its dedicated stage.
Runner-controlled skin/binary treatments restore themselves after child exit; a
pending-restoration field in `runner-result.json` means the private stage needs
recovery before reuse. No normal installation needs restoration.
