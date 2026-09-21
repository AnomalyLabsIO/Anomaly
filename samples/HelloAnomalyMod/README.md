# Hello Anomaly: ordinary MelonLoader mod

Start with this sample when making a client mod. It owns its command, key binding,
event subscriptions and scheduled task. Anomaly is a peer API library; this mod
does not reference the runtime `Anomaly.Client.dll`.

Extract the matching developer SDK and start the matching modded game once to
generate MelonLoader references. From this sample directory:

```powershell
dotnet build -c Release -p:AnomalyGameRoot="X:\Games\Anomaly" -p:DeployLocal=false
```

When the sample is copied outside the SDK, also pass
`-p:AnomalySdkRoot="X:\SDKs\Anomaly"` and retain the sibling
`Anomaly.Sample.props` file. Both roots can instead live in an untracked
`Directory.Build.props` alongside your project. Use one matching release set.
The Anomaly source repository is not needed. In a source checkout, the defaults
use its configured local references and API project.

Place only `bin/Release/net6.0/HelloAnomalyMod.dll` in the launcher's selected
loadout `local/Mods/` directory. Approve the changed DLL using the launcher's
existing trust flow, then start the game. Do not copy game, API or loader DLLs
from your build references into Mods.

The Anomaly namespace is `example.hello`; the raw MelonPreferences category ID is
`example_hello`. Keep preference category IDs free of dots and use underscores:
the supported loader treats dots as TOML paths, which can lose saved entries.
Use the raw category ID for `ModSettings.RegisterCategory` metadata too. Dotted
Anomaly command/input/config namespaces keep their existing form.

1. Run `example.hello.hello` in the game console, or press F6 outside text entry.
2. Open **Settings → Mods → Hello Anomaly**. Disable the greeting and try again.
3. Reset the setting to its default and confirm it takes effect immediately.
4. Connect to an Anomaly test server. A one-minute recurring callback starts at
   `ClientEvents.Ready`; disconnecting cancels it, and reconnecting creates one
   replacement task.
5. Use `anomaly.status` or `anomaly.status --copy` for diagnostics.

Unity-facing calls and callbacks in this sample run on the game thread. Subscribe
during initialization: lifecycle events are not replayed to late subscribers.
Ready signals a client scene transition; null-check the local player and other
required state even inside its handler. Enrollment may arrive later. Disconnect
events also cover failed handshake/retry attempts, so cleanup is safe to repeat.
Dispose the input handle and remove
named handlers on deinitialization. Disposal keeps user rebindings for next launch.
Managed modules are the advanced alternative in `../HelloAnomalyModule`.
