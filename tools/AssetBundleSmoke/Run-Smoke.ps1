[CmdletBinding()]
param(
    [string] $GameDirectory = (Join-Path $PSScriptRoot '../../artifacts/assetbundles/native-game-01'),
    [string] $LogoAssetName = 'game/skin/logo',
    [string] $BackgroundAssetName = 'game/skin/background',
    [ValidateRange(30,600)] [int] $TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\','/')
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/assetbundles')).TrimEnd('\','/')
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
if (!$game.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Game must be within this repository artifacts/assetbundles directory.' }
if ((Get-Item -LiteralPath $game).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Game must not be a reparse point.' }
$marker = Get-Content -Raw -LiteralPath (Join-Path $game 'assetbundle-smoke-staging.json') | ConvertFrom-Json
if ($marker.purpose -ne 'AssetBundleSmoke.v1' -or !$marker.complete -or ![string]::Equals($marker.destination,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Complete matching staging ownership metadata is required.' }
foreach ($inputFile in $marker.inputs) {
    if ((Get-FileHash -LiteralPath (Join-Path $game $inputFile.path) -Algorithm SHA256).Hash -ne $inputFile.sha256) { throw "Stage input changed: $($inputFile.path)" }
}
$bundle = Join-Path $game 'Fixture/skin.bundle'
if ((Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash -ne $marker.bundleSha256) { throw 'Fixture hash changed after staging.' }
$evidence = Join-Path $game 'Evidence'
if (Test-Path -LiteralPath $evidence) { throw 'This isolated stage has already been run; evidence is never overwritten.' }
$executable = Join-Path $game 'SCPSL.exe'
foreach ($existing in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
    if ([string]::Equals($existing.Path,$executable,[StringComparison]::OrdinalIgnoreCase)) { throw 'This isolated game is already running.' }
}
New-Item -ItemType Directory -Path $evidence | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.WorkingDirectory = $game
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
foreach ($key in @($start.Environment.Keys)) {
    if ($key.StartsWith('ANOMALY_') -and ($key.Contains('SMOKE') -or $key.Contains('PROFILE') -or $key.Contains('QUALIFICATION'))) { $start.Environment.Remove($key) | Out-Null }
}
$start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_DIR'] = $evidence
$start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_BUNDLE'] = $bundle
$start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_SHA256'] = $marker.bundleSha256
$start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_LOGO'] = $LogoAssetName
$start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_BACKGROUND'] = $BackgroundAssetName
if ($marker.productionPipeline) { $start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_PIPELINE'] = '1' }
if ($marker.network) {
    $start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_NETWORK'] = '1'
    $start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_NETWORK_MODE'] = $marker.networkMode
}
if ($marker.localFallback) { $start.Environment['ANOMALY_ASSETBUNDLE_SMOKE_LOCAL_FALLBACK'] = $marker.localFallback }
if ($marker.qualificationSha256) {
    $start.Environment['ANOMALY_ASSETBUNDLE_QUALIFICATION_INDEX'] = Join-Path $game 'Fixture/qualification.index.json'
    $start.Environment['ANOMALY_ASSETBUNDLE_QUALIFICATION_BUNDLE'] = Join-Path $game 'Fixture/qualification.bundle'
    $start.Environment['ANOMALY_ASSETBUNDLE_QUALIFICATION_SHA256'] = $marker.qualificationSha256
}
$start.Environment['APPDATA'] = Join-Path $game 'ProfileAppData'
$start.Environment['LOCALAPPDATA'] = Join-Path $game 'ProfileAppData/Local'
New-Item -ItemType Directory -Force -Path $start.Environment['LOCALAPPDATA'] | Out-Null
foreach ($argument in @('-noauth','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $evidence 'player.log'),'-appdatapath',(Join-Path $game 'ProfileAppData'),'-configpath',(Join-Path $game 'ProfileAppData/config'),'--melonloader.hideconsole','--melonloader.agfoffline')) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
$started = $process.StartTime.ToUniversalTime()
$run = [ordered]@{pid=$process.Id;executable=$executable;startedUtc=$started.ToString('O');arguments=@($start.ArgumentList);timeoutSeconds=$TimeoutSeconds;bundleSha256=$marker.bundleSha256;inputs=$marker.inputs}
$run | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'run.json')
Write-Output "Started private bundle smoke PID $($process.Id). Evidence: $evidence"
$timedOut = $false
try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500; $process.Refresh() }
    if (!$process.HasExited) {
        $timedOut = $true
        $process.Refresh()
        if (!$process.HasExited -and [string]::Equals($process.MainModule.FileName,$executable,[StringComparison]::OrdinalIgnoreCase) -and $process.StartTime.ToUniversalTime() -eq $started) {
            Stop-Process -Id $process.Id
            $process.WaitForExit(5000) | Out-Null
        } else { throw 'Refusing to stop a process whose executable/start-time ownership changed.' }
    }
} finally {
    [ordered]@{pid=$process.Id;timedOut=$timedOut;hasExited=$process.HasExited;exitCode=$(if($process.HasExited){$process.ExitCode}else{$null});finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'process-result.json')
}
$resultPath = Join-Path $evidence 'native-gate.json'
if (!(Test-Path -LiteralPath $resultPath)) { throw 'Harness produced no native gate result; inspect private player/MelonLoader logs.' }
$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
Write-Output (ConvertTo-Json $result -Depth 5)
if ($marker.localFallback) {
    $fallback = Get-Content -Raw -LiteralPath (Join-Path $evidence 'local-fallback.json') | ConvertFrom-Json
    if ($result.status -ne 'passed' -or $fallback.status -ne 'passed' -or $fallback.nativeLoadAttempts -ne 0 -or
        !$fallback.stockPreloader -or !$fallback.stockMainMenu -or !$fallback.rawSkinsIgnored) { throw 'Local fallback qualification failed.' }
    exit 0
}
if ($result.status -ne 'passed' -or !$result.nativePassed -or !$result.preloaderApplied -or !$result.preloaderCaptured -or !$result.menuApplied -or !$result.menuCaptured -or !$result.unloadPassed) { throw "Native/display gate did not fully pass: $($result.status)" }
if ($marker.qualificationSha256) {
    $qualification = Get-Content -Raw -LiteralPath (Join-Path $evidence 'qualification.json') | ConvertFrom-Json
    if ($qualification.status -ne 'passed' -or !$qualification.sprite -or !$qualification.mesh -or
        !$qualification.nativeAudioPlayback -or !$qualification.trustedVisual -or !$qualification.remoteTeardown) { throw 'Advanced native qualification did not fully pass.' }
}
if ($marker.network) {
    $network = Get-Content -Raw -LiteralPath (Join-Path $evidence 'network.json') | ConvertFrom-Json
    if ($network.status -ne 'passed') { throw 'Authenticated bundle network qualification did not fully pass.' }
}
