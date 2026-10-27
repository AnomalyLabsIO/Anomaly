# Explicitly wrap one exported native bundle/index pair in an asset-only content pack.
function Get-AssetContentPackFixtureEntries {
    param(
        [Parameter(Mandatory)] [string] $IndexPath,
        [Parameter(Mandatory)] [string] $BundlePath,
        [string] $Root = ''
    )
    $indexFile = Get-Item -LiteralPath $IndexPath
    if ($indexFile.Length -gt 262144) { throw 'Fixture asset index is oversized.' }
    $index = Get-Content -Raw -LiteralPath $IndexPath | ConvertFrom-Json
    if ($index.PackId -cnotmatch '^([a-z][a-z0-9_]{0,31}(?:\.[a-z][a-z0-9_]{0,31})*):([a-z0-9._-]+)$' -or
        $index.PackId.Length -gt 128 -or $Matches[1].Length -gt 64 -or $Matches[2] -in @('.','..')) { throw 'Invalid fixture pack identity.' }
    $owner = $Matches[1]; $name = $Matches[2]
    if ($index.Namespace -cne $owner -or $index.BundleFile -cnotmatch '^[a-z0-9._-]+\.bundle$' -or
        $index.BundleFile.Length -gt 128) { throw 'Fixture namespace or bundle filename is invalid.' }
    if ($index.BundleBytes -le 0 -or $index.BundleBytes -gt 67108864) { throw 'Fixture bundle size is outside the content pack limit.' }
    if ((Get-FileHash -LiteralPath $BundlePath -Algorithm SHA256).Hash -ne $index.BundleSha256 -or
        (Get-Item -LiteralPath $BundlePath).Length -ne $index.BundleBytes) { throw 'Fixture bundle/index identity mismatch.' }
    $packRoot = if ($Root) { "$($Root.TrimEnd('/'))/$owner/$name" } else { "$owner/$name" }
    $descriptor = [ordered]@{
        SchemaVersion = 1; PackId = $index.PackId; Version = '1.0.0'
        Assets = [ordered]@{ PackId = $index.PackId; Index = 'assets/pack.index.json' }
    } | ConvertTo-Json -Compress -Depth 4
    [pscustomobject]@{source=$null;content=$descriptor;path="$packRoot/pack.json"}
    [pscustomobject]@{source=$indexFile.FullName;path="$packRoot/assets/pack.index.json"}
    [pscustomobject]@{source=[IO.Path]::GetFullPath($BundlePath);path="$packRoot/assets/$($index.BundleFile)"}
}
