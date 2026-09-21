# Anomaly Client API versioning

`Anomaly.Client.Api` follows semantic versioning for its public managed surface.

- Patch releases may add implementation fixes without changing public signatures.
- Minor releases may add APIs and optional manifest fields while preserving existing callers.
- Major releases may remove or change public contracts. A managed module must declare the major API version it targets; incompatible majors are rejected before assembly load.
- Anomaly's `SupportedGameVersion` is an enforced compatibility gate, not advisory metadata. The launcher evaluates the assembly declaration before official-core installation/materialization, and the managed-module runtime evaluates the equivalent manifest declarations before `Assembly.LoadFrom`.

`14.2.7^` means `>=14.2.7 && <14.3.0`; an expression without `^` matches exactly that three-component game version. Public v1 uses `[assembly: SupportedGameVersion("14.2.7^")]`. Do not add or rely on MelonLoader's `MelonGameVersion` attribute.

Managed-module manifests use schema version 1, declare an inclusive minimum and exclusive maximum API version, and must include at least one `SupportedGameVersions` expression. They are resolved as one deterministic dependency graph. The runtime writes `UserData/Anomaly/modules.lock.json` with the ordered ids, versions, and SHA-256 values used for the launch. Capability declarations describe full-trust behavior for review; they are not a process sandbox.

Public API compatibility is covered by the `Anomaly.Client.Api.Tests` contract tests and the metadata-only `tools/PublicApiBaseline` check against `PublicAPI.Shipped.txt`. Review every baseline change; removals and signature changes require a major-version decision.
