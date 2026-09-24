# Launcher preparation benchmarks

These commands create new, private output directories and never start the game. No sibling repository source is edited. Keep results separate from process-to-menu measurements.

Build with `dotnet build tools/LaunchProfiler/LauncherBench/LauncherBench.csproj -c Release`. The project references the existing full-launcher Release binary (its exact SHA-256 is reported), and compiles unchanged current Lite core sources through linked compile items. Override `LauncherBin`, `LiteSource`, or `MelonNet6` MSBuild properties when needed; absent dependencies fail explicitly.

Run the full-launcher local-development materialization experiment:

```powershell
dotnet run --project tools/LaunchProfiler/LauncherBench/LauncherBench.csproj -c Release --no-build -- materialize E:\Projects\AnomalyLabs\ModdedFiles\loadouts\dev\local artifacts/launch-profiler/launcher-materialize 5
```

This copies only mod/plugin/library files and skins/i18n assets, uses real `LoadoutMaterializer` and link activation, and alternates baseline with a harness-only incremental-copy experiment restricted to this local-development fixture. Both retain local-development trust behavior. The experiment preserves a synthetic client cache sentinel; baseline currently deletes it. Output hashes must match. No real identity is copied. Timings describe preparation and junction creation, not game startup.

Run Lite's exact cached preparation validation against an idle, restored installation:

```powershell
dotnet run --project tools/LaunchProfiler/LauncherBench/LauncherBench.csproj -c Release --no-build -- lite-pinned 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy' artifacts/launch-profiler/lite-pinned 5
```

All relevant original game files, loader files and prepared payloads are physical private copies. The transport uses an immediate network-unavailable fixture: it isolates validation overhead and does not measure real network delays. A cache miss always fails before patching or regeneration. The pinned candidate still probes .NET, hashes the exact game inputs, validates every prepared payload/archive/runtime and revalidates runtime before returning. No journal is created or activated, and no restoration checks are removed.

Results retain every failure and preparation fingerprint in `report.json`. Inspect matched successes before computing speedups. Neither experiment is a production change or an official-package security qualification.
