# Editor policy assembly

This netstandard2.0 assembly links the exact asset-index, address, visual-description and stock CASSIE timing policy sources from `Anomaly.Shared`, including its embedded stock profile. The offline Unity authoring project uses it before publishing an index. It introduces no game, crypto, client or server runtime dependency and is not shipped to the game server/client.

Build with `dotnet build tools/AssetPackPolicy/AssetPackPolicy.csproj -c Release -p:DeployLocal=false`. Its filename is `Anomaly.AssetPackPolicy.dll`; the external author's `build-pack.ps1` copies it into Editor-only dependencies alongside `Anomaly.AssetValidation.dll` and pinned `AssetsTools.NET.dll`.

`Unity/AnomalyAssetPackExporter.cs` is the versioned production exporter source.
The external authoring build copies it to `Assets/Editor/AssetPackExporter.cs`.
Runtime asset indexes now use schema 2 and include the full validator's measured
`DecodedBytes`; catalog schema 5 authenticates that declaration for capacity
planning. The client independently checks exact equality before native loading.
Use `tools/AssetPackIndexUpgrade` to validate and rebuild an old index around an
unchanged compatible bundle without rerunning Unity. Legacy proof exporters emit
diagnostic reports and do not produce current runtime indexes.

Keep the explicit linked source list synchronized if the shared policy is split into additional files. The final exporter invokes `AssetPackIndexCodec.Parse` on the exact UTF-8 JSON it publishes, with the remote/local policy selected by the authoring config.
