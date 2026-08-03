# Hello Anomaly managed module

This sample uses `IAnomalyMod` and receives a framework-owned `IModContext`.
The context owns commands, scheduled work, cancellation, configuration,
persistence, and registered cleanup resources, so disabling or faulting the
module tears down its framework registrations deterministically.

Build without deploying to an active game:

```powershell
dotnet build HelloAnomalyModule.csproj -c Release -p:DeployLocal=false
```

Copy `bin/Release/net6.0/HelloAnomalyModule.dll` to
`UserData/Anomaly/Modules/`. Copy the manifest template beside it as
`HelloAnomalyModule.dll.anomaly.json`, replace `Sha256` with:

```powershell
(Get-FileHash bin/Release/net6.0/HelloAnomalyModule.dll -Algorithm SHA256).Hash.ToLowerInvariant()
```

Every new or changed DLL also requires launcher trust approval by SHA-256.
Approval is a full-process trust decision, not an operating-system sandbox.
Managed module disablement is immediate at Anomaly-owned boundaries and fully
takes effect on the next launch; hot assembly unload is not promised on IL2CPP.
