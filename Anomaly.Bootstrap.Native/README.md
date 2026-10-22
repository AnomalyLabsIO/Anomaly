# Win64 code-integrity prototype

Run `pwsh Anomaly.Bootstrap.Native/Build-Native.ps1 -Configuration Debug` from the
repository root with the Visual C++ x64 Build Tools installed. The script writes
`Anomaly.Bootstrap/bin/Debug/net6.0/Anomaly.Bootstrap.Native.dll`. The Release
configuration writes under `bin/Release/net6.0`.

For an explicit test deployment, copy the DLL to the game's
`UserData/Anomaly/Native/Anomaly.Bootstrap.Native.dll` and enable
`Anomaly security / Enable native code integrity prototype`. Keep the setting
off if the DLL is not installed. Neither the native build nor deployment runs as
part of an ordinary .NET build.

The exported functions baseline up to eight executable regions of at most 32
bytes and compare their bytes and page protection using Win32. Bootstrap
currently registers only a dedicated canary. The live SCP:SL/ModLaunch probe
verified that a direct byte change to the canary was caught, the game exited
with code 78 without a prompt when configured, and an unprotected nearby
Harmony patch still ran. This is a mechanism test, not JIT-body coverage or
same-process attestation.
