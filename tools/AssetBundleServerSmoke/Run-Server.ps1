[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerDirectory,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [Parameter(Mandatory=$true)][string]$AssetDirectory,
    [int]$Port=27989
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$server=[IO.Path]::GetFullPath($ServerDirectory)
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
$assets=[IO.Path]::GetFullPath($AssetDirectory)
$allowed=(Join-Path $repo 'artifacts/assetbundles')+[IO.Path]::DirectorySeparatorChar
if (!$server.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !$evidence.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !$assets.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'All runtime state must be private assetbundles artifacts.' }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory; keep the server AppData to preserve its identity.' }
$policy=Get-Content -LiteralPath (Join-Path $server 'hoster_policy.txt')
if ('ipv4_bind_ip: 127.0.0.1' -notin $policy -or 'ipv6_bind_ip: ::1' -notin $policy) { throw 'Loopback-only binding required.' }
& dotnet build (Join-Path $PSScriptRoot 'AssetBundleServerSmoke.csproj') -c Debug -p:DeployLocal=false --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
$lab=Join-Path $server 'AppData/SCP Secret Laboratory/LabAPI'
$inputs=@(
    @('Anomaly.Server/bin/Debug/Anomaly.Server.dll','plugins/global/Anomaly.Server.dll'),
    @('Anomaly.Shared/bin/Debug/Anomaly.Shared.dll','dependencies/global/Anomaly.Shared.dll'),
    @('Anomaly.Server/bin/Debug/0Harmony.dll','dependencies/global/0Harmony.dll'),
    @('tools/AssetBundleServerSmoke/bin/Debug/net48/Anomaly.AssetBundleServerSmoke.dll','plugins/global/Anomaly.AssetBundleServerSmoke.dll')
)
foreach($entry in $inputs) { $target=Join-Path $lab $entry[1]; New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null; Copy-Item -LiteralPath (Join-Path $repo $entry[0]) -Destination $target }
$config=Join-Path $lab "configs/$Port/Anomaly/config.yml"
New-Item -ItemType Directory -Force -Path (Split-Path $config) | Out-Null
"asset_directory: '$($assets.Replace("'","''"))'" | Set-Content -LiteralPath $config
New-Item -ItemType Directory -Path $evidence | Out-Null
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $server 'SCPSL.exe'))
$start.WorkingDirectory=$server
$start.UseShellExecute=$false
$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$start.Environment['ANOMALY_BUNDLE_SERVER_SMOKE_DIR']=$evidence
foreach($argument in @('-batchmode','-nographics','-stdout',"-port$Port",'-appdatapath',(Join-Path $server 'AppData'),'-configpath',(Join-Path $server "AppData/config/$Port"),'-logFile',(Join-Path $evidence 'server-player.log'))) { $start.ArgumentList.Add($argument) }
$process=[Diagnostics.Process]::Start($start)
$process.Id | Set-Content -LiteralPath (Join-Path $evidence 'server.pid')
$hashes=@($inputs | ForEach-Object { $path=Join-Path $lab $_[1]; [ordered]@{path=$_[1];sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()} })
[ordered]@{server=$server;assets=$assets;port=$Port;processId=$process.Id;utc=[DateTimeOffset]::UtcNow.ToString('O');inputs=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
Write-Output "Loopback server PID $($process.Id); evidence $evidence"
