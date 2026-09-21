# Disclaimer

Anomaly is not affiliated with SCP: Secret Laboratory or Northwood Studios. If
you experience an Anomaly problem, report it to AnomalyLabs rather than to
Northwood. This is an unsupported modification of the game.

Central Authentication (Steam/Discord) and local anti-cheat are disabled in the
modded copy. Only Anomaly-enabled servers accept Anomaly clients. Do not try to
connect the modified client to public/online servers, re-enable Central
Authentication or anti-cheat, spoof another user, bypass a ban, or build or
distribute unfair-advantage features. You are responsible for complying with the
game's EULA and for any consequences of using this modification.

# Welcome to Anomaly

Anomaly is a client-server modification framework for SCP: Secret Laboratory.
Public v1 targets:

- Anomaly core `1.0.0` and launcher `1.1.1`
- MelonLoader `0.7.2`
- Windows and Proton
- SCP:SL `14.2.7^`, exactly `>=14.2.7 && <14.3.0`

Anomaly uses its own `SupportedGameVersion("14.2.7^")` metadata. The launcher
enforces that declaration before installing or materializing the official core,
and the managed-module loader enforces the equivalent manifest field before
loading an assembly. Do not add or rely on MelonLoader's `MelonGameVersion`
attribute.

## Installing the client

The launcher creates a separate modded copy of the game, leaves the Steam copy
untouched, and manages official Anomaly updates. The official core is tracked by
default and must be complete, signed by a trusted Anomaly release key, and
compatible with the selected game. Missing, unsigned, tampered, or incompatible
core files block a modded launch; the launcher does not silently start without
Anomaly.

Prerequisites:

- Steam and SCP: Secret Laboratory installed
- A supported Windows environment, or Proton with an unambiguous Wine prefix
- The path to the Steam SCP:SL installation

1. Download the current Anomaly launcher from the official release.
2. Put it in a new directory; the launcher will place the modded game there.
3. Select the Steam SCP:SL directory and choose **Install Anomaly**.
4. Start the modded copy once so MelonLoader can generate its runtime files.

The client runtime package is materialized with this layout, relative to the
modded game directory:

```text
Plugins/Anomaly.Bootstrap.dll
Mods/Anomaly.Client.dll
UserLibs/Anomaly.Client.Api.dll
UserLibs/Anomaly.Shared.dll
```

The signed package also contains its generated runtime dependency closure. A
public third-party repository, repository UI, and deep-link installation are
planned but disabled for v1; they are separate from signed official-core
delivery.

## Installing the server

`Anomaly.Server.dll` is a LabAPI plugin. Install the server package relative to
the LabAPI root as follows:

```text
plugins/global/Anomaly.Server.dll
dependencies/global/Anomaly.Shared.dll
```

Install any additional runtime dependencies included by the signed server
package. EXILED compatibility is not part of the v1 server contract.

On enable, the server applies its offline/modded/IP-passthrough state only at
runtime. It does not persist `online_mode=false` or rewrite `config_gameplay`,
and it restores the captured runtime state on disable. Admission fails closed
while required Anomaly authentication services are unavailable, and vanilla
clients are rejected while Anomaly is operational.

Public remote password authentication is disabled. Setting `RequirePassword`
continues to reject remote clients until a vetted OPAQUE implementation is
available; the legacy verifier is limited to explicit loopback/migration use.

## Identity and enrollment

Anomaly uses a decentralized authenticated handshake and does not contact
Northwood or Steam authentication services. A server assigns an opaque,
server-local enrollment id after authentication. It is not a global launcher id
and is not derived for display from the client's public key.

After connecting, run `anomaly.whoami` (alias `whoami`) in the client console to
display the current server's enrollment id and copy it to the clipboard. The
value is cleared on disconnect. Server operators can use that same id for
server-local moderation records.

