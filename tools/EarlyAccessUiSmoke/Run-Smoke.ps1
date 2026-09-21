[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $GameDirectory,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [string] $LoopbackServer = '',
    [switch] $ConnectionOnly
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game = [IO.Path]::GetFullPath($GameDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$stagingPath = Join-Path $game 'smoke-staging.json'
if (!(Test-Path -LiteralPath $stagingPath)) { throw 'Only a game created by Prepare-Game.ps1 may be used.' }
$staging = Get-Content -Raw -LiteralPath $stagingPath | ConvertFrom-Json
if (![string]::Equals($staging.destination, $game, [StringComparison]::OrdinalIgnoreCase)) { throw 'The staging destination does not match.' }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory for each run.' }
if ($LoopbackServer) {
    $uri = [Uri]('udp://' + $LoopbackServer)
    $address = $null
    if (![Net.IPAddress]::TryParse($uri.Host, [ref]$address) -or ![Net.IPAddress]::IsLoopback($address)) {
        throw 'The test server must be an explicit loopback IP endpoint.'
    }
}
if ($ConnectionOnly -and !$LoopbackServer) { throw 'ConnectionOnly requires a loopback test server.' }
foreach ($project in @('Anomaly.Client/Anomaly.Client.csproj','samples/HelloAnomalyMod/HelloAnomalyMod.csproj','tools/EarlyAccessUiSmoke/EarlyAccessUiSmoke.csproj')) {
    & dotnet build (Join-Path $repository $project) -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Smoke build failed: $project" }
}
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
foreach ($entry in ($layout.packages | Where-Object packageId -eq 'anomaly-client').entries) {
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination (Join-Path $game $entry.path)
}
foreach ($mod in @('samples/HelloAnomalyMod/bin/Release/net6.0/HelloAnomalyMod.dll','tools/EarlyAccessUiSmoke/bin/Release/net6.0/Anomaly.EarlyAccessUiSmoke.dll')) {
    Copy-Item -LiteralPath (Join-Path $repository $mod) -Destination (Join-Path $game 'Mods')
}
# Approval is restricted to this isolated local test instance. This does not
# exercise the launcher's official-package signature or install workflow.
$approved = @(Get-ChildItem -LiteralPath (Join-Path $game 'Mods') -Filter '*.dll' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
@{ schemaVersion=1; mode='enforce'; approvedSha256=$approved } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $game 'UserData/Anomaly/mod-trust.json')
New-Item -ItemType Directory -Path $evidence | Out-Null
$sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the smoke source commit.' }
$sourceStatus = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the smoke source worktree.' }
$inputPaths = @(($layout.packages | Where-Object packageId -eq 'anomaly-client').entries.path) +
    @('Mods/HelloAnomalyMod.dll','Mods/Anomaly.EarlyAccessUiSmoke.dll','MelonLoader/net6/MelonLoader.dll','SCPSL.exe')
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    $file = Get-Item -LiteralPath (Join-Path $game $_)
    [ordered]@{ path=$_; size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ sourceCommit=$sourceCommit; sourceWorktreeClean=($sourceStatus.Count -eq 0); startedUtc=[DateTimeOffset]::UtcNow.ToString('O'); inputs=$inputs } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
$startInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $game 'SCPSL.exe'))
$startInfo.WorkingDirectory = $game
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Environment['ANOMALY_UI_SMOKE_DIR'] = $evidence
if ($LoopbackServer) { $startInfo.Environment['ANOMALY_SMOKE_SERVER'] = $LoopbackServer }
if ($ConnectionOnly) { $startInfo.Environment['ANOMALY_UI_SMOKE_CONNECTION_ONLY'] = '1' }
foreach ($argument in @('-noauth','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $evidence 'player.log'))) {
    $startInfo.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($startInfo)
$process.Id | Set-Content -LiteralPath (Join-Path $evidence 'client.pid')
Write-Output "Started isolated smoke client PID $($process.Id). Evidence: $evidence"
