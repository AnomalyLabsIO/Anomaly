[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ContentPackDirectory,
    [Parameter(Mandatory)] [string] $ExpectedContentPath
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $repository 'tools/AssetPackPolicy/bin/Release/netstandard2.0/Anomaly.AssetPackPolicy.dll'))
$expected = Get-Content -Raw -LiteralPath $ExpectedContentPath | ConvertFrom-Json
$files = @()
function Hash-Bytes([byte[]] $bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
foreach ($descriptorFile in Get-ChildItem -LiteralPath $ContentPackDirectory -Filter pack.json -File -Recurse) {
    $descriptor = [Anomaly.Shared.Content.Packs.ContentPackCodec]::Parse([IO.File]::ReadAllBytes($descriptorFile.FullName), $true)
    $packRoot = $descriptorFile.DirectoryName
    $reader = { param([string] $relative,[int] $maximum)
        $path = [IO.Path]::GetFullPath((Join-Path $packRoot $relative))
        if (!$path.StartsWith($packRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Component path escaped its fixture pack.' }
        if ((Get-Item -LiteralPath $path).Length -gt $maximum) { throw 'Fixture component exceeded the production limit.' }
        return [IO.File]::ReadAllBytes($path)
    }.GetNewClosure()
    $captured = [Anomaly.Shared.Content.Packs.ContentPackCodec]::Capture($descriptor,[Func[string,int,byte[]]]$reader,$true)
    if ($captured.AssetIndex -ne $null) {
        $pack = @($expected.AssetPacks | Where-Object PackId -CEQ $captured.AssetIndex.PackId)
        if ($pack.Count -ne 1) { throw 'An unpinned asset pack was found.' }
        $bundlePath = Join-Path $packRoot $captured.BundlePath
        $hash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $indexHash = Hash-Bytes $captured.AssetIndexBytes
        if ($hash -ne $pack[0].BundleSha256 -or $indexHash -ne $pack[0].IndexSha256) { throw 'Pinned asset content changed.' }
        $files += [pscustomobject][ordered]@{hash=$hash;bytes=(Get-Item -LiteralPath $bundlePath).Length;kind='AssetBundle';pack=$descriptor.PackId}
        $files += [pscustomobject][ordered]@{hash=$indexHash;bytes=$captured.AssetIndexBytes.Length;kind='AssetIndex';pack=$descriptor.PackId}
    }
    if ($captured.ScriptIndex -ne $null) {
        $sourceHash = Hash-Bytes $captured.SourceBytes
        if ($captured.ScriptIndex.PackId -cne $expected.ScriptPackId -or $sourceHash -ne $expected.ScriptSha256) { throw 'An unpinned Lua source was found.' }
        $indexHash = Hash-Bytes $captured.ScriptIndexBytes
        $expected | Add-Member -NotePropertyName ScriptIndexSha256 -NotePropertyValue $indexHash -Force
        $files += [pscustomobject][ordered]@{hash=$sourceHash;bytes=$captured.SourceBytes.Length;kind='ScriptSource';pack=$descriptor.PackId}
        $files += [pscustomobject][ordered]@{hash=$indexHash;bytes=$captured.ScriptIndexBytes.Length;kind='ScriptIndex';pack=$descriptor.PackId}
    }
}
$unique = @($files | Group-Object hash | ForEach-Object { $_.Group[0] })
$total = [long](($unique | Measure-Object bytes -Sum).Sum)
if ($total -le 0 -or $unique.Count -lt 4 -or $total -gt 256MB -or ($unique | Where-Object { $_.bytes -le 0 -or $_.bytes -gt 64MB }).Count -gt 0) { throw 'Exact fixture manifest exceeded the production limits.' }
$expected | Add-Member -NotePropertyName ManifestFiles -NotePropertyValue $unique -Force
$expected | Add-Member -NotePropertyName ExpectedTransferBytes -NotePropertyValue $total -Force
$expected | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ExpectedContentPath -Encoding utf8
Write-Output "Validated and pinned $($unique.Count) files / $total bytes using the production content-pack codec."
