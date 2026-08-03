# Signed release manifests

This tool creates and verifies deterministic artifact inventories signed with an operator-owned ECDSA private key. Private keys must remain outside the repository and CI logs.

```powershell
dotnet run --project tools/ReleaseManifest -- generate-key private-key.pem public-key.pem
dotnet run --project tools/ReleaseManifest -- create anomaly-client 1.0.0 <commit> THIRD_PARTY_DEPENDENCIES.md release.json private-key.pem Anomaly.Client.dll Anomaly.Client.Api.dll Anomaly.Shared.dll Anomaly.Bootstrap.dll
dotnet run --project tools/ReleaseManifest -- verify release.json release.json.sig public-key.pem <artifact-directory> THIRD_PARTY_DEPENDENCIES.md
```

The signed manifest binds the source commit, dependency/license inventory hash,
and bare artifact filenames, sizes, and lowercase SHA-256 values. Passing the
optional artifact directory to `verify` checks every payload before
materialization or execution. Private signing keys are ignored by repository
policy and must remain in an operator-owned keystore outside the source tree.
