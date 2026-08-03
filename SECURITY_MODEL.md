# Anomaly public-v1 security model

## Trust boundaries

- Remote clients, remote servers, network intermediaries, downloaded content, and unapproved local DLLs are hostile inputs.
- The launcher enforces SHA-256 consent across `UserLibs`, `Plugins`, `Mods`, nested MelonLoader-reclassified folders, and managed modules before process creation. `Anomaly.Bootstrap` rechecks ordinary Mods and managed modules before MelonLoader's Mods phase, but cannot retroactively protect UserLibs or Plugins because MelonLoader loads those first. Approved DLLs remain full-trust in-process code: capability declarations are review metadata, not a sandbox.
- Game-version declarations are advisory. Authentication pins, transcript validation, artifact hashes, payload limits, and content-policy failures are security gates.
- The server remains authoritative for admission and game state. Client integrity signals are evidence, never proof.

## Identity and enrollment

Public v1 uses one client identity key and server-local enrollment records. Servers expose opaque local enrollment ids to moderation/plugin APIs rather than a key-derived global id. Reusing one client public key still allows colluding servers to correlate users, and open enrollment still allows identity-ban evasion after key replacement; these are accepted residual risks and must be disclosed in UI.

Planned client-key rotation is represented by a bounded chain of old-key-signed delegations. The chain is stored alongside the DPAPI-protected current private key, carried in the retry-cookie and signed handshake transcripts, and capped at four links. A server validates every link only after the stateless retry cookie succeeds, requires phase two to be signed by the current leaf key, and derives its opaque local enrollment id from the chain root. Removing or losing the chain therefore creates a fresh enrollment; it never silently inherits the prior account. The local `anomaly.rotate_identity confirm` command persists the new private key and public proof together before changing the live identity.

The delegation chain is public and permanent. It makes rotations linkable (the global-key model is already cross-server correlatable), and it provides continuity rather than revocation: a server that has not added its own current-leaf state can still accept an older private key for the same root enrollment. Compromised-key revocation, fork rejection, and lineage compaction beyond four rotations require server-side accepted-leaf persistence and remain release-hardening work; do not describe the current operation as revoking a stolen old key.

### Server-key rotation and recovery limits

The local `anomalyidentity rotate` server-console command now replaces the persistent signing key only after it has created and durably stored a canonical rotation record signed by the old key. The record binds both public keys and fingerprints, a non-zero monotonic sequence, and a bounded validity interval. It is included in the new-key-signed handshake transcript, whose client half binds the normalized connection endpoint and game version. Clients retain the pinned public key and last accepted sequence, automatically replace the pin only after both signatures and all bindings validate, and reject mutation, expiry, rollback, and replay. The server's separate enrollment HMAC secret is retained across signing-key rotation, so moderation identifiers do not change merely because the server key rotates.

This is deliberately a one-hop recovery mechanism. The server retains and advertises only its most recent rotation record for 180 days; a client that misses more than one rotation, reconnects after expiry, or has only a legacy fingerprint-only pin cannot construct the missing chain. In those cases the changed key remains a hard security failure: the in-game client does not offer a pin-replacement prompt. Recovery requires independent fingerprint verification and a separate future launcher/operator trust-reset workflow. An unsigned reset is never described as automatic or signed rotation. Operators should therefore back up the persistent identity, avoid rapid successive rotations, and keep the prior-key proof available for the full migration window. Multi-hop rotation-chain retention, the trust-reset workflow, and an out-of-band signed recovery artifact remain post-v1 hardening work.

## Password access

The legacy PBKDF2 verifier is authentication-equivalent and is not suitable for hostile-internet public mode. RFC 9807 OPAQUE is the required replacement. As of this implementation, the available .NET binding targets .NET 8 only while Anomaly.Server/Shared require .NET Framework 4.8; public password mode therefore remains disabled rather than shipping a custom or unaudited PAKE. Legacy mode is bounded and restricted to explicit migration/loopback use.

## Custom content

Public v1 accepts items only. It does not activate remote roles, effects, generic prefab hierarchies, or post-instantiation script stripping. Item visuals use bounded passive data formats and authenticated, consented catalogs whose cache provenance includes server fingerprint, catalog hash, and content hash.

## Diagnostics

Diagnostic JSON is local, bounded, and redacted. Native and hang dumps are opt-in because process memory may contain authentication material. There is no automatic upload; users review exported artifacts before sharing.
