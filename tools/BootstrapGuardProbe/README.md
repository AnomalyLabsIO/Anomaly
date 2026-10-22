# Bootstrap guard runtime probe

This isolated .NET 6 executable exercises the actual loader-provided HarmonyX and
MonoMod assemblies without starting the game. It requires the configured local
MelonLoader references and the .NET 6 runtime. Do not deploy the probe to `Mods` or
`Plugins`, and do not substitute .NET 8 for these runtime-hook checks.

From the repository root, use PowerShell 7 and a new evidence directory:

```powershell
pwsh -File tools/BootstrapGuardProbe/Invoke-BootstrapGuardProbe.ps1 `
  -LoaderDirectory 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy\MelonLoader\net6' `
  -EvidenceDirectory 'artifacts/guard-probe-run-01'
```

The runner builds with `DeployLocal=false`, then runs `strict`, `allow`,
`preexisting`, `stocktamper`, and `scheduling` in separate processes. Each process is limited to
30 seconds. Its PID, start/end time, exit code, timeout status, stdout and stderr
are saved in the evidence directory. A failure stops the suite. `-SkipBuild` uses
the existing build; `-Modes strict,allow` narrows the run.

To check the harness error path without installing or exercising hooks, use
`-CheckFailureHandling` with a separate evidence directory. It intentionally
omits the executable's arguments and requires a handled error with exit code 1.
The executable catches managed failures, and disables Windows crash dialogs for
its own process only. No system crash-reporting settings are changed.

The runtime checks cover direct/class/reverse Harmony calls, all patch kinds,
required-patch removal and duplicate protection, MonoMod lifecycle and ordering,
native source identity, callback authority isolation and normal unsubscribe,
opt-in rebuilds alongside required prefixes, and no-effect versus applied
operations. The final callback-tamper assertion intentionally makes that isolated
process unhealthy; its final risk flags are not a clean-start acceptance claim.

The `scheduling` mode checks cached main/worker checkpoints, completeness-aware
worker rejection, pending catalog registration, deduplicated required client
descriptors, and authoritative detection of a raw removed client prefix. It
asserts operation counters for checkpoint reuse and unchanged catalog work rather
than measuring wall-clock speed. It enables the external-modification option to
check worker reuse after a completed assessment publishes explicit incomplete
coverage evidence. Installed hooks are also exercised for direct/class duplicates,
reinstall churn, ordinary two-install replacements, and blocked attempts, while
checking that advisory diagnostics do not alter admission evidence. A probe-only
96-record metadata cohort verifies the 32-item slice limit and one item per patch
record without installing extra native wrappers. Its final raw-removal assertion intentionally
leaves that process unhealthy. Worker calls have a five-second bounded join.

The probe does not establish IL2CPP behavior, native memory integrity, inventory
completeness or client/server authentication. Those require their separate tests
and the ordinary ModLaunch client/server qualification path.
