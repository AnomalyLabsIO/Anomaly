[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineGameDirectory,
    [Parameter(Mandatory)] [string] $CandidateGameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [Parameter(Mandatory)] [string] $BaselineLabel,
    [Parameter(Mandatory)] [string] $CandidateLabel,
    [ValidateRange(1,20)] [int] $Pairs = 5,
    [ValidateSet('milestones','detailed')] [string] $Mode = 'milestones',
    [ValidateRange(10,1800)] [int] $TimeoutSeconds = 120,
    [switch] $SkipWarmup
)
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
if (Test-Path -LiteralPath $evidence) { throw 'Use a new loadout-matrix evidence directory.' }
if ($BaselineLabel -eq $CandidateLabel -or $BaselineLabel -notmatch '^[a-zA-Z0-9_-]+$' -or $CandidateLabel -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use distinct simple condition labels (letters, numbers, hyphen, underscore).' }
$stages = @(
    [ordered]@{role='baseline';label=$BaselineLabel;game=[IO.Path]::GetFullPath($BaselineGameDirectory).TrimEnd('\','/');snapshot=(Join-Path $evidence 'private-baseline-fixture')},
    [ordered]@{role='candidate';label=$CandidateLabel;game=[IO.Path]::GetFullPath($CandidateGameDirectory).TrimEnd('\','/');snapshot=(Join-Path $evidence 'private-candidate-fixture')}
)
if ([string]::Equals($stages[0].game,$stages[1].game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Loadout comparison requires two separate private stages.' }
foreach ($stage in $stages) {
    $markerPath=Join-Path $stage.game 'launch-profiler-staging.json'
    if (!(Test-Path -LiteralPath $markerPath)) { throw "Missing stage marker: $($stage.game)" }
    $marker=Get-Content -Raw -LiteralPath $markerPath|ConvertFrom-Json
    if (![string]::Equals($marker.destination,$stage.game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Stage marker destination mismatch.' }
}
New-Item -ItemType Directory -Path $evidence|Out-Null
foreach ($stage in $stages) {
    if (!$SkipWarmup) {
        $warm=Join-Path $evidence ('warmup-' + $stage.label)
        & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $stage.game -EvidenceDirectory $warm -Mode $Mode -Experiment baseline -TimeoutSeconds $TimeoutSeconds
        $warmResult=Join-Path $warm 'result.json'
        if (!(Test-Path -LiteralPath $warmResult) -or !(Get-Content -Raw -LiteralPath $warmResult|ConvertFrom-Json).success) { throw "Warmup failed for $($stage.label); stop before measuring." }
    }
    New-Item -ItemType Directory -Path $stage.snapshot|Out-Null
    foreach ($folder in @('UserData','ProfileAppData')) { Copy-Item -LiteralPath (Join-Path $stage.game $folder) -Destination $stage.snapshot -Recurse }
    [ordered]@{schemaVersion=1;game=$stage.game;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');containsPrivateIdentity=$true;condition=$stage.label}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stage.snapshot 'profile-snapshot.json')
}
$runs=[Collections.Generic.List[object]]::new()
$comparison="$BaselineLabel-vs-$CandidateLabel"
$index=0
for($pair=1;$pair -le $Pairs;$pair++) {
    $order=if($pair % 2 -eq 1){@($stages[0],$stages[1])}else{@($stages[1],$stages[0])}
    foreach($stage in $order) {
        $index++
        $id='{0:D3}-pair{1}-{2}' -f $index,$pair,$stage.label
        $runs.Add([ordered]@{runId=$id;pairId="$comparison-$pair";candidate=$comparison;experiment='baseline';condition=$stage.label;variantRole=$stage.role;game=$stage.game;snapshot=$stage.snapshot;index=$index;evidenceDirectory=(Join-Path $evidence $id)})
    }
}
[ordered]@{schemaVersion=1;matrixType='loadout';mode=$Mode;pairs=$Pairs;baselineLabel=$BaselineLabel;candidateLabel=$CandidateLabel;comparisonPolicy='Equal game/loader/profiler inputs and display; each condition has its own fixed warmed profile and mod inventory. Identities and intentional mod groups differ between conditions.';runs=$runs}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $evidence 'matrix-plan.json')
foreach($run in $runs) {
    if(Test-Path -LiteralPath (Join-Path $evidence 'stop-request.json')) {
        [ordered]@{stopped=$true;nextRun=$run.runId;reason='stop-request.json observed between runs';stoppedUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'matrix-stopped.json')
        Write-Output 'Loadout matrix stopped between runs at the local stop-request marker.'
        break
    }
    try {
        & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $run.game -EvidenceDirectory $run.evidenceDirectory -Mode $Mode -Experiment baseline -RunId $run.runId -TimeoutSeconds $TimeoutSeconds -ProfileSnapshotDirectory $run.snapshot
        $run|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'matrix-run.json')
    } catch {
        New-Item -ItemType Directory -Force -Path $run.evidenceDirectory|Out-Null
        [ordered]@{schemaVersion=1;success=$false;failure=$_.Exception.Message;runId=$run.runId;experiment='baseline';mode=$Mode;menuReadyQpc=0}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'runner-failure.json')
        Write-Warning "Failed loadout run $($run.runId): $($_.Exception.Message)"
        foreach($running in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
            if([string]::Equals($running.Path,(Join-Path $run.game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)){throw 'The isolated process is still running; matrix stopped without touching it.'}
        }
    }
}
& (Join-Path $PSScriptRoot 'New-Report.ps1') -EvidenceDirectory $evidence
