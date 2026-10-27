# Anomaly public-v1 security model

## Trust boundaries

- Remote clients, remote servers, network intermediaries, downloaded content, and unapproved local DLLs are hostile inputs.
- The launcher verifies the complete official core against a signed release manifest and enforces third-party SHA-256 consent across `UserLibs`, `Plugins`, `Mods`, nested MelonLoader-reclassified folders, and managed modules before process creation. `Anomaly.Bootstrap` rechecks ordinary Mods and managed modules before MelonLoader's Mods phase, but cannot retroactively protect UserLibs or Plugins because MelonLoader loads those first. Approved DLLs remain full-trust in-process code: capability declarations are review metadata, not a sandbox.
- Runtime trust state is `<modded-game>/UserData/Anomaly/mod-trust.json`. General quarantine is `UserData/Anomaly/Quarantine/`; repeated managed-module startup failures use `Quarantine/managed-runtime/repeated-startup-failure/`.
- Anomaly game-version declarations are fail-closed compatibility gates. The launcher enforces the official-core `SupportedGameVersion` declaration before installation/materialization, and the managed-module runtime enforces manifest `SupportedGameVersions` before assembly load. Public v1 uses `14.2.7^` (`>=14.2.7 && <14.3.0`) and does not use MelonLoader's `MelonGameVersion` attribute.
- The server remains authoritative for admission and game state. Client integrity signals are evidence, never proof.

## Authentication-path modification monitoring

Bootstrap installs its guard at minimum plugin callback priority, before ordinary
Mods load. UserLibs and plugin constructors may have already executed, so it also
inspects supported preexisting patch/detour state. The guard retains stock
PatchShield and protects Anomaly.Shared without making Shared depend on the loader.
Its finite, versioned catalog covers supported Harmony/MonoMod operations and
authentication dependencies; it is not a sandbox or remote attestation. Direct
native writes, other hook engines, and reflection mutation between observations
can evade an in-process monitor. The launcher trust boundary remains necessary.

Protocol 8 includes a mandatory six-byte integrity claim in the signed transcript
and encrypted login payload. The server default rejects reported external Harmony
or detour changes, incomplete coverage, and inventory divergence. The server option
`AllowRiskyClientModifications` accepts that reported risk without bypassing other
admission rules. Successfully blocked attempts and the client's external-change
opt-in alone do not trigger rejection. Unsupported claims always fail parsing.

Observed risk and loaded-code inventory survive disconnect, unpatching, disabling,
and unregistering. Admission freezes both inventory and audit; a later relevant
change stops sends and requires a fresh handshake. Changed plaintext must never be
encrypted with an earlier handshake's AES-GCM key/nonce. No additional file-hash
approval is introduced by this runtime monitor. See
[the guard contract](Anomaly.Bootstrap/AUTHENTICATION_GUARD.md).

## Identity and enrollment

Public v1 uses one client identity key per game instance and server-local enrollment records. Servers expose opaque local enrollment ids to moderation/plugin APIs rather than a key-derived global id. Reusing one client public key still allows colluding servers to correlate users, and open enrollment still allows identity-ban evasion after key replacement; these are accepted residual risks and must be disclosed in UI.

The profile, nickname, and identity rotation history are stored in
`<MelonLoader UserData>/Anomaly/user.json`, normally beneath the game directory.
Accepted server identities are stored beside it in `server-trust.json`. A missing
local profile creates a new identity and fresh server enrollments; an existing
local profile is reused. Old `%APPDATA%/Anomaly` data is never imported, used as a
fallback, or deleted. Unreadable identity or trust files retain their existing
failure behavior. Private key protection remains bound to the current Windows
user through DPAPI, or to the game's Wine prefix under Proton; placing the file
beside the game does not make it portable across protection contexts.

Client-key rotation is represented by a bounded chain of old-key-signed delegations. The chain is stored alongside the DPAPI-protected current private key, carried in the retry-cookie and signed handshake transcripts, and capped at four links. A server validates every link only after the stateless retry cookie succeeds, requires phase two to be signed by the current leaf key, and derives its opaque local enrollment id from the chain root. Removing or losing the chain therefore creates a fresh enrollment; it never silently inherits the prior account. The local `anomaly.rotate_identity confirm` command persists the new private key and public proof together before changing the live identity.

The delegation chain is public and permanent. It makes rotations linkable (the
client key remains cross-server correlatable), and it provides continuity rather
than universal revocation. Each server now persists its accepted client leaf and
sequence atomically. It accepts first contact, the exact accepted leaf, or a
contiguous advance that contains that leaf; rollback and competing forks are
rejected after the server has committed a winner. This does not revoke a
currently accepted private key before the server observes a later valid
rotation, and lineage compaction beyond four rotations remains post-v1 work.

### Server-key rotation and recovery limits

