# Deterministic release packaging

`Build-Release.ps1` builds the x64 runtime projects, captures each project's
resolved assembly inputs, and invokes this tool. The higher-level
`Invoke-CompleteReleaseGate.ps1` also runs tests, API/compile-list/secret gates,
SDK and negative closure probes, then repeats a clean build and requires
byte-for-byte identical output. The packaging tool refuses incomplete or
unexpected layouts, stages the exact client/server/SDK trees from
`release/package-layout.json`, creates sorted fixed-timestamp ZIP archives,
writes a machine-readable dependency inventory, and verifies every staged and
archived byte before returning success.

Runtime package closure is checked from the assembly metadata in every staged
DLL. Each assembly reference must resolve to another packaged DLL, an exact
framework-provided name, or an exact host-provided name with a documented
provider. Host-provided names must also occur in the captured MSBuild reference
inputs. Missing dependencies, stale provider declarations, duplicate assembly
identities, native files disguised as DLLs, and post-package closure changes all
fail creation or verification. The generated schema-v2 dependency inventory
records every edge and the packaged/framework/host resolution chosen for it.
`release/Test-PackageClosure.ps1` audits a valid release and then removes a
known transitive Harmony dependency from an isolated copy to prove the same
closure engine rejects incomplete payloads.

The developer SDK also carries `BouncyCastle.Cryptography.dll` because public
`Anomaly.Shared.Auth.AuthCrypto` signatures expose Bouncy Castle types.
`reference/sdk-reference-metadata.json` records that compile dependency and the
separately supplied game/interop profile. `release/Test-SdkCompileClosure.ps1`
compiles a public-signature probe from the staged SDK, then removes Bouncy
Castle from an isolated copy and proves the same probe fails.

The dependency inventory records names, sizes, and SHA-256 hashes without
publishing machine-specific absolute paths. `release-receipt.json` binds the
source commit, archive hashes, runtime version, reproducibility timestamp, and
canonical release commands. Every archive carries `LICENSE`, `NOTICE`, and the
same `THIRD_PARTY_DEPENDENCIES.md` whose hash is bound by the inventory. Signing
is a separate operator step so private keys never enter the repository or
ordinary CI workers.

The deterministic packaging pass emits a schema-v3 receipt with explicit
unsigned signing state, no `keyId`, an empty `trustedKeyIds` list, and no gate
evidence. After `Compare-ReleaseOutputs.ps1` succeeds, the complete gate writes
its logs, TRX results, tool/runtime versions, and pass states under `evidence/`
and invokes:

```text
finalize-gate-evidence <output-directory> <gate-summary-json>
```

The command rejects failed/missing gates, missing tool versions, paths outside
the evidence tree, reparse points, unexpected files, and missing TRX results.
It inventories each evidence file by size/SHA-256, atomically updates the
receipt, and runs the full release verifier again. Thus nondeterministic logs
and timestamps never participate in (or mask) the deterministic package
comparison.

After a signed `Build-Release.ps1` pass has independently verified every
ECDSA-P256 manifest, it invokes
`record-signing <output-directory> <key-id> <public-key-pem>`. The command
re-verifies every signature with that P-256 public key, checks all manifest
identities, records only the verified key ID, and hashes the public
manifest/signature/envelope files. It never receives or records private-key
material or the public-key path/bytes.
