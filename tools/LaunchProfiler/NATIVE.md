# Native launch attribution

`Capture-Native.ps1` wraps one existing warm, detailed, baseline `Run-Profile.ps1` launch in a uniquely named WPR session. It combines `GeneralProfile.Verbose` (sampled CPU, image loads and disk I/O) with `FileIO.Verbose`. It never clears caches, changes tracing settings or cancels a session.

The preflight requires `wpr -status` to report no active user recording and checks named ETW sessions via `logman query -ets`. It refuses other WPR sessions. The specifically named built-in Windows `WPR_initiated_DiagTrackMiniLogger` telemetry collectors are recorded in metadata and left running. `finally` stops only the uniquely named instance whose start succeeded. A failed stop is surfaced with the owned instance name and saved logs.

Build the separate analyzer before tracing:

```powershell
dotnet build tools/LaunchProfiler/NativeSummary/NativeSummary.csproj -c Release -p:DeployLocal=false
dotnet tools/LaunchProfiler/NativeSummary/bin/Release/net8.0-windows/NativeSummary.dll --self-test
pwsh -NoProfile -File tools/LaunchProfiler/Capture-Native.ps1 -GameDirectory artifacts/launch-profiler/game-core -EvidenceDirectory artifacts/launch-profiler/native-one -CheckOnly
```

After the ordinary A/B launch matrix finishes, run the same capture command without `-CheckOnly`, adding `-ProfileSnapshotDirectory` for the common warm private profile. It may require elevation for WPR. The wrapper writes the ordinary runner artifacts in `EvidenceDirectory` and native artifacts in its sibling `EvidenceDirectory-native`:

- `launch.etl`: saved native recording.
- `capture.json`, `wpr-start.log`, `wpr-stop.log`: session ownership, profile and cleanup evidence.
- `summary.json`: target-game-PID CPU sample modules, instruction addresses and module-relative offsets, thread sample counts, file operations/bytes and disk response-time hotspots.

The analyzer also isolates the core-init-end → main-menu-start gap and first SimpleMenu Awake-end → Start gap when the detailed managed markers exist. It joins raw monotonic QPC values from the same machine, validates the profiler PID/frequency against runner metadata, and keeps unresolved names explicit. No symbol downloads occur; use WPA with matching native symbols if function names are needed. `xperf` and `wpaexporter` are available on the inspected machine, but this analyzer uses Microsoft's pinned TraceEvent package so the summary does not depend on WPA UI profiles.

```powershell
dotnet tools/LaunchProfiler/NativeSummary/bin/Release/net8.0-windows/NativeSummary.dll --etl artifacts/launch-profiler/native-one-native/launch.etl --run artifacts/launch-profiler/native-one --output artifacts/launch-profiler/native-one-native/summary.json
```

CPU samples are evidence of where the target ran, not precise function durations or proof of what blocked its main thread. File I/O and disk I/O are separate layers; their byte totals overlap. Disk response sums can overlap across requests. The summary excludes events without a target-PID attribution and reports missing events/samples as unavailable. Keep tracing runs out of normal A/B medians because tracing changes overhead. The ETL is a local, system-wide diagnostic artifact even though the summary filters to the game PID.

References: [Microsoft WPR built-in recording profiles](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/built-in-recording-profiles) and [Microsoft TraceEvent 3.1.21](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.1.21).
