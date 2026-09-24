[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [string] $NativeDirectory = '',
    [string] $ProfileSnapshotDirectory = '',
    [ValidateRange(10,1800)] [int] $TimeoutSeconds = 120,
    [switch] $CheckOnly
)
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$NativeDirectory) { $NativeDirectory = $evidence + '-native' }
$native = [IO.Path]::GetFullPath($NativeDirectory)
$wpr = (Get-Command wpr.exe -ErrorAction Stop).Source
$logman = (Get-Command logman.exe -ErrorAction Stop).Source
$analyzer = Join-Path $PSScriptRoot 'NativeSummary/bin/Release/net8.0-windows/NativeSummary.dll'
$instance = 'AnomalyLaunchProfiler_' + [Guid]::NewGuid().ToString('N')
$status = @(& $wpr -status 2>&1 | ForEach-Object { "$_" })
$statusExit = $LASTEXITCODE
$sessions = @(& $logman query -ets 2>&1 | ForEach-Object { "$_" })
$sessionExit = $LASTEXITCODE
$wprSessions = @($sessions | Where-Object { $_ -match '^\s*WPR_' } | ForEach-Object {
    ($_ -replace '\s+Trace\s+Running\s*$', '').Trim()
})
# Explicitly preserve the built-in Windows DiagTrack telemetry recorders. No other
# user, named or default WPR session is allowed during this diagnostic capture.
$telemetryPattern = '^WPR_initiated_DiagTrackMiniLogger_(OneTrace_User_Logger_[A-Za-z0-9_]+|WPR System Collector)$'
$telemetry = @($wprSessions | Where-Object { $_ -match $telemetryPattern })
$conflicts = @($wprSessions | Where-Object { $_ -notmatch $telemetryPattern })
$ready = $statusExit -eq 0 -and $sessionExit -eq 0 -and ($status -join "`n") -match 'WPR is not recording' -and $conflicts.Count -eq 0
$preflight = [ordered]@{
    ready = $ready; wpr = $wpr; analyzerPresent = (Test-Path -LiteralPath $analyzer -PathType Leaf)
    existingTelemetrySessions = $telemetry; conflictingWprSessions = $conflicts
    wprStatus = $status; etwSessionInventory = $sessions; instanceName = $instance
    profiles = @('GeneralProfile.Verbose', 'FileIO.Verbose'); mode = 'file'
}
if ($CheckOnly) { $preflight | ConvertTo-Json -Depth 5; return }
if (!$ready) { throw 'Native capture refused: an existing user WPR recording or an unrecognized recorder state is present. No sessions were changed. Use -CheckOnly for details.' }
if (!$preflight.analyzerPresent) { throw 'Build tools/LaunchProfiler/NativeSummary/NativeSummary.csproj -c Release before tracing.' }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new EvidenceDirectory for each native launch.' }
if (Test-Path -LiteralPath $native) { throw 'Use a new NativeDirectory for each native launch.' }
if (!(Test-Path -LiteralPath (Join-Path $game 'launch-profiler-staging.json') -PathType Leaf)) { throw 'Only the private staged game is supported.' }
if (!(Test-Path -LiteralPath (Join-Path $game 'MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll') -PathType Leaf)) { throw 'Native launch capture requires the existing warm interop cache.' }
if ($native.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $evidence.StartsWith($native + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $native -eq $evidence) { throw 'EvidenceDirectory and NativeDirectory must be separate sibling directories.' }
[IO.Directory]::CreateDirectory($native) | Out-Null
$temporary = Join-Path $native 'wpr-work'
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$etl = Join-Path $native 'launch.etl'
$metadata = [ordered]@{
    schemaVersion = 1; preflight = $preflight; evidenceDirectory = $evidence
    tracePath = $etl; started = $false; stopped = $false; startExit = $null; stopExit = $null; stopError = $null
    startQpc = $null; stopQpc = $null; frequency = [Diagnostics.Stopwatch]::Frequency
    nativeEvidenceOnly = $true; launchError = $null
}
$metadataPath = Join-Path $native 'capture.json'
$started = $false
$failure = $null
try {
    $metadata.startQpc = [Diagnostics.Stopwatch]::GetTimestamp()
    & $wpr -start GeneralProfile.Verbose -start FileIO.Verbose -filemode -recordtempto $temporary -instancename $instance *> (Join-Path $native 'wpr-start.log')
    $metadata.startExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "WPR start failed. See $(Join-Path $native 'wpr-start.log'). No game was launched." }
    $started = $true
    $metadata.started = $true
    $metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath
    $runParameters = @{ GameDirectory = $game; EvidenceDirectory = $evidence; Mode = 'detailed'; Experiment = 'baseline'; TimeoutSeconds = $TimeoutSeconds }
    if ($ProfileSnapshotDirectory) { $runParameters.ProfileSnapshotDirectory = $ProfileSnapshotDirectory }
    & (Join-Path $PSScriptRoot 'Run-Profile.ps1') @runParameters
}
catch { $failure = $_; $metadata.launchError = $_.Exception.Message }
finally {
    # Never cancel a session. A successful start authorizes stopping only our
    # randomly named instance; -instancename must be WPR's final argument.
    if ($started) {
        $metadata.stopQpc = [Diagnostics.Stopwatch]::GetTimestamp()
        try {
            & $wpr -stop $etl 'Anomaly private launch sampled CPU and file IO' -skipPdbGen -instancename $instance *> (Join-Path $native 'wpr-stop.log')
            $metadata.stopExit = $LASTEXITCODE
            $metadata.stopped = $LASTEXITCODE -eq 0
        }
        catch { $metadata.stopError = $_.Exception.Message }
    }
    $metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath
}
if ($metadata.started -and !$metadata.stopped) { throw "The owned WPR instance '$instance' did not stop successfully. See wpr-stop.log; no unrelated session was stopped." }
if ($failure) { throw $failure }
& dotnet $analyzer --etl $etl --run $evidence --output (Join-Path $native 'summary.json') *> (Join-Path $native 'summary.log')
if ($LASTEXITCODE -ne 0) { throw "Native ETL summary failed; the trace is preserved at $etl. See summary.log." }
[pscustomobject]@{ evidenceDirectory = $evidence; nativeDirectory = $native; etl = $etl; summary = Join-Path $native 'summary.json' } | ConvertTo-Json
