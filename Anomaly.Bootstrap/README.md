# Bootstrap admission monitoring

The guard targets the installed MelonLoader 0.7.3, HarmonyX 2.10.2, MonoMod
22.7.31.1, and SCP:SL 14.2.7 contracts. Loader libraries remain host-provided.
Bootstrap has no reference to Client, Client.Api, or Shared.

Before the Mods phase, Bootstrap verifies runtime protection readiness. Local
mods do not require a `mod-trust.json` approval file, and Bootstrap does not
move mod assemblies into quarantine.

`AdmissionInventoryLedger` records a process-lifetime mod inventory. It subscribes
to assembly loading before enumerating existing assemblies and reads genuine
MelonInfo/MelonID custom attribute metadata without constructing the mod or its
attributes. Plugins and ordinary UserLibs are not mod admission claims. Managed
modules reserve their manifest identity before assembly loading; a failed file
load can cancel its reservation, while observed assembly loading, construction
failure, initialization failure, and later unregistering retain the claim.
The wire core ID remains `anomaly.client`.

Snapshots contain detached immutable ID/version entries. Duplicate identities
from different origins, conflicting original declarations, and unrepresentable
metadata prevent a snapshot. A known mod disappearing from the public registry
or changing its current metadata retains the original claim and reports sticky
inventory-divergence evidence. Custom resolvers without pre-construction metadata
report incomplete coverage and retain the earliest observed valid identity.
In-flight module loads prevent snapshots. Transport generation checks only read
latched state; main-thread capture/heartbeat performs registry reconciliation.
Distinct custom-resolver instances retain distinct origins even when they use the
same wrapper type. Each new divergent metadata/registry state (and restoration)
invalidates an older admission generation, while unchanged repeated checks do not.

`ProtectedMethodCatalog` protects Anomaly assembly methods and enumerates the
critical loader metadata, crypto, serialization, IL2CPP writer, connect, and raw
send boundaries. Required signatures are exact; missing or ambiguous required
targets are coverage failures. Trusted patch exceptions use actual target,
patch method, and patch kind tuples. There is no public owner-string allowlist.
The loader's two default-assembly-context prefixes are matched by exact signatures;
their implementations and PatchShield, including generated callback methods, are
mandatory infrastructure rather than configurable external targets.
Native address mapping runs on the main thread after the support module is ready,
without holding the catalog lock across JIT or native runtime calls. Detour
callbacks only consult the cached mapping. Open generic methods and the runtime's
unsupported delegate `BeginInvoke`/`EndInvoke` stubs have no callable native entry
in this map; their managed protection is unchanged. Concrete required signatures
remain mandatory. Unknown raw addresses,
private detour implementations, arbitrary native memory writes, and reflection
field writes are outside this finite interception catalog. Registry reconciliation
can observe some data changes but is not a general field-write interceptor.
Full coverage requires a completed support-ready native mapping pass; newly
added catalog targets make that state stale until the main thread refreshes it.
Admission can refresh an already initialized map before checking coverage.

This is in-process defensive monitoring, not remote attestation or a sandbox for
hostile full-trust code. Highest registration priority cannot precede every
plugin/UserLib assembly load or constructor. The server receives authenticated
client-reported evidence; server-side admission policy remains authoritative.

## Verification scheduling

Bootstrap owns the recurring verification schedule. Required guard interceptors,
stock PatchShield hooks, registered client patch tuples, callback identities, and
inventory reconciliation are checked once per second. Direct patch-record removal
and callback-field replacement can bypass operation hooks, so these targeted
invariant checks remain necessary. The client consumes the shared evidence and
checks health, active mutations, and admission generations every frame and send.

Scheduled work stops after 1 ms or 32 items per frame, whichever comes first. Each
patch or detour record counts as an item. This is a cooperative budget: a single
runtime call or contended lock can overrun it, and timing diagnostics record those
overruns. No Harmony, MonoMod, catalog, or guard lock survives a frame boundary.

The full Harmony/MonoMod inventory is inspected at startup. Later supported
operations and catalog registrations select exact protected targets for audit;
there is no recurring global patch inventory sweep. Up to 16,384 distinct pending
targets are retained, and overflow reports incomplete coverage. Catalog assembly
observations, resolved bindings, and native addresses are cached separately from
security evidence. Settled static classification misses do not rescan assemblies.

Startup and explicit admission checkpoints may synchronously finish required work
on the main thread. Completed evidence is reused only while the relevant revisions
and completeness still match. Workers consume completed evidence and fail closed
when it is unavailable; they do not resolve native targets or run reconciliation.
This schedule does not make arbitrary private-field or native-memory writes
interceptable. The finite-catalog limits above still apply.

Set `SLVR_BOOTSTRAP_TIMING=1` to emit timing aggregates every 30 seconds.
`GuardSlice` measures frame work and `Checkpoint` measures synchronous admission
calls; totals distinguish completed checks, cache reuse, resolver activity,
individual inspected records, slice items, and indivisible operation overruns.
`SLVR_BOOTSTRAP_FRAME_CORRELATION=1` also records bounded QPC windows for slices
and checkpoints. These opt-in values do not affect guard or admission decisions.

## Mod patch diagnostics

Ordinary mod patches are not automatically deduplicated or removed. Bootstrap
observes successful supported Harmony operations and queues advisory warnings:

- A duplicate warning means more than one active registration shares the exact
  target, patch method, patch kind, and Harmony owner. It is emitted once per tuple
  per process.
- A reinstall warning means three successful installations of that tuple were
  observed within five minutes, with removal between activations. It is emitted
  at most once per tuple per five-minute interval.

Warnings name the Harmony owner and patch assembly. Tracking is bounded to 4,096
tuples and 128 queued records; at most four warnings are emitted per frame within
the shared budget, with suppressed diagnostics summarized. Blocked, failed, and
indeterminate operations do not count as successful installations. These warnings
do not change audit flags or admission generations and do not block mod operations.
See [client patch lifetime guidance](../Anomaly.Client.Api/MODDING.md#patch-lifetime)
for process-owned installation and intentional reinstall handling.

## Startup patch diagnostics

Set `ANOMALY_BOOTSTRAP_PATCH_TRACE` to an absolute log-file path in the launcher
process environment to trace guard installation. Each patch records its ordinal,
full target signature, module identity, and metadata token before entering Harmony,
then records completion after Harmony returns. Entries are flushed to disk so a
native startup crash can leave the last attempted target. The file is replaced
on each process start; use a separate path for each reproduction attempt.
Tracing is disabled when the variable is unset and does not change guard policy.

## Validation

Run `dotnet test Anomaly.Bootstrap.Tests/Anomaly.Bootstrap.Tests.csproj -c Debug
-p:DeployLocal=false`. The net8 tests cover lifetime storage, raw metadata without
construction, exact overload selection, and required signatures in the configured
installed binaries using Cecil. They do not initialize Unity or perform native
detours. Loader-native guard behavior belongs in the separate net6 guard probe
and isolated game/server qualification; unit tests are not live qualification.
