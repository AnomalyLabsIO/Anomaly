# CLR and host allocation audit

This audit covers the restricted development host and pinned MoonSharp fork
`3.0.0-anomaly.5`. It does not qualify a CLR heap ceiling.
`MemoryHardeningQualified` and `RemoteExecutionEnabled` remain false. Remote
script offers are rejected before consent/download and again at the connection
and runtime entry points. Local owner sessions use a separate internal entry point
after the client proves the current nondedicated host connection.

## Closed paths

| Path | Allocation control and ownership |
|---|---|
| Compiler, lexer and source metadata | Lists, dictionaries, parameter-name sets, builders, symbols, source references, closure symbol copies and bytecode capacity reserve before growth. Capacity remains charged until the backing collection is collected. Conservative source scratch estimates remain additional charges. |
| Ordinary Lua exceptions | Exception object/throw bookkeeping has a conservative 8 KiB allowance; formatted messages, decorated source locations and stack-trace copies are accounted separately. Lua may catch ordinary errors, but cannot catch memory/CPU termination. |
| Diagnostic formatting | Bounded scalar/string formatting preflights output, explicitly reserves and sizes its builder, and separately accounts the resulting string. Large alignment/field widths are rejected before large output allocation. |
| Primitive and tuple tostring | Output strings/builders and traversal are accounted. Base conversion scans whitespace without allocating a trimmed input copy. |
| Random reseeding | Seeded Random state and its retained wrapper are accounted before replacement. |
| Bindings and reflection | Argument/setter arrays, primitive boxes, scalar return boxes, descriptors, generated names and conservative reflection workspace are preflighted and bound to storage. Script-to-C# strings are bounded independent copies, so host retention keeps their charge. |
| Session work | Session/resource/event backing stores have fixed bounds; queued arguments are validated and copied inside the owner budget. Per-entry budgets and externally retained results have owners. Diagnostics use a prepaid bounded allowance, including when only their string escapes. Terminal responses are shared and need no fresh allocation after exhaustion. |
| Event publication | Token strings, nodes/descriptors, frozen graphs, invocation and publication metadata have backing owners. Nested snapshots retain the shared publication owner. Same-source getter reentry fails closed before recursive capture; foreign-thread publication retires without allocating a recipient snapshot. Fatal bookkeeping/capture errors invalidate selected recipients. |
| Pack messages | Schemas and subscription dictionaries have bounded retained storage. Events and Messages share 32 registrations and 64 queue slots. Queued payloads have a separate 64 KiB limit, checked before snapshotting; incoming validation has reserved transient workspace, and snapshots retain their allocation ticket until collected. Outbound flat records reserve bounded workspace before raw-table conversion and bind retained envelopes; Lua table/string conversion remains inside the same execution and memory budgets. |
| Script-pack parsing | A separate JSON profile caps depth at 5, value nodes at 4,608, arrays at 32 entries, objects at 5 properties and strings at 255 characters. The 64 KiB index cap applies before decoding. AssetBundle parser defaults are unchanged. The supported 32-schema/32-field shape is tested. |
| Source snapshots | The complete source metadata in each index is checked before source reads. Identical hashes share one private source decode. Local reads are capped by the remaining 256 KiB aggregate allowance before allocating buffers. Prepared remote inventories transfer ownership once; connection routing retains only pack/index identities. |
| Source validation and capability installation | Canonical identity and schema checks avoid scratch collections. Empty-source validation allocates no identity strings or closure state. Failed message installation retires and removes the unpublished VM; retiring a session drops its message capability reference. |
| Messaging control plane | The connection bridge has a conservative 2 MiB parent-budget reservation bound to its lifetime, covering up to 8 packs, 256 schemas/8,192 field descriptors and bounded rate buckets. Grouping scratch is separately prepaid. Session copies discard producer schema lists, cache three capability wrappers, and use allocation-free frozen schema checks. |
| Annotated gameplay queries | Metadata discovery installs only annotated types/members/events before VM construction. Nullable and the annotated firearm/motion structs retain the existing snapshot node/payload bounds. Reflection reserves conservative primitive/struct result boxing allowances; detached position values are charged before construction. Game-context descriptors belong to each session. |
| Owned output | Primitive-only output validates borrowed strings/durations and rates before copying. Logs are single-line/bounded; display strings reserve a conservative 256 KiB native workspace plus their copied storage before entering the sink. Runtime/session rate caps, eight lazy owner slots, fixed geometry and host expiry bound creation/churn; disposal clears only the owner. Native Unity/TMP internals remain a trusted boundary, not an intercepted allocator. |
| Observation scheduling and diagnostics | Repeated fair rounds admit up to four callbacks within a 2 ms no-new-start window; each gets an independent at-most-10 ms terminal wall allowance. Explicit latest observations retain one pending snapshot per registration generation. Replacing roots never refunds live storage. Session source/event diagnostics use a fixed prepaid allowance and at most four bounded emissions. |

