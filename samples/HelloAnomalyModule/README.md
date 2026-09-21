# Hello Anomaly managed module

This advanced sample uses `IAnomalyMod` and receives a framework-owned `IModContext`.
Start with `../HelloAnomalyMod` for an ordinary MelonLoader mod.
The context owns commands, scheduled work, cancellation, configuration,
persistence, and registered cleanup resources, so disabling or faulting the
module tears down its framework registrations deterministically.

Build without deploying to an active game:

```powershell
dotnet build HelloAnomalyModule.csproj -c Release -p:DeployLocal=false
```

From the extracted SDK, add `-p:AnomalyGameRoot="X:\Games\Anomaly"` to use
the matching game that has already generated MelonLoader references. If you
copy the sample outside the SDK, retain `../Anomaly.Sample.props` and also
pass `-p:AnomalySdkRoot="X:\SDKs\Anomaly"`. No source checkout is required.

Copy `bin/Release/net6.0/HelloAnomalyModule.dll` to
`UserData/Anomaly/Modules/`. Copy the manifest template beside it as
`HelloAnomalyModule.dll.anomaly.json`, replace `Sha256` with:

```powershell
(Get-FileHash bin/Release/net6.0/HelloAnomalyModule.dll -Algorithm SHA256).Hash.ToLowerInvariant()
```

Every new or changed DLL also requires launcher trust approval by SHA-256.
Approval is a full-process trust decision, not an operating-system sandbox.
The manifest's `example.module` id gives this sample the console command
`example.module.hello`, separate from the ordinary-mod starter.
Managed module disablement is immediate at Anomaly-owned boundaries and fully
takes effect on the next launch; hot assembly unload is not promised on IL2CPP.
