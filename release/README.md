# Anomaly release production

Run the complete unsigned release gate from the repository root:

```powershell
./release/Invoke-CompleteReleaseGate.ps1 -Version 1.0.0
```

This command runs the private-key marker scan, explicit compile-list check,
Release/x64 warning-as-error build, every core test, shipped API baseline,
release-signing tool tests, runtime package closure tests, and staged SDK sample
compilation. It then performs a clean second build and requires the complete
unsigned release output to be byte-for-byte identical. Deploy-local is disabled
throughout. Controlled CI supplies the client/server/LabAPI reference-root
properties documented in `Directory.Build.props`; missing inputs fail with the
exact property and path.

Only after the two unsigned outputs compare byte-for-byte does the gate add an
`evidence/` directory to the primary output and finalize schema-v3
`release-receipt.json`. The receipt binds every per-gate command log and TRX
result by relative path, size, and SHA-256; records the pass state and UTC
interval for each gate; and records the exact .NET runtimes/SDK, PowerShell,
Git, and ripgrep versions used. Source commit, dependency-inventory SHA-256,
and package archive SHA-256 values remain in the same receipt. The unsigned
gate explicitly records `signing.state=unsigned`, a null `keyId`, and an empty
`trustedKeyIds` list. `Test-ReleaseEvidence.ps1` independently re-verifies the
receipt and every retained evidence file, then proves a modified copy of a
command log is rejected before the gate reports success.

Both the complete gate and the lower-level packager require a clean worktree
and require `SourceCommit` to equal the checked-out commit. This prevents a
receipt from attributing uncommitted source bytes to an unrelated Git object.
Finish and review the implementation, commit it deliberately, and then create
the release evidence from that clean checkout.

`Build-Release.ps1` is the lower-level build/package/signing command. It builds
unless `-SkipBuild` is supplied, captures and hashes resolved inputs, stages the
declared payloads, emits canonically ordered commit-timestamped archives, and
verifies the result. It deliberately does not claim to run the complete test,
API, secret, or two-build reproducibility gate; use
`Invoke-CompleteReleaseGate.ps1` before signing or publishing.

`release/package-layout.json` is the reviewed closure policy. Packaged DLLs are
verified from their PE assembly metadata. Framework exclusions are exact names
provided by the declared .NET runtime. Host exclusions are exact names tied to
MelonLoader 0.7.3 or the SCP:SL 14.2.7 client/server and must be observed in the
resolved MSBuild inputs. The client archive carries Bouncy Castle; the server
uses the SCP:SL 14.2.7 game's Managed copy, whose assembly version is pinned
in the closure declaration. The server carries Lib.Harmony 2.2.2. The client
uses MelonLoader 0.7.3's HarmonyX 2.10.2 and Newtonsoft.Json 13.0.4 instead
of redistributing competing copies. The SCP:SL, Unity, IL2CPP interop,
MelonLoader, LabAPI, and
CLR/framework assemblies remain host-provided and are not redistributed.

Every client, server, and SDK archive includes the project `LICENSE`, `NOTICE`,
and the exact `THIRD_PARTY_DEPENDENCIES.md` whose SHA-256 is bound into the
dependency inventory. The declaration contains an assembly-by-assembly map for
all redistributed third-party runtime DLLs. Mono.Cecil remains a build-tool
dependency and is not carried by the client archive.

The developer SDK includes `BouncyCastle.Cryptography.dll` beside
`Anomaly.Shared.dll` under `lib/net48` because `AuthCrypto` exposes Bouncy
Castle types in its public signatures. Its reference metadata records this
compile dependency and declares the separate, non-redistributed SCP:SL client
interop profile. CI compiles a staged-SDK probe both with and without that DLL
to prove the dependency is necessary and present.

Signing requires all three external operator inputs:

```powershell
./release/Build-Release.ps1 -Version 1.0.0 `
  -OutputDirectory artifacts/release-1.0.0-signed `
  -SigningKeyPath X:\controlled\release-private.pem `
  -PublicKeyPath X:\controlled\release-public.pem `
  -KeyId production-2026
```

Keys must be ECDSA P-256. No production private key belongs in this repository
or a normal CI worker. Use a new empty output directory for the signing pass;
the lower-level packager intentionally refuses to overwrite the unsigned gate
evidence. The trusted public key and `keyId` must be added to the
launcher's embedded trust store before publishing. The distribution endpoint
must return the generated client envelope (`manifestBase64`,
`signatureBase64`, and `dependencyInventoryBase64`) and every artifact named by
the signed `anomaly-client` manifest, mapped to its launcher install target.
The mandatory launcher identity remains `target=client-runtime` and
`packageId=releaseId=anomaly-client`; adding dependency artifacts does not alter
the schema-v2 signed-manifest contract. Each archive hash is recorded in the
dependency inventory, whose SHA-256 is bound by the signed manifest, while the
manifest also signs every post-extraction artifact hash and size.
After all signatures and the official client envelope verify,
`Build-Release.ps1` changes the receipt signing state to `signed`, records the
verified `keyId`, and binds the public manifests, signatures, and envelope by
size and SHA-256. Neither key path nor any private/public key bytes are written
to the receipt. This signing-evidence update is intentionally outside the
unsigned reproducibility comparison.
