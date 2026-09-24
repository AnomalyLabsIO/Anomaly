[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [ValidateRange(1,20)] [int] $Pairs = 5,
    [ValidateRange(10,1800)] [int] $TimeoutSeconds = 120,
    [switch] $SkipWarmup,
    [string] $ProfileSnapshotDirectory
)
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
if (Test-Path -LiteralPath $evidence) { throw 'Use a new instrumentation-matrix evidence directory.' }
$markerPath = Join-Path $game 'launch-profiler-staging.json'
if (!(Test-Path -LiteralPath $markerPath)) { throw 'Instrumentation comparison requires an isolated staged game.' }
$marker = Get-Content -Raw -LiteralPath $markerPath | ConvertFrom-Json
if (![string]::Equals($marker.destination,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Stage marker destination mismatch.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
if (!$SkipWarmup) {
    $warm = Join-Path $evidence 'warmup'
    & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $game -EvidenceDirectory $warm -Mode milestones -Experiment baseline -TimeoutSeconds $TimeoutSeconds
    $warmResult = Join-Path $warm 'result.json'
    if (!(Test-Path -LiteralPath $warmResult) -or !(Get-Content -Raw -LiteralPath $warmResult | ConvertFrom-Json).success) { throw 'Warmup failed; stop before measuring instrumentation overhead.' }
}
if (!$ProfileSnapshotDirectory) {
    $ProfileSnapshotDirectory = Join-Path $evidence 'private-warm-fixture'
    New-Item -ItemType Directory -Path $ProfileSnapshotDirectory | Out-Null
    foreach ($folder in @('UserData','ProfileAppData')) { Copy-Item -LiteralPath (Join-Path $game $folder) -Destination $ProfileSnapshotDirectory -Recurse }
    [ordered]@{schemaVersion=1;game=$game;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');containsPrivateIdentity=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ProfileSnapshotDirectory 'profile-snapshot.json')
}
$snapshot = [IO.Path]::GetFullPath($ProfileSnapshotDirectory)
$snapshotMarker = Join-Path $snapshot 'profile-snapshot.json'
if (!(Test-Path -LiteralPath $snapshotMarker) -or ![string]::Equals((Get-Content -Raw -LiteralPath $snapshotMarker | ConvertFrom-Json).game,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'The shared private profile snapshot must belong to the same staged game.' }
$runs = [Collections.Generic.List[object]]::new()
$index = 0
for ($pair=1; $pair -le $Pairs; $pair++) {
    $order = if ($pair % 2 -eq 1) { @('milestones','detailed') } else { @('detailed','milestones') }
    foreach ($mode in $order) {
        $index++
        $id = '{0:D3}-pair{1}-{2}' -f $index,$pair,$mode
        $role = if ($mode -eq 'milestones') { 'baseline' } else { 'candidate' }
        $runs.Add([ordered]@{runId=$id;pairId="instrumentation-$pair";candidate='detailed-overhead';experiment='baseline';condition=$mode;mode=$mode;variantRole=$role;game=$game;snapshot=$snapshot;index=$index;evidenceDirectory=(Join-Path $evidence $id)})
    }
}
[ordered]@{schemaVersion=1;matrixType='instrumentation';pairs=$Pairs;baselineLabel='milestones';candidateLabel='detailed';game=$game;privateProfileSnapshot=$snapshot;comparisonPolicy='Same staged game, full binary manifest, restored private profile, actual display and capture class; only profiler mode differs. This measures additional detailed-mode instrumentation cost above the milestone probe, not absolute profiler-free overhead.';runs=$runs} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence 'matrix-plan.json')
foreach ($run in $runs) {
    if (Test-Path -LiteralPath (Join-Path $evidence 'stop-request.json')) {
        [ordered]@{stopped=$true;nextRun=$run.runId;reason='stop-request.json observed between runs';stoppedUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'matrix-stopped.json')
        Write-Output 'Instrumentation matrix stopped between runs at the local stop-request marker.'
        break
    }
    try {
        & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $game -EvidenceDirectory $run.evidenceDirectory -Mode $run.mode -Experiment baseline -RunId $run.runId -TimeoutSeconds $TimeoutSeconds -ProfileSnapshotDirectory $snapshot
        $run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'matrix-run.json')
    } catch {
        New-Item -ItemType Directory -Force -Path $run.evidenceDirectory | Out-Null
        [ordered]@{schemaVersion=1;success=$false;failure=$_.Exception.Message;runId=$run.runId;experiment='baseline';mode=$run.mode;menuReadyQpc=0} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'runner-failure.json')
        Write-Warning "Failed instrumentation run $($run.runId): $($_.Exception.Message)"
        foreach ($running in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
            if ([string]::Equals($running.Path,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)) { throw 'The isolated process is still running; matrix stopped without touching it.' }
        }
    }
}
& (Join-Path $PSScriptRoot 'New-Report.ps1') -EvidenceDirectory $evidence
