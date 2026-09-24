[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ServerEvidence,
    [Parameter(Mandatory=$true)] [string] $ClientEvidence,
    [int] $Seed = 12345
)
$ErrorActionPreference = 'Stop'
$server = [IO.Path]::GetFullPath($ServerEvidence)
$client = [IO.Path]::GetFullPath($ClientEvidence)
$checks = [Collections.Generic.List[string]]::new()
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $checks.Add($message)
}
function ReadJson([string]$path) { Get-Content -Raw -LiteralPath $path | ConvertFrom-Json }
function FindRoom($snapshot, $operation) {
    @($snapshot.Rooms | Where-Object { $_.Zone -eq $operation.Zone -and $_.X -eq $operation.X -and $_.Y -eq $operation.Y })
}
function AssertPosition($actual, $expected, [string]$label) {
    for ($axis=0; $axis -lt 3; $axis++) {
        Require ([Math]::Abs($actual[$axis] - $expected[$axis]) -lt 0.0001) "$label axis $axis"
    }
}
$captures = @{}
$lastGeneration = 0
for ($cycle=1; $cycle -le 14; $cycle++) {
    $scenario = (Get-Content -Raw -LiteralPath (Join-Path $client "facility-scenario-$cycle.txt")).Trim()
    $local = ReadJson (Join-Path $client "facility-rooms-$cycle.json")
    $remote = ReadJson (Join-Path $server "facility-generation-$($local.GenerationId).json")
    $content = ReadJson (Join-Path $client "facility-content-$cycle.json")
    Require ($content.readiness -eq 'Declined') "Cycle $cycle optional content reaches the requested consent outcome"
    Require ($local.Seed -eq $Seed -and $local.Seed -eq $remote.Seed) "Cycle $cycle fixed seed matches"
    Require ($local.GenerationId -eq $remote.GenerationId -and $local.SnapshotDigest -eq $remote.SnapshotDigest) "Cycle $cycle authenticated snapshot matches"
    Require ($local.RoomsDigest -eq $remote.RoomsDigest -and $local.Rooms.Count -eq $remote.Rooms.Count) "Cycle $cycle exact sorted room/candidate/transform/identity descriptors match"
    if ($cycle -eq 2 -or $cycle -eq 14) { Require ($local.GenerationId -eq $lastGeneration) "Cycle $cycle late reconnect reuses current generation" }
    elseif ($cycle -gt 2) { Require ($local.GenerationId -gt $lastGeneration) "Cycle $cycle advances generation despite same seed" }
    $lastGeneration = $local.GenerationId
    $captures[$cycle] = $remote
}
$baseline = $captures[1]
Require ($baseline.RoomsDigest -eq $captures[2].RoomsDigest) 'Late reconnect preserves all descriptors'
Require ($baseline.RoomsDigest -eq $captures[3].RoomsDigest) 'Managed no-op adapter exactly matches pristine native generation'
foreach ($pair in @(@(4,'lcz'),@(5,'hcz'),@(6,'entrance'),@(7,'atlas'))) {
    $result = $captures[$pair[0]]
    $scenario = $pair[1]
    $recipe = ReadJson (Join-Path $server "recipe-$scenario.json")
    foreach ($operation in $recipe.Applied) {
        $actual = @(FindRoom $result $operation)
        $prior = @(FindRoom $baseline $operation)
        Require ($prior.Count -eq 1) "$scenario target exists in baseline at $($operation.Zone)/$($operation.X)/$($operation.Y)"
        if ($operation.Operation -in @('skip','atlas-remove')) {
            Require ($actual.Count -eq 0) "$scenario removes requested tile"
            continue
        }
        Require ($actual.Count -eq 1) "$scenario keeps transformed tile"
        if ($operation.Operation -eq 'move') {
            $expected = @(for($axis=0; $axis -lt 3; $axis++) { $prior[0].Position[$axis] + $operation.Offset[$axis] })
            if ($scenario -eq 'entrance' -and $operation.Zone -eq 3) {
                # HCZ mean (15,0,0) minus Entrance mean (0,0,15).
                $expected[0] += 15; $expected[2] -= 15
            }
            AssertPosition $actual[0].Position $expected "$scenario translated tile"
        }
        if ($operation.Operation -eq 'rotate') {
            $half = $operation.Yaw * [Math]::PI / 360
            $expected = @(0,[Math]::Sin($half),0,[Math]::Cos($half))
            for($axis=0; $axis -lt 4; $axis++) {
                Require ([Math]::Abs($actual[0].Rotation[$axis] - $expected[$axis]) -lt 0.00001) "$scenario absolute rotation axis $axis"
            }
        }
    }
}
Require ($captures[7].RoomsDigest -eq $captures[8].RoomsDigest) 'Repeated atlas round preserves descriptors with a fresh generation'
Require ($baseline.RoomsDigest -eq $captures[9].RoomsDigest) 'Whole-layout event with no edits exactly matches pristine native generation'
foreach ($pair in @(@(9,'planned-noop'),@(10,'planned-move'),@(11,'planned-remove'),@(12,'planned-checkpoint'),@(13,'planned-remove'))) {
    $result = $captures[$pair[0]]
    $scenario = $pair[1]
    $recipe = ReadJson (Join-Path $server "recipe-generation-$($result.GenerationId).json")
    Require ($recipe.WholeLayoutEvent -and $recipe.GenerationId -eq $result.GenerationId -and $recipe.Seed -eq $Seed) "$scenario whole-layout event describes the generated snapshot"
    Require ($recipe.PlannedRooms -eq $baseline.Rooms.Count) "$scenario inspects the complete original layout"
    Require ($recipe.MaterializedRooms -eq 0) "$scenario whole-layout event precedes atlas room instantiation"
    foreach ($operation in $recipe.Applied) {
        $actual = @(FindRoom $result $operation)
        $prior = @(FindRoom $baseline $operation)
        Require ($prior.Count -eq 1) "$scenario target exists in the native baseline"
        if ($operation.Operation -eq 'skip') {
            Require ($actual.Count -eq 0) "$scenario removes the requested room and its identity descriptors"
            if ($scenario -eq 'planned-remove') {
                Require ($operation.RemovedDuplicateId -eq 0 -and $operation.RepeatedPrefabCount -gt 1) 'Whole-layout removal selects the first occurrence of a repeated networked prefab'
            }
            continue
        }
        Require ($actual.Count -eq 1) "$scenario retains the transformed room"
        if ($operation.Operation -eq 'move') {
            $expected = @(for($axis=0; $axis -lt 3; $axis++) { $prior[0].Position[$axis] + $operation.Offset[$axis] })
            AssertPosition $actual[0].Position $expected "$scenario absolute final placement"
        }
        if ($operation.Operation -eq 'rotate') {
            $half = $operation.Yaw * [Math]::PI / 360
            $expected = @(0,[Math]::Sin($half),0,[Math]::Cos($half))
            for($axis=0; $axis -lt 4; $axis++) {
                Require ([Math]::Abs($actual[0].Rotation[$axis] - $expected[$axis]) -lt 0.00001) "$scenario final rotation axis $axis"
            }
        }
    }
    $removed = @($recipe.Applied | Where-Object Operation -eq 'skip').Count
    Require ($result.Rooms.Count -eq $baseline.Rooms.Count - $removed) "$scenario changes only the requested room count"
    foreach ($prior in $baseline.Rooms) {
        $operations = @($recipe.Applied | Where-Object { $_.Zone -eq $prior.Zone -and $_.X -eq $prior.X -and $_.Y -eq $prior.Y })
        if (@($operations | Where-Object Operation -eq 'skip').Count) { continue }
        $actual = @(FindRoom $result $prior)
        Require ($actual.Count -eq 1) "$scenario retains baseline room $($prior.Zone)/$($prior.X)/$($prior.Y)"
        $excluded = @()
        if (@($operations | Where-Object Operation -eq 'move').Count) { $excluded += 'Position' }
        if (@($operations | Where-Object Operation -eq 'rotate').Count) { $excluded += 'Rotation' }
        $before = $prior | Select-Object * -ExcludeProperty $excluded | ConvertTo-Json -Compress -Depth 20
        $after = $actual[0] | Select-Object * -ExcludeProperty $excluded | ConvertTo-Json -Compress -Depth 20
        Require ($before -eq $after) "$scenario preserves unedited transforms, candidate, duplicate ordinal and child identities at $($prior.Zone)/$($prior.X)/$($prior.Y)"
    }
}
Require ($captures[11].RoomsDigest -eq $captures[13].RoomsDigest) 'Repeated planned removal preserves descriptors with a fresh generation'
Require ($captures[13].RoomsDigest -eq $captures[14].RoomsDigest) 'Late join after planned removal preserves authoritative descriptors'
$report = [ordered]@{ passed=$true; checks=$checks.Count; comparisons=$checks; client=$client; server=$server; nativeAutomaticRestartVerified=$true; restartMode='Server native fast restart and native client reconnect' }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $client 'facility-comparison.json')
Write-Output "Passed $($checks.Count) facility assertions; evidence: $(Join-Path $client 'facility-comparison.json')"
