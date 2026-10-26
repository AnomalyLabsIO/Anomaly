# Script API catalog export

Run from the repository root:

```powershell
dotnet run --project tools/ScriptApiCatalog -- Anomaly.Client.Scripting --verify
```

To regenerate after reviewing a script API change, omit `--verify`. An explicit output
directory is required. The tool writes `ScriptAPI.Shipped.txt`, `ScriptAPI.json`, and
`Anomaly.Client.Api.d.lua` using the same discovery/catalog code as the client host.
It does not create a VM, execute Lua, subscribe to native events or load server assemblies.
API XML documentation adjacent to the referenced API DLL supplies optional summaries.

Names default to the annotated C# target names. Explicit aliases and all visibility,
type-graph and event admission checks are retained. The Lua file is an IDE definition
file; do not load it into a game script. Add it to LuaLS `workspace.library` for editor
completion and signatures. The catalog does not create globals for discovered types.

The build uses the configured local API/game references and `DeployLocal=false`.
This exporter grants no execution permission. Verified bundle execution requires the
host's separate approval path and remains experimental.
