# Connection-bound content and scripting

The authenticated content offer contains data-only asset packs and script packs.
A generated schema-1 script-pack index binds exact source addresses, byte counts,
SHA-256 digests, API compatibility, and message schemas. Lua source files travel
through the existing file-transfer protocol and are validated before they reach
the interpreter. Raw remote source strings are not an execution entry point.

Remote execution is disabled. `RemoteExecutionEnabled` follows
`MemoryHardeningQualified`, which remains false:
accounted allocation leases do not impose a qualified CLR heap ceiling or intercept
arbitrary approved C#/native work. See [script authoring](SCRIPT-PACKS.md),
[host constraints](README.md), and the [allocation audit](ALLOCATION-AUDIT.md).

An offer requiring scripts is declined before content consent, download or VM
creation while this gate is closed. The initialization order below describes the
path after qualification; local owner execution has its own host proof and entry point.

## Acquisition before a player exists

`ConnectionBootstrap.Claim` claims the accepted peer and installs directional AEAD
transport after the signed, transcript-bound login and server identity checks.
Bounded authenticated bootstrap traffic can then flow without a ReferenceHub,
player object, or Mirror Ready. This is an authenticated connection phase.

The server admission owns an immutable snapshot lease, exact catalog and manifest,
fresh transfer session, consent state, and fixed monotonic 120-second deadline.
Catalog delivery, consent, file transfer, source/index and asset validation,
execution approval, and initialization consume that deadline. Progress, duplicate
metadata, or unfinished replacement generations cannot extend it. A legitimate new
facility generation after admission gets its own admission scope and deadline.

## Remote initialization order

1. Claim the authenticated connection and begin admission. Send the facility state
   when available, then the exact content catalog and file manifest. The offer binds
   every script index and source file in addition to ordinary asset content.
2. Obtain exact content consent before file requests. A persisted approval can
   apply only to the same server fingerprint, catalog hash, and manifest hash.
   Cache hits still require a fresh session-bound request and validation.
3. Transfer missing files under the existing chunk, count, byte, hash, and idle
   limits. For scripts, validate the generated index, manifest kind/size/hash,
   strict UTF-8 source, exact inventory, API version, and aggregate bounds. For
   assets, run the complete existing managed bundle validator before native loading.
4. Stage the complete validated content transactionally. No scripts run while the
   separate execution approval is pending. The prompt shows authenticated server
   identity, source count, and source bytes; remembered content consent does not
   authorize execution. An empty source inventory needs no execution prompt.
5. After approval, register the item catalog and initialize a connection-owned
   `ConnectionScriptSet`. Freeze and validate all sources before creating the first
   VM. Execute each source in ordinal `PackId`, then ordinal `Address` order, in a
   separate VM per source, checking current ownership before and after every load.
   Install pack-bound Messages before loading. No module imports or shared globals
   are supplied across files.
6. Only successful local asset/item/script initialization permits Activated.
   Missing approval, stale scope, invalid source, syntax error, invalid load result,
   or terminal CPU/deadline/depth/allocation failure rolls back every staged script
   and subscription, revokes content, and closes the connection. Required scripts
   cannot be skipped to obtain a partial admission.
7. After exact Activated, the server sends `ContentBootstrapReadyMessage` with the
   transfer session ID and both offer hashes. The client requires this confirmation,
   successful local initialization, and completed facility generation before
   releasing pending Mirror Ready/AddPlayer and public ClientReady. Every release
   checks the absolute deadline, including when facility state is unavailable.
8. The server independently guards `OnServerReady`, `OnServerAddPlayer`,
   `SetClientReady`, and `AddPlayerForConnection`. Pending, declined, failed,
   expired, or replaced admission cannot ready/create a player. Hub adoption retains
   the completed offer and grant rather than issuing a second offer.

