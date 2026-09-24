# Anomaly Client API versioning

This early access SDK establishes a new pre-release baseline while retaining
core/API version `1.0.0`. Rebuild mods compiled against an earlier development
build, even if its assembly version is also `1.0.0.0`. This is an intentional
compatibility reset before the public contract is frozen; no existing release
tag or published package is changed by this source update.

Once frozen, `Anomaly.Client.Api` follows semantic versioning for its public
managed surface.

- Patch releases may add implementation fixes without changing public signatures.
- Minor releases may add APIs and optional manifest fields while preserving existing callers.
- Major releases may remove or change public contracts. A managed module must declare the major API version it targets; incompatible majors are rejected before assembly load.
- Anomaly's `SupportedGameVersion` is an enforced compatibility gate, not advisory metadata. The launcher evaluates the assembly declaration before official-core installation/materialization, and the managed-module runtime evaluates the equivalent manifest declarations before `Assembly.LoadFrom`.

`14.2.7^` means `>=14.2.7 && <14.3.0`; an expression without `^` matches exactly that three-component game version. Public v1 uses `[assembly: SupportedGameVersion("14.2.7^")]`. Do not add or rely on MelonLoader's `MelonGameVersion` attribute.

Managed-module manifests use schema version 1, declare an inclusive minimum and exclusive maximum API version, and must include at least one `SupportedGameVersions` expression. They are resolved as one deterministic dependency graph. The runtime writes `UserData/Anomaly/modules.lock.json` with the ordered ids, versions, and SHA-256 values used for the launch. Capability declarations describe full-trust behavior for review; they are not a process sandbox.

Public API compatibility is covered by the `Anomaly.Client.Api.Tests` contract tests and the metadata-only `tools/PublicApiBaseline` check against `PublicAPI.Shipped.txt`. Review every baseline change; removals and signature changes require a major-version decision.

## Migrating development mods

- `InputRegistry.Register` now returns `IDisposable`. Keep the handle and dispose
  it during mod deinitialization, after unsubscribing input handlers. Disposal is
  silent, removes only that registration, and preserves the user's saved binding.
  Duplicate registration still warns and returns an inert handle.
- Public registrations reject `anomaly` and `anomaly.*` IDs. Use your own valid
  lowercase dotted namespace for commands, bindings, virtual inputs, cooldowns,
  config, and persistence.
- `FileCache`, its entry type, `LocalizationService`, asset unloading, global
  config disposal, and global persistence flushing are framework internals.
  Use `AnomalyResources.TryGetTexture` / `TryGetAudioClip`, `Tr`, and your own
  persistence store's `Flush()` instead.
- Ordinary MelonLoader mods are the primary starter. Managed modules remain an
  advanced option and can track the new input handle with `IModContext.Track`.
- Keep raw MelonPreferences category IDs free of dots: the starter uses
  `example_hello` for preferences and `example.hello` for Anomaly registrations.
  The supported loader treats dots as TOML paths, which can lose saved entries.
  Anomaly's config/persistence facades still accept dotted mod IDs.

Use DLLs and XML documentation from one release set. The checked baseline also
records constant values, parameter defaults, visibility, and type modifiers so
compatibility reviews cover more than member names.

## AssetBundle protocol 6 migration

Rebuild companion mods against the matching current DLL set. The pre-release managed versions remain unchanged. Resource lookup, texture overrides and item icons now use typed AssetAddress values instead of raw-file hashes/paths. TryGetSprite and TryGetMesh join texture/audio lookup. Raw PNG skins and OGG/WAV overrides are replaced by offline-authored bundle/index pairs. See [ASSETBUNDLES.md](../ASSETBUNDLES.md) for authoring, lifetimes and API examples.