Reclaimable accounting follows collection, including finalization/resurrection. Neither
Remove/Clear, entry completion, disposal, reconnect nor abandoned child-budget wrappers
can refund storage that remains live. Unknown-lifetime direct Charge remains permanent.
Production observes collection and never forces game-wide GC.

Script index/source decoding is bounded admission work before any VM exists, not
a measured per-session heap. Decoded sources are capped at 8 logical entries and
256 KiB of UTF-8 source, with at most one 64 KiB index graph processed at a time.
These bounds constrain admission peaks without treating disk/cache data as a VM allowance.

## Regression evidence

`ClrAllocationExecutionTests` exercises preflight for exceptions, source locations,
formatted/aligned errors, scalar/tuple output, reseeding and compiler containers; a
512 KiB base-conversion input avoids a trim copy. Its isolated error loop accounts
and reclaims 1,024 discarded ordinary errors.

`HostAllocationTests` checks primitive boxing, descriptor/reflection preflight, fixed
resource/queue bounds, retained string arguments and diagnostic strings, bounded
binding churn, and a reentrant event getter. Worker watchdog/process/GC failures are
test failures. Explicit collections and actual-thread allocation measurements occur
only in isolated workers, and do not measure a per-script live heap.

Source-pack regressions exercise hostile parser shapes, maximum supported schemas,
remaining-byte preflight, physical-source decode sharing and single-use prepared
snapshots. Actual-thread measurements check allocation-free canonical empty-source,
schema and message-scope validation. Weak-reference tests ensure completed setup
does not retain producer schemas or source inventory records, while retired session
wrappers cannot retain their VM through the messaging capability. Initialization
failure tests verify capability rollback and refusal before any remote VM allocation.

### Active-invocation CPU work (2026-10-26)

The interpreter now installs/restores a thread-local active execution scope around
VM processing and existing nested scopes. Arithmetic string-to-number coercion
charges input length before parsing. String-key dictionary lookups, insertions and
removals charge before hashing/mutation. Prime/shared tables charge traversal,
length scans and dead-key collection to the current invocation, not a captured
creation-time budget. The allocation owner remains independent of the work owner.

Seven finite guarded regressions completed incorrectly before the fix with only
512 fuel: numeric coercion, three prime-table traversal paths, and long-key reads,
writes and insertions. They now require terminal `CpuLimit`; a bounded useful-work
case still completes. Inputs are retained 128 KiB strings or 4,096-entry tables,
prepared before a fresh invocation budget. `tonumber` and string arguments passed
directly to callbacks already had entry charges; those paths were not changed.

Both Debug and Release scripting suites passed all 518 tests. A separate standalone
production-host smoke used 512-entry tables and 8,192-character strings within the
unchanged session allocation allowance. All seven finite workloads returned
`CpuLimit`, retired their session and rejected later invocation/queueing; a healthy
sibling completed 57 invocations and owned logging still worked. The initial
4,096-entry Lua setup hit `MemoryLimit`, so the smoke reduced table size and
increased bounded traversal repetitions rather than relaxing production quotas.
This exercises the managed host/coroutine path, not Unity/native calls.

Tracked restore inputs rebuilt the fork from the hash-verified pinned upstream
revision. All 253 restored C# sources matched the active fork after newline
normalization; six added files differed only in line endings. Local evidence is
under `artifacts/vm-containment-20261026/`. These CPU checks do not qualify memory
containment or change the remote-execution gate.

