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
