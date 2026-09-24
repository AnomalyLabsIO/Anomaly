[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [ValidateSet('offline','deferred-settings','deferred-cleanup','identity-once','unchanged-module-lock','no-skins')]
    [string[]] $Experiments = @('offline'),
    [ValidateRange(1,20)] [int] $Pairs = 5,
    [ValidateSet('milestones','detailed')] [string] $Mode = 'milestones',
    [ValidateRange(10,1800)] [int] $TimeoutSeconds = 120,
    [switch] $SkipWarmup,
    [string] $ProfileSnapshotDirectory = '',
    [string[]] $VariantManifests = @()
)
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
$marker = Join-Path $game 'launch-profiler-staging.json'
if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Only a marked isolated profiling game may be used.' }
$staging = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
if (![string]::Equals($staging.destination,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Staging destination does not match.' }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new matrix evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
if (!$SkipWarmup) {
    & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $game -EvidenceDirectory (Join-Path $evidence 'warmup') -Mode $Mode -Experiment baseline -TimeoutSeconds $TimeoutSeconds
    $warmPath = Join-Path $evidence 'warmup/result.json'
    if (!(Test-Path -LiteralPath $warmPath) -or !(Get-Content -Raw -LiteralPath $warmPath | ConvertFrom-Json).success) {
        throw 'Warmup did not reach menu readiness; fix the fixture before running a matrix.'
    }
}
if (!$ProfileSnapshotDirectory) {
    $ProfileSnapshotDirectory = Join-Path $evidence 'private-warm-fixture'
    New-Item -ItemType Directory -Path $ProfileSnapshotDirectory | Out-Null
    foreach ($folder in @('UserData','ProfileAppData')) { Copy-Item -LiteralPath (Join-Path $game $folder) -Destination $ProfileSnapshotDirectory -Recurse }
    [ordered]@{schemaVersion=1;game=$game;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');containsPrivateIdentity=$true} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ProfileSnapshotDirectory 'profile-snapshot.json')
}
$runs = [Collections.Generic.List[object]]::new()
$variantMap = @{}
foreach ($manifest in $VariantManifests) {
    $info = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    if ($variantMap.ContainsKey($info.variant)) { throw "Duplicate variant manifest: $($info.variant)" }
    $variantMap[$info.variant] = [IO.Path]::GetFullPath($manifest)
}
$index = 0
foreach ($experiment in $Experiments) {
    if ($experiment -in @('identity-once','unchanged-module-lock') -and !$variantMap.ContainsKey($experiment)) { throw "Experiment $experiment requires a matching VariantManifests entry." }
    for ($pair = 1; $pair -le $Pairs; $pair++) {
        # Alternate order to expose drift, while comparing identical private profile inputs.
        $order = if ($pair % 2 -eq 1) { @('baseline',$experiment) } else { @($experiment,'baseline') }
        foreach ($variant in $order) {
            $index++
            $runId = '{0:D3}-{1}-pair{2}-{3}' -f $index,$experiment,$pair,$variant
            $runs.Add([ordered]@{runId=$runId;pairId="$experiment-$pair";candidate=$experiment;experiment=$variant;index=$index;evidenceDirectory=(Join-Path $evidence $runId)})
        }
    }
}
[ordered]@{schemaVersion=1;game=$game;mode=$Mode;pairs=$Pairs;privateProfileSnapshot=[IO.Path]::GetFullPath($ProfileSnapshotDirectory);runs=$runs} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'matrix-plan.json')
foreach ($run in $runs) {
    if(Test-Path -LiteralPath (Join-Path $evidence 'stop-request.json')) {
        [ordered]@{stopped=$true;nextRun=$run.runId;reason='stop-request.json observed between runs';stoppedUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'matrix-stopped.json')
        Write-Output 'Matrix stopped between runs at the local stop-request marker.'
        break
    }
    try {
        $variantPath = if ($variantMap.ContainsKey($run.experiment)) { $variantMap[$run.experiment] } else { '' }
        & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $game -EvidenceDirectory $run.evidenceDirectory -Mode $Mode -Experiment $run.experiment -RunId $run.runId -TimeoutSeconds $TimeoutSeconds -ProfileSnapshotDirectory $ProfileSnapshotDirectory -VariantManifest $variantPath
        $run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'matrix-run.json')
    } catch {
        New-Item -ItemType Directory -Force -Path $run.evidenceDirectory | Out-Null
        [ordered]@{schemaVersion=1;success=$false;failure=$_.Exception.Message;runId=$run.runId;experiment=$run.experiment;mode=$Mode;menuReadyQpc=0} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.evidenceDirectory 'runner-failure.json')
        Write-Warning "Failed matrix run $($run.runId): $($_.Exception.Message)"
        # Do not continue if the exact isolated process survived an unexpected runner exception.
        foreach ($running in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
            if ([string]::Equals($running.Path,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)) { throw 'The isolated process is still running; matrix stopped without touching it.' }
        }
    }
}
& (Join-Path $PSScriptRoot 'New-Report.ps1') -EvidenceDirectory $evidence
