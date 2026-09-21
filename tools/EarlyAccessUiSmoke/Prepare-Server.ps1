[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SourceServerRoot,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [ValidateRange(1024,65535)] [int] $Port = 27988
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceServerRoot)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($destination.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Server destination must be separate from source.' }
if (Test-Path -LiteralPath $destination) { throw 'Use a new server destination.' }
if (!(Test-Path -LiteralPath (Join-Path $source 'SCPSL.exe'))) { throw 'Source server executable is missing.' }
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath $source -Recurse -File)) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $parts = $relative.Split([IO.Path]::DirectorySeparatorChar)
    if ($parts[0] -in @('AppData','Logs','Dumps','LabAPI','plugins','dependencies') -or
        $file.Name -match '(?i)^(hoster_policy\.txt|.*\.log)$' -or
        $file.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if ($file.Extension -in @('.dll','.exe','.assets','.resource','.resS')) {
        New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
    } else { Copy-Item -LiteralPath $file.FullName -Destination $target }
}
$labRoot = Join-Path $destination 'AppData/SCP Secret Laboratory/LabAPI'
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
foreach ($entry in ($layout.packages | Where-Object packageId -eq 'anomaly-server').entries) {
    $target = Join-Path $labRoot $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination $target
}
$configuration = Join-Path $destination "AppData/config/$Port"
New-Item -ItemType Directory -Force -Path $configuration | Out-Null
@('gamedir_for_configs: true','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1') | Set-Content -LiteralPath (Join-Path $destination 'hoster_policy.txt')
@('server_name: Anomaly early access loopback test','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1','server_ip: 127.0.0.1','online_mode: false','enable_query: false','contact_email: default','max_players: 4') |
    Set-Content -LiteralPath (Join-Path $configuration 'config_gameplay.txt')
Write-Output "Prepared loopback-only test server at $destination; port $Port."
