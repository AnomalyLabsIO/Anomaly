# Rebuild an asset index's decoded resource summary

Asset index schema 2 requires `DecodedBytes`, measured by the full managed bundle
validator. Catalog schema 5 authenticates that declaration so a client can plan
combined local/server asset residency before acquiring bundle bodies. The server
remains a metadata-only indexer. Clients independently measure the bundle and
require exact equality before native loading; a declaration never bypasses the
existing decoded or encoded limits.

Prefer re-exporting with the current `Anomaly.AssetAuthoring` exporter. To keep an
existing compatible bundle byte-for-byte while rebuilding its index:

Release SDK users run `authoring/Upgrade-Index.ps1`; the SDK contains this tool,
its complete runtime dependencies and wrappers without a core checkout. The
following command is for maintainers working from source:

```powershell
dotnet run --project tools/AssetPackIndexUpgrade -c Release -- path/to/old.index.json path/to/new.index.json
```

Use `--local` for a local-only pack (for example UI skins). `--bundle=<path>` can
select a read-only bundle outside the index directory. The tool verifies complete
hash, length, supported native contents and resource limits before writing a new
index. It accepts schema 1 or 2, rejects unknown/duplicate properties, never
overwrites an existing destination, and never changes the source or bundle.
Deploy the rebuilt index under the original descriptor-selected index filename
only after inspecting the result. Its new hash requires new exact-content consent.

The legacy skin/schema proof exporters produce diagnostic reports, not current
runtime asset indexes; use the production AssetPackExporter for runtime packs.