## Remaining trust and runtime boundaries

An arbitrary approved C# getter/method can allocate or retain data inside its body
before returning; reflection workspace/output reservations cannot intercept it. A
finite regression deliberately demonstrates an uninstrumented 64 KiB host allocation
and keeps memory qualification false. Each future capability needs bounded,
reviewed work and explicit reservations/ownership for its own large buffers and native
resources. An annotation alone is not a memory boundary.

The annotated gameplay surface does not qualify those native getters. Player/room/item
resolution, wrapper cache misses, stat queries, IL2CPP strings and firearm/module reads
can allocate in trusted API/Unity code outside the VM allowance. Native cache/population
and teardown behavior still require live review. The bounded owned-text capability
now creates native TMP labels, but its 256 KiB workspace reservation is conservative;
native font/material/geometry/GC use is not a qualified allocator ceiling.
Other [future mutation bindings](CAPABILITIES.md) need their own budgets.

CLR-private exception trace/diagnostic internals, JIT/type/reflection/culture caches,
GC metadata/fragmentation and native/IL2CPP/Unity allocations are not exact script-owned
measurements. The exception and container allowances are conservative estimates;
framework caches are trusted runtime overhead, not proven per-VM containment. Memory
pressure before natural GC may still reject otherwise bounded scripts. No managed OOM
recovery, uninterrupted frame-time guarantee, or live Unity qualification is claimed.

Excluded modules (filesystem/OS/network/debug/automatic interop, module loading,
bytecode streams and dumping) remain unavailable; this audit does not qualify their
allocation paths. Script-pack initialization uses these same restricted libraries and limits;
these checks do not remove the remaining CLR/native trust boundaries or qualify remote execution.

Pack schema validation and wire decoding are host admission work, outside the VM's
per-session allowance. They are separately bounded: 32 schemas per pack, 256 across
the active source set, 32 primitive fields per record, 1 KiB strings and 4 KiB encoded
records. Per-activation rate buckets have at most 256 pack/name entries. Both directions
limit messages and bytes, and sends stop when native reliable queues reach 128 packets.
These bounds limit this bridge's contribution; they do not turn native transport queues,
CLR metadata or the existing shared network machinery into a measured per-script heap.

## Enablement decision

Do not enable hostile remote execution on this evidence. The fixed admission and
bridge bounds close the source-pack gaps, but a VM allowance still cannot interrupt
or account arbitrary C#/native work inside an approved operation. The existing
finite uncooperative-host regression demonstrates that limitation. Production
capabilities need audited, bounded bodies with reservations before allocations,
including native resources; a hard process-wide ceiling requires an external
boundary. Current tests do not qualify live Unity/IL2CPP GC behavior or native use.

## Local-host acceptance evidence (2026-10-26)

The opt-in `EarlyAccessUiSmoke -UseModLaunch -LocalHosting -ClientScripting`
scenario passed against the supported game/loader through both console `host`
and Create Server. Evidence is retained locally under
`artifacts/content-packs-cutover-20261026/local-host/`: descriptor-selected sources,
native Mirror readiness, reciprocal host proof and the prepared set were verified;
the real sample rendered player/round text. Each hosted connection exercised 120 frames with two
queued native-query callbacks per frame, literal/noninteractive owner labels,
independent clear/expiry and disconnect cleanup. Returning to the menu and rehosting
created fresh sessions. No production GC was forced. The reversible deployment was
restored and its protected native/unrelated hashes matched.

This closes the previously unexercised local readiness/output path, not the
hostile remote containment decision. It covers one local player in the lobby,
not multiplayer role/despawn churn, long-running GC behavior or the complete native
getter graph. Runtime counters in the evidence are outstanding conservative
charges and cumulative reservations, not measured CLR/native heap ceilings.
The experimental allowance still cannot intercept arbitrary allocations or stop a
blocking native body. Remote qualification therefore remains false; a hard
availability boundary still requires qualified bounded host operations and,
where hard isolation is required, an external execution boundary.