The client profile is `<MelonLoader UserData>/Anomaly/user.json`, normally
`UserData/Anomaly/user.json` beneath the game directory. It stores the identity,
nickname, and identity rotation history for that game instance. The first launch
without a local profile creates a new identity; subsequent launches reuse it.
Existing `%APPDATA%/Anomaly` data is left untouched: Anomaly does not import it,
fall back to it, or delete it. A new identity receives a fresh server enrollment.

Private identity material remains protected for the current Windows user with
DPAPI; under Proton it is tied to the Wine prefix running the game. Moving the
file to a different Windows user or Wine prefix may make it undecryptable.
Anomaly preserves an unreadable profile and fails closed rather than replacing
it. The additive `identity_public` profile field is non-secret fingerprint/health
material and is not an `@anomaly` enrollment id.

## Public-v1 custom content

Remote custom content is items-only. A server plugin registers a stable
`modid:name` key, a stock base item, display text, and an optional PNG icon.
Each custom item inherits its base item's model, pickup behavior, and weight.
The only remotely transferred v1 formats are PNG textures and OGG audio; custom
item definitions consume PNG icons only.

An authenticated server offers a frozen catalog. The user explicitly approves
the server-fingerprint/catalog/manifest tuple before any missing files are
requested, and the client activates the staged catalog all-or-nothing. Declining
requests no files and activates nothing. Catalog or file changes apply to future
connections and require consent for the changed tuple; active-session hot reload
is not supported.

Public v1 does not provide remote custom roles, effects, models, weights,
AssetBundles, generic prefab spawning, or destructive content hot reload.

## Making a client mod

1. Install and start the modded client once.
2. Create a MelonLoader IL2CPP project pointed at the modded `SCPSL.exe`.
3. Reference `Anomaly.Client.Api.dll` and `Anomaly.Shared.dll` from
   `UserLibs/`. Do not reference `Anomaly.Client.dll`.
4. Declare `[assembly: MelonAdditionalDependencies("Anomaly")]` when using the
   API during initialization, and declare Anomaly's custom
   `[assembly: SupportedGameVersion("14.2.7^")]` compatibility metadata.
5. Follow [the client API guide](Anomaly.Client.Api/MODDING.md).

Server companions are LabAPI plugins that reference `Anomaly.Server.dll` and
`Anomaly.Shared.dll`.

## Trust, quarantine, and local data

Third-party DLL approval is a SHA-256 full-process trust decision, not a
sandbox. Before process creation, the launcher validates every MelonLoader path
it owns, including `Plugins`, `UserLibs`, `Mods`, reclassified nested DLLs, and
managed modules. Runtime trust state is stored under the modded game at
`UserData/Anomaly/mod-trust.json`; unapproved or unhealthy modules are moved
under `UserData/Anomaly/Quarantine/`. Repeated managed-module startup failures
use `Quarantine/managed-runtime/repeated-startup-failure/`.

Anomaly-owned client storage uses `<MelonLoader UserData>/Anomaly` for this
game instance. Accepted server identities are saved in `server-trust.json`.
Downloaded server content is cached by SHA-256 under `FileCache/`; corrupt,
orphaned, or temporary blobs are reconciled or moved into its `.quarantine/`
directory. Consent provenance is stored separately in `content-consent.json`.
This cache contains server assets, not game installations.

Bindings, overrides, managed modules and their config/data directories keep
their existing locations beneath this root. Skins remain in `UserData/Skins`,
translations in `UserData/i18n`, and mod settings/state accessed through
`ClientConfig` or `ClientPersistence` remain backed by MelonPreferences.

Crash diagnostics remain local under `UserData/Anomaly/CrashReports/`. JSON
reports are bounded and redacted on a best-effort basis, no report is uploaded
automatically, and cleanup retains at most 20 artifact sets, 256 MiB total, and
30 days. Native minidumps are disabled by default; setting
`ANOMALY_ENABLE_MINIDUMPS=1` opts in to captures that can contain private process
memory. Review every artifact before sharing it.

## License

Anomaly core and its in-repository documentation are licensed
`LGPL-2.1-or-later`.

Copyright (c) 2026 AnomalyLabs contributors.

The separate Anomaly launcher retains its GPL-3.0 license.
