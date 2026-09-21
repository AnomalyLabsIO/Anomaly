# Public API baseline

This tool reads assembly metadata without loading the target assembly or its
Unity dependencies. It records the public/protected type and member surface in
a deterministic text baseline.

```powershell
dotnet build Anomaly.Client.Api/Anomaly.Client.Api.csproj -c Release -p:DeployLocal=false
dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -- `
  Anomaly.Client.Api/bin/Release/net6.0/Anomaly.Client.Api.dll `
  Anomaly.Client.Api/PublicAPI.Shipped.txt
```

The check exits nonzero for additions, removals, or signature changes. After a
compatibility and semantic-version review, append `--update` to intentionally
regenerate the baseline.

Format v2 also records public/protected visibility (including nested protected
types), abstract/sealed type modifiers, constant and enum values, parameter names,
optional/default values, and in/out/params metadata. Values are escaped and
formatted with invariant culture. Run the metadata mutation fixtures with:

```powershell
dotnet run --project tools/PublicApiBaseline/PublicApiBaseline.csproj -- --self-test
```

The complete release gate runs these fixtures separately from comparing the
current assembly to the reviewed baseline.
