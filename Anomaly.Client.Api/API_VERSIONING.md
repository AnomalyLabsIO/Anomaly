# Anomaly Client API versioning

`Anomaly.Client.Api` follows semantic versioning for its public managed surface.

- Patch releases may add implementation fixes without changing public signatures.
- Minor releases may add APIs and optional manifest fields while preserving existing callers.
- Major releases may remove or change public contracts. A managed module must declare the major API version it targets; incompatible majors are rejected before assembly load.
- `SupportedGameVersion` is advisory launcher metadata. It is never an API or launch gate.

Managed-module manifests use schema version 1, declare an inclusive minimum and exclusive maximum API version, and are resolved as one deterministic dependency graph. The runtime writes `UserData/Anomaly/modules.lock.json` with the ordered ids, versions, and SHA-256 values used for the launch. Capability declarations describe full-trust behavior for review; they are not a process sandbox.

Public API compatibility is covered by the `Anomaly.Client.Api.Tests` contract tests and the metadata-only `tools/PublicApiBaseline` check against `PublicAPI.Shipped.txt`. Review every baseline change; removals and signature changes require a major-version decision.
