# Script API catalog export

Run from the repository root:

```powershell
dotnet run --project tools/ScriptApiCatalog -- Anomaly.Client.Scripting --verify
```

To regenerate after reviewing a script API change, omit `--verify`. An explicit output
directory is required. The tool writes `ScriptAPI.Shipped.txt`, `ScriptAPI.json`, and
`Anomaly.Client.Api.d.lua`, and `LuaEnumNames.md` using the same discovery/catalog code as the client host.
It does not create a VM, execute Lua, subscribe to native events or load server assemblies.
API XML documentation adjacent to the referenced API DLL supplies optional summaries.

Names default to the annotated C# target names. Explicit aliases and all visibility,
type-graph and event admission checks are retained. The Lua file is an IDE definition
file; do not load it into a game script. The catalog does not create globals for discovered types.

Generate a complete LuaLS authoring workspace with the configured game interop metadata:

```powershell
dotnet run --project tools/ScriptApiCatalog -- --lua-workspace artifacts/lua-workspace 14.2.7 1.0.0.0
```

This writes `.definitions/anomaly.lua` and a Lua 5.2 `.luarc.json` whose `runtime.builtin`
settings match the restricted Anomaly sandbox. Existing configuration is preserved;
append `--force` to replace it. To use the definitions in another authoring workspace,
add their directory to `workspace.library` and merge the generated `runtime.version`
and `runtime.builtin` settings. Both dot and colon calls have signatures. Enum results
retain their declared domains; event callbacks use frozen snapshot types whose destroyed
wrapper fields can be nil.

The build uses the configured local API/game references and `DeployLocal=false`.
This exporter grants no execution permission. Verified bundle execution requires the
host's separate approval path and remains experimental.
