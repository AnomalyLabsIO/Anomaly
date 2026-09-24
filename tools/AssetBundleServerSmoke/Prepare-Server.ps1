[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceServerRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$Port=27989
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=[IO.Path]::GetFullPath($SourceServerRoot)
$destination=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=(Join-Path $repo 'artifacts/assetbundles')+[IO.Path]::DirectorySeparatorChar
if (!$destination.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $destination)) { throw 'Use a new isolated assetbundles artifact directory.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$copied=0
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relative=[IO.Path]::GetRelativePath($source,$file.FullName)
    $first=$relative.Split([IO.Path]::DirectorySeparatorChar)[0]
    if ($first -in @('AppData','Logs','Dumps','LabAPI','plugins','dependencies','steamapps','_CommonRedist') -or $file.Name -match '(?i)^(hoster_policy\.txt|.*\.log)$') { continue }
    $target=[IO.Path]::GetFullPath((Join-Path $destination $relative))
    if (!$target.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Copy target escaped staging root.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    # File.Copy follows source file links and writes a regular independent destination file.
    [IO.File]::Copy($file.FullName,$target)
    $copied++
}
foreach($required in @('SCPSL.exe','UnityPlayer.dll','SCPSL_Data/Managed/Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $destination $required))) { throw "Missing staged runtime $required" }
}
$config=Join-Path $destination "AppData/config/$Port"
New-Item -ItemType Directory -Force -Path $config | Out-Null
@('gamedir_for_configs: true','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1') | Set-Content -LiteralPath (Join-Path $destination 'hoster_policy.txt')
@('server_name: Anomaly AssetBundle loopback fixture','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1','server_ip: 127.0.0.1','online_mode: false','enable_query: false','contact_email: default','max_players: 4') | Set-Content -LiteralPath (Join-Path $config 'config_gameplay.txt')
[ordered]@{source=$source;destination=$destination;regularFileCopies=$copied;port=$Port;sourceExeSha256=(Get-FileHash -LiteralPath (Join-Path $source 'SCPSL.exe')).Hash.ToLowerInvariant();stagedExeSha256=(Get-FileHash -LiteralPath (Join-Path $destination 'SCPSL.exe')).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'staging.json')
Write-Output "Prepared $copied independent server files at $destination"
