# MoonSharp hardening inputs

`third_party/` is ignored and untracked. This directory preserves Anomaly's
reviewable changes and build metadata without tracking the upstream source tree.
The patch targets the 247 interpreter files at MoonSharp commit
`fbd03678d5c0f731d731d099211c7f4a3a1ae8b7`. The manifest pins each original file's
SHA-256; the restore command verifies all inputs before creating a new local fork.

These commands require the full Anomaly source checkout, not an extracted
developer SDK. The SDK includes this guide and `Fork-README.md` for reference;
it does not include the runtime sources, restore script, manifest, or patch.
Its starter plugins use the packaged API DLLs and do not require MoonSharp restore.

Before building the client, scripting host, or runtime releases from a fresh
checkout, obtain the upstream commit above separately (restore never downloads it).
Supply that checkout's `src/MoonSharp.Interpreter` directory:

```powershell
pwsh -File tools/MoonSharpHardening/Restore-MoonSharp.ps1 -UpstreamSourceDirectory <checkout>/src/MoonSharp.Interpreter
dotnet build Anomaly.Client.Scripting/Anomaly.Client.Scripting.csproj -c Debug -p:DeployLocal=false
```

Restore refuses an existing destination. It performs no network access, deployment,
source deletion, Git index changes or automatic replacement of a developer's fork.
The default destination is `third_party/MoonSharp`; an optional `-Destination`
must name a new directory below this repository's `third_party/`.

The patch includes CPU checks and experimental conservative allocation accounting.
The host selects reclaimable quotas: tracked storage regains quota only after observed
CLR collection, with no forced production GC or refund on session disposal. Direct
engine callers retain the cumulative default. Neither mode measures actual live CLR
heap or RSS. Remote script execution remains disabled until memory qualification;
bounded host-operation review and live runtime acceptance remain incomplete.
Verified local hosted source uses a separate internal entry point. Raw remote
source remains rejected.

`Fork-README.md` documents the engine-level limits. `LICENSE` retains MoonSharp's
BSD-3-Clause license; KopiLua attribution remains in the patched source.

After editing the ignored fork, regenerate the patch with Python 3 and the same
verified source input, then restore into a fresh directory and compare/build it:

```powershell
python tools/MoonSharpHardening/Update-Patch.py <checkout>/src/MoonSharp.Interpreter
```