The pre-player allowlist (`Anomaly.Shared/Networking/ContentBootstrapPolicy.cs` in the source checkout)
contains only bounded facility/content metadata, transfer traffic, confirmation,
and matching consent/request/ack contracts. Direction, authentication phase,
payload size, rate limits, AEAD, and replay checks still apply. General gameplay,
third-party messages, and script messages do not enter ordinary dispatch through
this bootstrap exception. Initialization must not wait for an application reply.

`Messages.On` handlers are present before admission completes. The server can send
initial application state from `ClientEvents.ContentReady`, after its adopted
content grant is committed. Receives enqueue bounded snapshots and never call Lua
synchronously. `Messages.Send` remains `NotReady` until pack activation, admission,
and player transport are current. See [message contracts](MESSAGING-DESIGN.md).

## Connection ownership and revocation

Readiness, scripts, and message authority belong to the current native connection,
transport generation, and exact offer/transfer state. Clearing or replacing crypto
material advances that generation and revokes pending work even if a rekey keeps
the same native connection pointer. Consent callbacks and asynchronous completions
carry offer generations; stale completions cannot activate a replacement offer.

Disconnect, authenticated facility-generation replacement, host stop, and shutdown
invalidate subscriptions, queued work, scripts, and content ownership. Cleanup does
not call untrusted Lua unload hooks or reuse a VM across reconnects. Retirement of
any required VM after admission is a content failure and closes the connection.
Initial scene arrival preserves the offer prepared before the scene was available;
a scene notification alone is not a new authenticated offer.

Failed or declined acknowledgment gets at most 250 ms to flush after keys are
revoked. Its delayed disconnect belongs to the original native connection and
transport generation. Replacement connections and later rekeys cancel that grace
action, including before the coroutine first resumes. The server independently
rejects failed or declined acknowledgment.

Server admission identity is the actual authenticated connection/session, not a
reusable numeric connection ID. New generations invalidate old consent/transfer
state. A current FileRequest session must be accepted before activation, and ordered
replay state rejects acknowledgment from an earlier generation.

Connected, scene-loaded, facility-ready, content-initialized, native Ready, and
public ClientReady are distinct milestones. Connected or scene-loaded events do
not grant execution permission. Public events are not replayed: loaded scripts
observe future ClientReady but cannot observe an earlier Connected retroactively.

## Local nondedicated host

Local owner packs under `<UserData>/Anomaly/ContentPacks/<namespace>/<pack>` use
the same strict `pack.json` descriptor and source/index validation, with one
declared entrypoint and no modules. Their sources remain inert in menus, offline
play, dedicated-server mode and remote connections. Local content fallback never
executes owner scripts while joining a remote server.

Execution requires a current nondedicated in-process host: NetworkServer is active,
NetworkClient is an active host, and the server local connection and current client
connection form a nonnull reciprocal pair. A localhost/loopback address alone is
insufficient. This proof exists before a hub/player is spawned and does not borrow
the later administrator-grant identity check.

The client waits for that pair when necessary with a bounded readiness wait. It
initializes validated local sources into a fresh connection-owned set before public
ClientReady and checks current host identity on subsequent ticks. This local path
does not promise to defer Mirror native player creation as remote admission does.
Host stop, disconnect, scope changes, and shutdown revoke the set. Ownership supplies
local authorization; remote packs require fresh execution approval.

## Evidence and acceptance limits

Offline tests cover strict source decoding, exact identity, ordering, initialization
rollback, CPU/allocation limits, queues, subscription cleanup, stale offers,
same-pointer rekeys, consent, admission, and transport guards with inert native
boundaries. Builds check configured interop signatures. They do not prove live
SCP:SL spawn ordering, hard CLR heap containment, or that a modified client executed
supplied logic. Gameplay remains server-authoritative.

Live acceptance needs missing-file and cache-hit offers, empty sources, approvals
and declines, expired prompts, validation/startup/callback failures, disconnect and
rekey during initialization, first-scene and facility-generation ordering, verified
local hosting versus remote loopback, and slow/missing facility bootstrap. Failure
must revoke only the affected scope, hold remote readiness, and prevent callbacks
from spilling into a replacement connection.
