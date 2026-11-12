[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ServerDirectory,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string] $ContentPackDirectory,
    [string] $AdmissionRequirements = '',
    [string] $PlainRejection = '',
    [switch] $BlockAnyAdditionalClientMods,
    [switch] $AllowRiskyClientModifications,
    [switch] $SkipBuild,
    [ValidateRange(65536,67108864)] [int] $BulkUploadBytesPerSecond = 16777216,
    [string[]] $BlockedClientModIds = @(),
    [ValidateRange(1024,65535)] [int] $Port = 27988
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$server = [IO.Path]::GetFullPath($ServerDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$contentPackSource = [IO.Path]::GetFullPath($ContentPackDirectory)
if (!$server.StartsWith((Join-Path $repository 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a private server staged under this repository artifacts directory.'
}
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory.' }
$policy = Get-Content -LiteralPath (Join-Path $server 'hoster_policy.txt')
if ('ipv4_bind_ip: 127.0.0.1' -notin $policy -or 'ipv6_bind_ip: ::1' -notin $policy) { throw 'Loopback-only binding is required.' }
foreach ($project in $(if ($SkipBuild) { @() } else { @('Anomaly.Server/Anomaly.Server.csproj', 'tools/ServerApiSmoke/ServerApiSmoke.csproj') })) {
    & dotnet build (Join-Path $repository $project) -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Fixture build failed: $project" }
}
$labRoot = Join-Path $server 'AppData/SCP Secret Laboratory/LabAPI'
$contentPacks = Join-Path $labRoot 'plugins/global/Anomaly/ContentPacks'
if (!(Test-Path -LiteralPath $contentPackSource -PathType Container)) { throw 'Content pack source directory is missing.' }
New-Item -ItemType Directory -Force -Path $contentPacks | Out-Null
Get-ChildItem -LiteralPath $contentPackSource -Force | Copy-Item -Destination $contentPacks -Recurse -Force
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
foreach ($entry in ($layout.packages | Where-Object packageId -eq 'anomaly-server').entries) {
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination (Join-Path $labRoot $entry.path)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/net48/Anomaly.ServerApiSmoke.dll') -Destination (Join-Path $labRoot 'plugins/global')
$config = Join-Path $labRoot "configs/$Port/Anomaly/config.yml"
New-Item -ItemType Directory -Force -Path (Split-Path $config) | Out-Null
$blockedIdsYaml = if ($BlockedClientModIds.Count -eq 0) { '[]' } else {
    "`n" + (($BlockedClientModIds | ForEach-Object { "  - '$($_.Replace("'", "''"))'" }) -join "`n")
}
@(
    "content_pack_directory: 'ContentPacks'"
    "block_any_additional_client_mods: $($BlockAnyAdditionalClientMods.IsPresent.ToString().ToLowerInvariant())"
    "allow_risky_client_modifications: $($AllowRiskyClientModifications.IsPresent.ToString().ToLowerInvariant())"
    "bulk_upload_bytes_per_second: $BulkUploadBytesPerSecond"
    "blocked_client_mod_ids: $blockedIdsYaml"
) | Set-Content -LiteralPath $config
New-Item -ItemType Directory -Path $evidence | Out-Null
$startInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $server 'SCPSL.exe'))
$startInfo.WorkingDirectory = $server
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Environment['ANOMALY_SERVER_SMOKE_DIR'] = $evidence
foreach ($name in @('ANOMALY_MOD_ADMISSION_REQUIREMENTS','ANOMALY_MOD_ADMISSION_PLAIN_REJECTION')) {
    $startInfo.Environment.Remove($name) | Out-Null
}
if ($AdmissionRequirements) { $startInfo.Environment['ANOMALY_MOD_ADMISSION_REQUIREMENTS'] = $AdmissionRequirements }
if ($PlainRejection) { $startInfo.Environment['ANOMALY_MOD_ADMISSION_PLAIN_REJECTION'] = $PlainRejection }
foreach ($argument in @('-batchmode','-nographics','-stdout',"-port$Port",'-appdatapath',(Join-Path $server 'AppData'),'-configpath',(Join-Path $server "AppData/config/$Port"),'-logFile',(Join-Path $evidence 'server-player.log'))) {
    $startInfo.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($startInfo)
$process.Id | Set-Content -LiteralPath (Join-Path $evidence 'server.pid')
$inputPaths = @(($layout.packages | Where-Object packageId -eq 'anomaly-server').entries.path) + @('plugins/global/Anomaly.ServerApiSmoke.dll')
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    $file = Get-Item -LiteralPath (Join-Path $labRoot $_)
    [ordered]@{ path=$_; size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ server=$server; contentPacks=$contentPacks; port=$Port; startedUtc=[DateTimeOffset]::UtcNow.ToString('O'); processId=$process.Id; buildSkipped=$SkipBuild.IsPresent; inputs=$inputs } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
Write-Output "Started private loopback fixture PID $($process.Id). Evidence: $evidence"
