$ErrorActionPreference = 'Stop'

function Assert-SmokeGameIdle([string] $GameDirectory) {
    $gameExe = Join-Path $GameDirectory 'SCPSL.exe'
    $launcherExe = Join-Path $GameDirectory '.modlaunch/SCPSL-ModLaunch.exe'
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name = 'SCPSL.exe' OR Name = 'SCPSL-ModLaunch.exe'")) {
        if (!$process.ExecutablePath) { throw "Cannot verify the executable path for PID $($process.ProcessId). Run with permission to inspect it before changing test files." }
        if ([string]::Equals([IO.Path]::GetFullPath($process.ExecutablePath), $gameExe, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals([IO.Path]::GetFullPath($process.ExecutablePath), $launcherExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The authorized game's launcher or child is still running (PID $($process.ProcessId)). Wait for both to exit before changing test files."
        }
    }
    if ((Test-Path -LiteralPath (Join-Path $GameDirectory '.modlaunch/session.json')) -or
        (Test-Path -LiteralPath (Join-Path $GameDirectory 'version.dll'))) {
        throw 'ModLaunch has not restored its native session. Let the launcher restore first, or run that launcher with --modlaunch-recover-only after its child exits.'
    }
}

function Resolve-SmokeBackupTarget([string] $GameDirectory, [string] $StockDirectory, [string] $Scope, [string] $RelativePath) {
    if ($Scope -eq 'stock') {
        if ($RelativePath -notin @('registry.txt','iphistory.txt')) { throw 'Unsupported stock preference backup target.' }
        $root = [IO.Path]::GetFullPath($StockDirectory)
    } elseif ($Scope -eq 'game') { $root = [IO.Path]::GetFullPath($GameDirectory) }
    else { throw 'Unknown backup target scope.' }
    $target = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (!$target.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup target escapes its intended directory.' }
    return $target
}

function New-ModLaunchSmokeBackup([string] $Repository, [string] $GameDirectory, [string] $BackupDirectory, [string[]] $GamePaths) {
    Assert-SmokeGameIdle $GameDirectory
    $backupRoot = [IO.Path]::GetFullPath($BackupDirectory)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $Repository 'artifacts/news-ui-smoke')).TrimEnd('\') + '\'
    if (!$backupRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'ModLaunch backups must be inside repository artifacts/news-ui-smoke.' }
    if (Test-Path -LiteralPath $backupRoot) { throw 'Use a fresh backup directory for this test deployment.' }
    New-Item -ItemType Directory -Path (Join-Path $backupRoot 'files') -Force | Out-Null
    $stockRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'SCP Secret Laboratory'))
    $entries = @()
    $paths = @($GamePaths | Sort-Object -Unique | ForEach-Object { @{scope='game';path=$_} }) +
        @(@{scope='stock';path='registry.txt'}, @{scope='stock';path='iphistory.txt'})
    foreach ($item in $paths) {
        $target = Resolve-SmokeBackupTarget $GameDirectory $stockRoot $item.scope $item.path
        $entry = [ordered]@{ scope=$item.scope; path=$item.path; existed=(Test-Path -LiteralPath $target -PathType Leaf); backup=$null; sha256=$null; attributes=$null; lastWriteUtc=$null }
        if ($entry.existed) {
            $file = Get-Item -LiteralPath $target
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing reparse-point backup target: $($item.path)" }
            $entry.backup = 'files/' + $entries.Count + '.bin'
            $entry.sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            $entry.attributes = [int]$file.Attributes
            $entry.lastWriteUtc = $file.LastWriteTimeUtc.ToString('O')
            Copy-Item -LiteralPath $target -Destination (Join-Path $backupRoot $entry.backup)
            if ((Get-FileHash -LiteralPath (Join-Path $backupRoot $entry.backup) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Backup hash mismatch.' }
        }
        $entries += $entry
    }
    $nativePaths = @('SCPSL.exe','GameAssembly.dll','UnityPlayer.dll','SCPSL_Data/il2cpp_data/Metadata/global-metadata.dat','MelonLoader/net6/MelonLoader.dll')
    $protected = @($nativePaths | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $GameDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant()} })
    foreach ($folder in @('Mods','Plugins','UserLibs')) {
        foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $GameDirectory $folder) -File -Filter '*.dll' -ErrorAction SilentlyContinue)) {
            $relative = [IO.Path]::GetRelativePath($GameDirectory,$file.FullName).Replace('\','/')
            if ($relative -notin $GamePaths) { $protected += @{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} }
        }
    }
    [ordered]@{ schemaVersion=1; gameDirectory=$GameDirectory; stockDirectory=$stockRoot; createdUtc=[DateTimeOffset]::UtcNow.ToString('O');
        entries=$entries; protected=$protected; notice='Local backup may contain private profiles and configuration. Do not share its files.' } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backupRoot 'backup.json')
    return $backupRoot
}
