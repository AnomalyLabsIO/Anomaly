# Bootstrap startup regression probe

This .NET 6 probe loads the actual built `Anomaly.Client.dll` in a fresh process
for each scenario. It does not launch SCP:SL or initialize Unity/IL2CPP.

Build Client and Bootstrap first with `DeployLocal=false`, then run from the repository root:

```powershell
pwsh -NoProfile -File tools/BootstrapStartupProbe/Invoke-BootstrapStartupProbe.ps1 `
  -LoaderDirectory 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy\MelonLoader\net6' `
  -ClientAssembly 'Anomaly.Client/bin/Debug/net6.0/Anomaly.Client.dll' `
  -BootstrapAssembly 'Anomaly.Bootstrap/bin/Debug/net6.0/Anomaly.Bootstrap.dll'
```

The runner builds the probe unless `-SkipBuild` is supplied. Use `-Configuration
Release` with matching Release assembly paths when testing a Release build. Each
child process has a 30-second limit.

Unexpected managed failures are caught, written to stderr, and return exit code 1.
The probe disables Windows crash dialogs only for its own process; it does not
change system error-reporting settings. Expected startup rejection remains exit 78.

Expected results:

- **Missing Bootstrap:** Client's assembly loads and `Core` constructs; the
  non-inlined `PullUpBootstraps` call raises a catchable `FileNotFoundException`.
  The actual `OnEarlyInitializeMelon` catches it and terminates with exit code 78.
- **Failed Bootstrap:** the loaded plugin's startup state is set to `Failed`;
  `PullUpBootstraps` returns false and the actual early lifecycle exits with code 78.

Both scenarios verify that Client's `Core.Instance` is still unset before the
early lifecycle rejects startup. The probe substitutes only
`MelonLogger.Error(string)` with a console sink because its normal implementation
requires the native loader. It does not substitute the startup guard or
`Environment.Exit`. These checks establish managed loading and failure behavior;
they do not replace validation through the normal ModLaunch game startup.

The target intentionally matches the client's .NET 6 runtime, so newer SDKs may
emit `NETSDK1138` about that target's support status.
