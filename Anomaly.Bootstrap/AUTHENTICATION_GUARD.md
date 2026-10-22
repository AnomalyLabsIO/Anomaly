# Authentication guard contract

The plugin uses MelonLoader 0.7.3's HarmonyX 2.10.2 and MonoMod.RuntimeDetour
22.7.31.1. Do not package a second copy of these runtimes. Bootstrap is a plugin
only; Shared has no MelonLoader dependency and Client.Api has no Bootstrap dependency.

## Policy

`Anomaly.Bootstrap.AllowExternalAuthenticationModifications` is a MelonPreferences
boolean, default `false`, captured once during early initialization. Restart after
changing it. It permits audited changes to catalogued external authentication
methods. It never permits altering Anomaly code, removing mandatory guard hooks,
or overriding stock PatchShield. The server's LabAPI configuration property
`AllowRiskyClientModifications` defaults to `false` and independently decides whether
to accept a valid signed report with risk flags.

In `UserData/MelonPreferences.cfg`, the setting is stored as:

```toml
[AnomalyBootstrap]
AllowExternalAuthenticationModifications = false
```

The category is deliberately flat: MelonLoader 0.7.3's preference writer loses
entries when a category identifier contains a dot. The setting is still owned
by `Anomaly.Bootstrap`; changing it takes effect only after restarting the game.

| Audit flag | Value | Risk |
| --- | ---: | --- |
| ExternalHarmonyModification | 1 | Yes |
| ExternalDetourModification | 2 | Yes |
| CoverageIncomplete | 4 | Yes |
| InventoryDivergence | 8 | Yes |
| BlockedAttemptObserved | 16 | No |

The six-byte record contains schema byte 1, little-endian catalog ushort 1,
little-endian flag ushort, and a strict 0/1 external-modification option byte.
Protocol 8 / login payload 3 bind it into the signature and encryption. Unknown
schema, catalog, bits, malformed inventory, and invalid cryptography always fail.
An absent or failed core guard never creates a clean report.

## Coverage and limits

Harmony checks run before direct/class patch metadata changes, including patch
removal and both reverse-patch endpoints. Approved infrastructure uses exact
method/kind tuples. MonoMod callbacks and lifecycle hooks observe supported apply,
undo, disposal and retargeting paths; known native entry addresses are monitored
only where the adapter can resolve them. Required signatures and callback presence
are checked explicitly. Missing external coverage is reportable only with client
opt-in; missing mandatory monitor infrastructure remains fatal.

Foreign callbacks and IL manipulation run without the monitor's temporary backend
authority. Callback delegate identities remain intact, including ordinary
unsubscription. Required patch tuples cannot be duplicated or removed. Tiered JIT
retargeting is permitted for the exact owned MonoMod backend object and refreshes
the known entry-address cache. Exact generated replacement methods and their
native entries are protected before MonoMod publishes them to other callbacks.
Successful native operations seen before their
source address resolves are retained in a bounded ledger and reconciled later;
overflow is a coverage gap. Blocked and no-effect operations do not advance the
admission generation, while partial or indeterminate failures report a coverage
gap. New bounded diagnostic entries are logged on the main-thread heartbeat.

The catalog covers Anomaly assemblies, loader inventory boundaries, installed
crypto implementations, connection-data writers, connect packet construction and
the managed transport send/retransmission path. Catalog version 1 does not claim
the entire transitive .NET/IL2CPP/native implementation. Foundational runtime
operations, OS/socket internals, arbitrary memory writes and alternate native
detour providers remain trust boundaries. Minimum plugin priority cannot precede
UserLibs, plugin constructors, or every equal-priority plugin. Preexisting supported
modifications are therefore audited too. A clean claim means no changes were
observed under this catalog, not that the process is untampered.

Bootstrap retains primitive mod identities captured before activation where the
loader exposes them. Loaded managed modules remain recorded even if construction
or activation fails. Unregistering/disable never removes a loaded identity;
Plugins/UserLibs are not added to the mod admission inventory. Invalid or conflicting
identities cannot be silently omitted. The client consumes a detached snapshot,
not the live registered-mod list.

Admission is frozen for the handshake. A new relevant change invalidates it;
retries cannot change the encrypted plaintext or reuse that handshake's nonce for
a changed claim. Send checks latch failures without invoking Unity UI off-thread;
the main thread tears down the session. Blocked attempts are informational.

## Qualification

Run the Bootstrap, Shared, Server and relevant API tests with `DeployLocal=false`,
then the fresh-process net6 guard probe against the loader's actual dependency
closure using `tools/BootstrapGuardProbe/Invoke-BootstrapGuardProbe.ps1` (see its
README for bounded execution and evidence capture). Engine-inert unit tests cannot establish loader callback ordering or
IL2CPP hook behavior. Qualify the client and server together in a bounded isolated
loopback run using `tools/EarlyAccessUiSmoke`; keep fixtures out of release packages.
The staged game's static assemblies may be hard links: never alter them on disk.
