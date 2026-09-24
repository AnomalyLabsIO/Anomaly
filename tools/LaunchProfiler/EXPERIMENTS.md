# Private launch experiments

`ExperimentHooks` is called only after the profiler validates the private stage and after MelonLoader's support module has initialized. It has its own Harmony owner, rolls back a failed hook installation, and removes its patches at shutdown. The baseline installs no behavior-changing hooks. These are measurement candidates, not production optimizations.

| Experiment | Behavior | Acceptance boundary |
| --- | --- | --- |
| `deferred-settings` | The stock Mods category is still installed during settings Awake. Its font lookup and row population wait until Mods is selected. A manual `SelectCategory(int)` hook and the scripted settings check both replay the original population method. | Verify Mods rows and controls on first open, reopen and menu return. Include `experiment.settings.populate` in first-interaction latency; work is deferred, not eliminated. |
| `deferred-cleanup` | Defers only `CrashReportWriter.CleanupOldReports` until after menu readiness and an additional update. Crash hooks, watchdog and system-information initialization remain early. Shutdown also completes pending cleanup. | Record `experiment.cleanup.run` and check post-menu frames. Crash-write retention remains unchanged. |
| `identity-once` | Private source variant reuses one decrypted/parsed protected identity and derived public key for structural, lineage and stored-public validation. Buffers are cleared in `finally`. Legacy migration still uses the existing `SetIdentity` implementation. | Existing corruption, migration and atomic-write tests plus decrypt count, zeroization, valid rotation, public mismatch and migration-backup tests must pass. No identity bytes appear in trace output. |
| `unchanged-module-lock` | Private source variant skips only writing a semantically identical `modules.lock.json`, ignoring its `ResolvedUtc` timestamp. Hash checks, dependency resolution, health and startup markers remain unchanged. Malformed, absent, oversized or different locks take the original write path. | Unchanged lock timestamp now means last content change. Include the added comparison read in timing. Six focused policy tests cover identical, absent, malformed and changed contents. |

`no-skins` and `offline` are runner-controlled private-stage fixtures; these hooks do not modify skin files or networking. No experiment changes source in `Anomaly.Client`, `Anomaly.Client.Api`, or `Anomaly.Shared`.

Prepare and validate copied-source variants with PowerShell 7:

```powershell
pwsh -NoProfile -File tools/LaunchProfiler/Prepare-Variants.ps1 -Variant identity-once -Build -Configuration Release
pwsh -NoProfile -File tools/LaunchProfiler/Prepare-Variants.ps1 -Variant unchanged-module-lock -Build -Configuration Release
```

The script copies source into a new directory under `artifacts/launch-profiler/variants`, validates the reviewed source hashes before transforming it, builds with `DeployLocal=false`, and runs isolated .NET Framework 4.8 tests. Use `-ClientRoot` and `-ServerRoot` only to specify the matching reference installation. An SDK-access sandbox failure may require running this same build with read access to the installed Windows SDK; do not replace references to silence it.

Each successful result returns a `variant.json` manifest and the private `Anomaly.Client.dll` path. Stage **only that client DLL**, require `buildSucceeded` and `testsSucceeded`, and verify `clientSha256` against the staged file. Keep the baseline Client.Api/Shared/third-party assemblies fixed. The manifest includes original source hashes; newer manifests also include transformed source hashes. Existing snapshots are retained rather than overwritten or deleted.

Hook compilation and source-variant tests establish feasibility. They do not establish runtime savings or qualify deferred UI behavior: retain repeated matched baseline/variant launches, menu screenshots, Mods-tab interaction and post-menu frame evidence before promoting a candidate.
