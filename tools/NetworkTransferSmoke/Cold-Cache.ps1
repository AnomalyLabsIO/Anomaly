[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Prepare','Restore')] [string] $Action,
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $BackupDirectory,
    [string] $ExpectedContentPath = '',
    [string] $TransferManifestPath = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../EarlyAccessUiSmoke/ModLaunchBackup.ps1')
$game = [IO.Path]::GetFullPath($GameDirectory)
Assert-SmokeGameIdle $game
$cache = [IO.Path]::GetFullPath((Join-Path $game 'UserData/Anomaly/FileCache'))
$backup = [IO.Path]::GetFullPath($BackupDirectory)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$backup.StartsWith((Join-Path $repository 'artifacts') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be beneath repository artifacts.' }
function Resolve-CacheFile([string] $relative) {
    if ($relative -ne 'manifest.json' -and $relative -cnotmatch '^[a-f0-9]{2}/[a-f0-9]{64}$') { throw 'Invalid exact cache filename.' }
    $target = [IO.Path]::GetFullPath((Join-Path $cache $relative))
    if (!$target.StartsWith($cache + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Cache target escaped its root.' }
    $cursor = $target
    while ($cursor.Length -ge $game.Length) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse-point target is not supported: $cursor" }
        $cursor = Split-Path $cursor
        if (!$cursor) { break }
    }
    return $target
}
if ($Action -eq 'Prepare') {
    if (Test-Path -LiteralPath $backup) { throw 'Use a new backup directory.' }
    if (!$ExpectedContentPath) { throw 'An exact generated fixture expectation is required.' }
    $expected = Get-Content -Raw -LiteralPath $ExpectedContentPath | ConvertFrom-Json
    $hashes = @($expected.AssetPacks | ForEach-Object { $_.BundleSha256; $_.IndexSha256 }) + @($expected.ScriptSha256)
    if ($expected.ManifestFiles) { $hashes += @($expected.ManifestFiles | ForEach-Object { $_.hash }) }
    if ($TransferManifestPath) {
        $manifest = Get-Content -Raw -LiteralPath $TransferManifestPath | ConvertFrom-Json
        $hashes += @($manifest.files | ForEach-Object { $_.hash })
    }
    $hashes = @($hashes | ForEach-Object { if ($_ -cnotmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid pinned SHA256.' }; $_.ToLowerInvariant() } | Sort-Object -Unique)
    $existingBytes = if (Test-Path -LiteralPath $cache) { [long]((Get-ChildItem -LiteralPath $cache -File -Recurse | Measure-Object Length -Sum).Sum) } else { 0L }
    $expectedBytes = [long](($expected.AssetPacks | Measure-Object BundleBytes -Sum).Sum) + 2MB
    if ($existingBytes + $expectedBytes -gt 500MB) { throw 'This run could trigger the cache owner budget eviction. Stage/restore a separate cache explicitly before continuing.' }
    New-Item -ItemType Directory -Path (Join-Path $backup 'files') -Force | Out-Null
    $entries = @()
    foreach ($relative in @('manifest.json') + @($hashes | ForEach-Object { $_.Substring(0,2) + '/' + $_ })) {
        $target = Resolve-CacheFile $relative
        $entry = [ordered]@{path=$relative;existed=(Test-Path -LiteralPath $target -PathType Leaf);backup=$null;sha256=$null;lastWriteUtc=$null;attributes=$null}
        if ($entry.existed) {
            $file = Get-Item -LiteralPath $target
            $entry.sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($relative -ne 'manifest.json' -and $entry.sha256 -ne (Split-Path $target -Leaf)) { throw "Pinned cache hash mismatch: $relative" }
            $entry.backup = 'files/' + $entries.Count + '.bin'
            $entry.lastWriteUtc = $file.LastWriteTimeUtc.ToString('O'); $entry.attributes = [int]$file.Attributes
            Copy-Item -LiteralPath $target -Destination (Join-Path $backup $entry.backup)
            if ((Get-FileHash -LiteralPath (Join-Path $backup $entry.backup)).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Backup verification failed.' }
        }
        $entries += $entry
    }
    [ordered]@{schema=1;game=$game;cache=$cache;expected=(Get-FileHash -LiteralPath $ExpectedContentPath).Hash;entries=$entries} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'cache-backup.json') -Encoding utf8
    # Only individually pinned content hashes are removed. The runtime reconciles its own index.
    foreach ($entry in $entries | Where-Object { $_.existed -and $_.path -ne 'manifest.json' }) {
        Remove-Item -LiteralPath (Resolve-CacheFile $entry.path)
    }
    Write-Output "Prepared $($hashes.Count) exact content hashes; index and prior bytes backed up in $backup"
} else {
    $record = Get-Content -Raw -LiteralPath (Join-Path $backup 'cache-backup.json') | ConvertFrom-Json
    if ($record.schema -ne 1 -or $record.game -ne $game -or $record.cache -ne $cache) { throw 'Cache backup ownership mismatch.' }
    foreach ($entry in $record.entries) {
        $target = Resolve-CacheFile $entry.path
        if ($entry.existed -and (Get-FileHash -LiteralPath (Join-Path $backup $entry.backup)).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Backup content changed; refusing restoration.' }
        if ($entry.path -ne 'manifest.json' -and (Test-Path -LiteralPath $target) -and
            (Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne (Split-Path $target -Leaf)) { throw 'Current content is not the pinned test file; refusing replacement.' }
    }
    foreach ($entry in $record.entries) {
        $target = Resolve-CacheFile $entry.path
        if ($entry.existed) {
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $backup $entry.backup) -Destination $target -Force
            [IO.File]::SetLastWriteTimeUtc($target,[DateTime]::Parse($entry.lastWriteUtc)); [IO.File]::SetAttributes($target,[IO.FileAttributes]$entry.attributes)
            if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Restored cache verification failed.' }
        } elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    Write-Output 'Exact cache files and prior index restored; unlisted files were not changed.'
}
