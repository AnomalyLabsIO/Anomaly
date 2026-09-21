# Signed release manifests

This tool creates and verifies canonical schema-v2 artifact inventories signed
with an operator-owned ECDSA P-256 private key and SHA-256. Private keys must
remain outside the repository and CI logs.

```powershell
dotnet run --project tools/ReleaseManifest -- generate-key private-key.pem public-key.pem
$env:SOURCE_DATE_EPOCH = '<release commit time>'
dotnet run --project tools/ReleaseManifest -- create `
  production-2026 client-runtime anomaly-client anomaly-client 1.0.0 `
  <commit> dependency-inventory.json release.json private-key.pem `
  <artifact> [...]
dotnet run --project tools/ReleaseManifest -- verify `
  release.json release.json.sig public-key.pem `
  <artifact-directory> dependency-inventory.json
```

The exact camel-case manifest contract is:

`schemaVersion`, `keyId`, `target`, `packageId`, `releaseId`, `version`,
`sourceCommit`, `dependencyInventorySha256`, `generatedUtc`, and `artifacts`.

Artifacts are canonically ordered bare filenames with `sizeBytes` and lowercase
`sha256`. For the launcher's mandatory official core, use `target` =
`client-runtime` and both `packageId` and `releaseId` = `anomaly-client`.
`SOURCE_DATE_EPOCH` makes the manifest timestamp reproducible. The detached
signature remains separate from deterministic payload archives.

Verification rejects unknown JSON fields, non-P-256 keys, malformed metadata,
signature or inventory mismatches, duplicate filenames, and missing or changed
payload bytes. Private signing keys are ignored by repository policy and belong
in an operator-controlled keystore, never the source tree.