The local `anomalyidentity rotate` server-console command now replaces the persistent signing key only after it has created and durably stored a canonical rotation record signed by the old key. The record binds both public keys and fingerprints, a non-zero monotonic sequence, and a bounded validity interval. It is included in the new-key-signed handshake transcript, whose client half binds the normalized connection endpoint and game version. Clients retain the pinned public key and last accepted sequence, automatically replace the pin only after both signatures and all bindings validate, and reject mutation, expiry, rollback, and replay. The server's separate enrollment HMAC secret is retained across signing-key rotation, so moderation identifiers do not change merely because the server key rotates.

This is deliberately a one-hop recovery mechanism. The server retains and advertises only its most recent rotation record for 180 days; a client that misses more than one rotation, reconnects after expiry, or has only a legacy fingerprint-only pin cannot construct the missing chain. In those cases the changed key remains a hard security failure: the in-game client does not offer a pin-replacement prompt. Recovery requires independent fingerprint verification and a separate future launcher/operator trust-reset workflow. An unsigned reset is never described as automatic or signed rotation. Operators should therefore back up the persistent identity, avoid rapid successive rotations, and keep the prior-key proof available for the full migration window. Multi-hop rotation-chain retention, the trust-reset workflow, and an out-of-band signed recovery artifact remain post-v1 hardening work.

## Password access

The legacy PBKDF2 verifier is authentication-equivalent and is not suitable for hostile-internet public mode. RFC 9807 OPAQUE is the required replacement. As of this implementation, the available .NET binding targets .NET 8 only while Anomaly.Server/Shared require .NET Framework 4.8; public password mode therefore remains disabled rather than shipping a custom or unaudited PAKE. Legacy mode is bounded and restricted to explicit migration/loopback use.

## Custom content

Content packs use a strict bounded `pack.json` descriptor under
`ContentPacks/<namespace>/<pack>`. Explicit sections select one Lua entrypoint,
an asset index and/or typed server-side items; unlisted files never execute or
transfer. Item definitions clone stock templates and inherit their gameplay,
model and weight, with custom name, description and optional indexed
Texture2D/Sprite icon. Typed `Data.Items` is not CLR/Unity object activation.
Restricted self-contained Unity bundles support textures, sprites, audio,
static meshes and decorative descriptions, not arbitrary prefabs, shaders,
script-backed objects or native cross-bundle dependencies. The client validates
immutable bytes before Unity loading, but Unity/FMOD/graphics still consume data
in-process: this is not process isolation. See [native limits](ASSETBUNDLES.md).

Lua has one declared entrypoint per logical pack with no modules, OS/filesystem,
network, reflection or automatic CLR interop. Remote execution requires actual
Windows low-privilege AppContainer (LPAC)/job qualification, hash-vetted worker
payloads and separate exact content/execution consent. Standard AppContainer is not
a fallback. Before resume, qualification verifies actual LPAC access behavior on the
duplicated child token, zero capabilities/no loopback exemption and bounded
protected-directory ACL audits; ACL changes do not grant ancestor-directory access.
Controlled network qualification requires same-listener positive controls before and
after, a secret parent nonce and no child connection. Timeout or recognized Winsock
startup error 10107 is accepted only with independently verified LPAC, zero network
capabilities and no loopback exemption. Error 10107 means network unavailable, not
proven permission denial; neither the error nor timeout alone proves isolation.
Windows' enforced LPAC no-network-capability contract is the security authority.
The isolated worker has hard process memory/CPU/process
limits, bounded data-only host RPC and a dedicated non-ThreadPool wall watchdog;
unsupported platforms/Wine or failed probes fail closed with no in-game VM fallback.
`MemoryHardeningQualified` remains false for the separate local owner path, which
requires verified nondedicated in-process hosting. See the
[descriptor and execution contract](Anomaly.Client.Scripting/SCRIPT-PACKS.md).

Consent is bound to server fingerprint, catalog SHA-256, and manifest SHA-256.
A decline requests no files. Item/native-asset materialization, icon, translation
and collision checks stage before all-or-nothing catalog commit.
Active connections keep their immutable catalog; file changes affect future
offers instead of triggering destructive hot reload. SHA-256 blobs are cached
within the game instance under `<MelonLoader UserData>/Anomaly/FileCache/`;
consent provenance is stored separately in `Anomaly/content-consent.json`
beneath the same UserData directory, and invalid cache material is quarantined.
The cache contains downloaded server assets, not game installations.

## Diagnostics

Diagnostic JSON remains local under
`<MelonLoader UserData>/Anomaly/CrashReports/`, is bounded, and is redacted on a
best-effort basis. Cleanup retains at most 20 artifact sets, 256 MiB total, and
30 days. Native minidumps are disabled by default and require
`ANOMALY_ENABLE_MINIDUMPS=1`; process memory can contain private material.
There is no automatic upload, and users must review every artifact before
sharing it.
