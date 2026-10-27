[CmdletBinding()]
param([Parameter(Mandatory=$true)][string] $BackupDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ModLaunchBackup.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$backupRoot = [IO.Path]::GetFullPath($BackupDirectory)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/news-ui-smoke')).TrimEnd('\') + '\'
if (!$backupRoot.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup directory must remain within artifacts/news-ui-smoke.' }
$manifest = Get-Content -LiteralPath (Join-Path $backupRoot 'backup.json') -Raw | ConvertFrom-Json
$game = [IO.Path]::GetFullPath($manifest.gameDirectory)
$authorizedGame = 'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy'
if (![string]::Equals($game,$authorizedGame,[StringComparison]::OrdinalIgnoreCase)) { throw 'This restore targets only the explicitly authorized ModLaunch installation.' }
Assert-SmokeGameIdle $game
foreach ($entry in $manifest.entries) {
    $target = Resolve-SmokeBackupTarget $game $manifest.stockDirectory $entry.scope $entry.path
    if ($entry.existed) {
        $source = [IO.Path]::GetFullPath((Join-Path $backupRoot $entry.backup))
        if (!$source.StartsWith($backupRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Backup content is missing or does not match its recorded hash.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
        [IO.File]::SetLastWriteTimeUtc($target,[DateTime]::Parse($entry.lastWriteUtc).ToUniversalTime())
        [IO.File]::SetAttributes($target,[IO.FileAttributes]$entry.attributes)
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Restore hash mismatch: $($entry.path)" }
    } elseif (Test-Path -LiteralPath $target -PathType Leaf) {
        # Each literal path was verified beneath its declared game/stock directory.
        Remove-Item -LiteralPath $target -Force
    }
}
foreach ($relative in @($manifest.createdDirectories | Sort-Object Length -Descending)) {
    $directory = Resolve-SmokeBackupTarget $game $manifest.stockDirectory 'game' $relative
    if ((Test-Path -LiteralPath $directory) -and
        ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing a reparse-point staging directory during restore: $relative"
    }
    if ((Test-Path -LiteralPath $directory -PathType Container) -and
        !(Get-ChildItem -LiteralPath $directory -Force | Select-Object -First 1)) {
        # Remove only previously absent, still-empty directories created by staging.
        [IO.Directory]::Delete($directory)
    }
}
$mismatches = @($manifest.protected | Where-Object {
    $target = Resolve-SmokeBackupTarget $game $manifest.stockDirectory 'game' $_.path
    !(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $_.sha256
} | ForEach-Object {$_.path})
[ordered]@{ restoredUtc=[DateTimeOffset]::UtcNow.ToString('O'); restoredEntries=$manifest.entries.Count; protectedMismatches=$mismatches } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'restored.json')
if ($mismatches.Count) { throw "Restored test files, but protected native/unrelated files differ: $($mismatches -join ', ')" }
Write-Output "Restored and hash-verified $($manifest.entries.Count) test/profile/preference files; protected native and unrelated mod hashes match."
