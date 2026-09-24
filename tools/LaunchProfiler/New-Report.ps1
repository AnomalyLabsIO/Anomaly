[CmdletBinding()]
param([Parameter(Mandatory)] [string] $EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
function Read-Json([string] $Path) {
    if (Test-Path -LiteralPath $Path) { try { return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json } catch { return $null } }
    return $null
}
function Get-Median([double[]] $Values) {
    if (!$Values.Count) { return $null }
    $sorted = @($Values | Sort-Object)
    $half = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return [double]$sorted[$half] }
    return ([double]$sorted[$half - 1] + [double]$sorted[$half]) / 2
}
function Get-Stats([double[]] $Values) {
    if (!$Values.Count) { return $null }
    $median = Get-Median $Values
    $min = ($Values | Measure-Object -Minimum).Minimum
    $max = ($Values | Measure-Object -Maximum).Maximum
    $deviations = @($Values | ForEach-Object { [Math]::Abs($_ - $median) })
    return [ordered]@{count=$Values.Count;medianMs=$median;minMs=$min;maxMs=$max;rangeMs=($max-$min);medianAbsoluteDeviationMs=(Get-Median $deviations)}
}
function Test-Manifest($Manifest, [string] $Label, [string[]] $Required, $Reasons) {
    if (!@($Manifest).Count) { $Reasons.Add("missing or empty $Label manifest"); return }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Manifest) {
        $path = [string]$file.path
        if (!$path -or [IO.Path]::IsPathRooted($path) -or $path.Contains('\') -or $path -match '(^|/)\.\.?(/|$)' -or !$seen.Add($path) -or
            !$file.PSObject.Properties['size'] -or $file.size -lt 0 -or [string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
            $Reasons.Add("invalid or duplicate entry in $Label manifest: $path")
        }
    }
    foreach ($requiredPath in $Required) { if (!$seen.Contains($requiredPath)) { $Reasons.Add("$Label manifest missing required input: $requiredPath") } }
}
$plan = Read-Json (Join-Path $evidence 'matrix-plan.json')
$directories = if ($plan) { @($plan.runs | ForEach-Object evidenceDirectory) }
else { @(Get-ChildItem -LiteralPath $evidence -Filter 'runtime-inputs.json' -File -Recurse | ForEach-Object DirectoryName) }
$unattempted = @()
if ($plan -and (Test-Path -LiteralPath (Join-Path $evidence 'matrix-stopped.json'))) {
    $unattempted = @($plan.runs | Where-Object {
        !(Test-Path -LiteralPath (Join-Path $_.evidenceDirectory 'runtime-inputs.json')) -and
        !(Test-Path -LiteralPath (Join-Path $_.evidenceDirectory 'runner-failure.json'))
    })
    $directories = @($directories | Where-Object { $_ -notin $unattempted.evidenceDirectory })
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($directory in $directories) {
    $input = Read-Json (Join-Path $directory 'runtime-inputs.json')
    $result = Read-Json (Join-Path $directory 'result.json')
    $runner = Read-Json (Join-Path $directory 'runner-result.json')
    $trace = Read-Json (Join-Path $directory 'events.json')
    $visual = Read-Json (Join-Path $directory 'visual-review.json')
    $failed = Read-Json (Join-Path $directory 'runner-failure.json')
    $runnerException = Read-Json (Join-Path $directory 'runner-exception.json')
    $restoration = Read-Json (Join-Path $directory 'treatment-restoration.json')
    $meta = if ($plan) { $plan.runs | Where-Object evidenceDirectory -eq $directory | Select-Object -First 1 } else { $null }
    $reasons = [Collections.Generic.List[string]]::new()
    $treatmentAssessment = 'applied or baseline'
    $speedupEligible = $true
    if ($visual -and !$visual.valid) { $reasons.Add('visual review rejected readiness: ' + $visual.reason) }
    if (!$input) { $reasons.Add('missing or invalid runtime-inputs.json') }
    if (!$trace) { $reasons.Add('missing or invalid events.json') }
    if (!$result) { $reasons.Add('missing or invalid result.json') }
    elseif (!$result.success) { $reasons.Add('profiler failure: ' + $result.failure) }
    if (!$runner) { $reasons.Add('missing runner completion record') }
    elseif ($runner.timedOut) { $reasons.Add('runner timed out') }
    if ($runner) {
        if (!$runner.PSObject.Properties['processExited'] -or !$runner.processExited) { $reasons.Add('process was left running or exit evidence is missing') }
        elseif (!$runner.PSObject.Properties['exitCode']) { $reasons.Add('process exit code is missing') }
        elseif ($runner.exitCode -ne 0 -or $runner.stopMethod -eq 'owned-child-kill') { $reasons.Add('process did not complete a clean exit') }
        if (!$runner.resultPresent) { $reasons.Add('runner did not observe profiler completion') }
        if ($input -and ($runner.processId -ne $input.processId -or $runner.runId -ne $input.runId)) { $reasons.Add('runner PID/run ID mismatch') }
    }
    if ($failed) { $reasons.Add('runner exception: ' + $failed.failure) }
    if($runnerException){$reasons.Add('runner exception: ' + $runnerException.failure)}
    if($restoration -and !$restoration.restored){$reasons.Add('private treatment restoration remains pending')}
    if ($input -and $result) {
        $requiredBinaries = @('SCPSL.exe','GameAssembly.dll','UnityPlayer.dll','version.dll','SCPSL_Data/il2cpp_data/Metadata/global-metadata.dat','Plugins/Anomaly.LaunchProfiler.dll','MelonLoader/net6/MelonLoader.dll','MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg')
        if ($input.captureClass -ne 'forced-assembly-generation-with-warm-tool-packages') { $requiredBinaries += 'MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll' }
        Test-Manifest $input.inputs 'binary' $requiredBinaries $reasons
        Test-Manifest $input.profileInputs 'profile' @('UserData/Loader.cfg') $reasons
        if ($input.baselineProfileInputs) { Test-Manifest $input.baselineProfileInputs 'baseline profile' @('UserData/Loader.cfg') $reasons }
        if ($input.processId -ne $result.pid) { $reasons.Add('PID mismatch') }
        if ($input.runId -ne $result.runId) { $reasons.Add('run ID mismatch') }
        if ($input.stopwatchFrequency -ne $result.stopwatchFrequency -or $input.stopwatchFrequency -le 0) { $reasons.Add('QPC frequency mismatch') }
        if ($result.menuReadyQpc -le $input.launchQpc -or $input.launchQpc -le 0) { $reasons.Add('menu readiness absent or invalid QPC') }
        if ($input.mode -ne $result.mode -or $input.experiment -ne $result.experiment) { $reasons.Add('mode/experiment mismatch') }
        if ($result.droppedEvents -gt 0) { $reasons.Add('trace dropped events; instrumentation is incomplete') }
        if ($trace) {
            if ($trace.schemaVersion -ne 1 -or $trace.pid -ne $input.processId -or $trace.runId -ne $input.runId -or $trace.mode -ne $input.mode -or $trace.experiment -ne $input.experiment -or
                $trace.stopwatchFrequency -ne $input.stopwatchFrequency -or $trace.firstPluginQpc -ne $result.firstPluginQpc) { $reasons.Add('trace identity or QPC metadata mismatch') }
            if (!$trace.PSObject.Properties['droppedEvents'] -or $trace.droppedEvents -ne 0) { $reasons.Add('trace dropped-event status missing or nonzero') }
            $readyEvents = @($trace.events | Where-Object name -eq 'menu.ready')
            $earlyEvents = @($trace.events | Where-Object name -eq 'plugin.early')
            if ($readyEvents.Count -ne 1 -or $readyEvents[0].timestampQpc -ne $result.menuReadyQpc) { $reasons.Add('trace does not contain the exact result readiness marker') }
            if ($earlyEvents.Count -ne 1 -or $earlyEvents[0].timestampQpc -ne $result.firstPluginQpc) { $reasons.Add('trace does not contain the exact first plugin marker') }
        }
        if ($input.captureClass -eq 'forced-assembly-generation-with-warm-tool-packages' -and !$runner.generator.generationObserved) { $reasons.Add('forced generator execution was not observed') }
        switch ($input.experiment) {
            'deferred-settings' {
                $deferred = @($trace.events | Where-Object name -eq 'experiment.settings.deferred')
                $replayed = @($trace.events | Where-Object name -eq 'experiment.settings.populate')
                if (!$deferred.Count -or !$replayed.Count) { $reasons.Add('settings deferral/replay treatment was not verified') }
                elseif ($deferred[0].timestampQpc -ge $result.menuReadyQpc) { $treatmentAssessment='settings work was not on the measured launch critical path'; $speedupEligible=$false }
                elseif ($replayed[0].timestampQpc -lt $result.menuReadyQpc) { $reasons.Add('settings replay occurred before readiness') }
            }
            'deferred-cleanup' {
                $deferred = @($trace.events | Where-Object name -eq 'experiment.cleanup.deferred')
                $replayed = @($trace.events | Where-Object name -eq 'experiment.cleanup.run')
                if (!$deferred.Count -or !$replayed.Count -or $deferred[0].timestampQpc -ge $result.menuReadyQpc -or $replayed[0].timestampQpc -lt $result.menuReadyQpc) { $reasons.Add('cleanup deferral/replay treatment was not verified') }
            }
            { $_ -in @('offline','no-skins') } {
                if (!$runner.treatment.applied) { $reasons.Add('treatment was not verified: ' + $runner.treatment.detail) }
            }
            { $_ -in @('identity-once','unchanged-module-lock') } {
                $client = $input.inputs | Where-Object path -eq 'Mods/Anomaly.Client.dll' | Select-Object -First 1
                if (!$input.variant -or !$input.variant.buildSucceeded -or !$input.variant.testsSucceeded -or
                    $input.variant.variant -ne $input.experiment -or $input.variant.clientSha256 -ne $client.sha256 -or
                    $input.variant.clientSha256 -eq $input.variant.baselineClientSha256 -or !$runner.treatment.applied) {
                    $reasons.Add('private built/tested binary treatment was not verified')
                }
            }
        }
    }
    $valid = $reasons.Count -eq 0
    $elapsed = if ($valid) { 1000.0 * ([double]$result.menuReadyQpc - [double]$input.launchQpc) / [double]$input.stopwatchFrequency } else { $null }
    $first = if ($valid) { 1000.0 * ([double]$result.firstPluginQpc - [double]$input.launchQpc) / [double]$input.stopwatchFrequency } else { $null }
    $profileSignature = if ($input) { ($(if($input.baselineProfileInputs){$input.baselineProfileInputs}else{$input.profileInputs}) | ConvertTo-Json -Depth 5 -Compress) } else { '' }
    $binarySignature = if ($input) {
        (@($input.inputs | ForEach-Object {
            if ($input.variant -and $_.path -eq 'Mods/Anomaly.Client.dll') { [pscustomobject][ordered]@{path=$_.path;size=$input.variant.baselineClientSize;sha256=$input.variant.baselineClientSha256} }
            else { $_ }
        }) | ConvertTo-Json -Depth 5 -Compress)
    } else { '' }
    $generatorSignature = if ($runner.generator) { (@($runner.generator.cpp2il,$runner.generator.interop,$runner.generator.unityDependencies,$runner.generator.regex) | ConvertTo-Json -Compress) } else { '' }
    $display = $result.readiness.display
    $displaySignature = if ($display -and $display.width -gt 0 -and $display.height -gt 0 -and $display.backend) { (@($display.width,$display.height,$display.backend) | ConvertTo-Json -Compress) } else { '' }
    $intervals = [Collections.Generic.List[object]]::new()
    if ($input -and $result -and $input.stopwatchFrequency -gt 0 -and $input.launchQpc -gt 0) {
        $boundaries = @([pscustomobject]@{name='process.start-request';timestampQpc=$input.launchQpc})
        $boundaryNames = @('plugin.early','loader.pre-initialization','loader.application-early-start','loader.pre-mods','loader.pre-support','loader.application-start','game.first-update','scene.loaded','scene.initialized','menu.ready')
        $boundaries += @($trace.events | Where-Object { $_.name -in $boundaryNames -and $_.timestampQpc -ge $input.launchQpc } | Sort-Object timestampQpc)
        for ($boundaryIndex=1;$boundaryIndex -lt $boundaries.Count;$boundaryIndex++) {
            $from=$boundaries[$boundaryIndex-1];$to=$boundaries[$boundaryIndex]
            $label = if ($from.name -eq 'process.start-request') { 'unobserved before first plugin callback; native/loader/runtime attribution unavailable' }
                elseif ($from.name -eq 'loader.application-early-start' -and $to.name -eq 'loader.pre-mods') { 'generator boundary elapsed time; includes work/waits between callbacks, not isolated CPU cost' }
                elseif ($from.name -eq 'loader.pre-support' -and $to.name -eq 'loader.application-start') { 'combined support setup, IL2CPP type readiness and Harmony initialization; internal/native attribution unresolved' }
                elseif ($from.name -eq 'game.first-update' -and $to.name.StartsWith('scene.')) { 'first-update to scene callback envelope; asynchronous/native loading and waiting unresolved' }
                else { 'observed callback interval; internal/native/wait attribution unresolved' }
            $intervals.Add([ordered]@{from=$from.name;to=$to.name;toDetail=$to.detail;elapsedMs=(1000.0*([double]$to.timestampQpc-[double]$from.timestampQpc)/[double]$input.stopwatchFrequency);label=$label})
        }
    }
    $observedSpans = @()
    if ($trace -and $trace.stopwatchFrequency -gt 0) {
        $observedSpans = @($trace.events | Where-Object { $_.kind -eq 'span' -and ($_.name -in @('profiler.install','menu.settings.first_use','experiment.settings.populate','experiment.cleanup.run') -or $_.name -eq 'game.SimpleMenu.StartLoad.lifetime') } | ForEach-Object {
            [ordered]@{name=$_.name;elapsedMs=(1000.0*[double]$_.durationQpc/[double]$trace.stopwatchFrequency);detail=$_.detail;label=$(if($_.name -eq 'game.SimpleMenu.StartLoad.lifetime'){'coroutine lifetime includes yielded/native waits; executionMilliseconds is separate'}else{'inclusive observed span; may overlap lifecycle envelopes'})}
        })
    }
    $rows.Add([pscustomobject][ordered]@{
        runId=$(if($input){$input.runId}elseif($meta){$meta.runId}else{Split-Path $directory -Leaf})
        experiment=$(if($input){$input.experiment}elseif($meta){$meta.experiment}else{'unknown'})
        mode=$(if($input){$input.mode}elseif($meta -and $meta.mode){$meta.mode}else{'unknown'});loadout=$(if($input){$input.loadout}else{'unknown'})
        captureClass=$(if($input.captureClass){$input.captureClass}else{'seeded-warm-cache'})
        pairId=$(if($meta){$meta.pairId}else{''});candidate=$(if($meta){$meta.candidate}else{''})
        condition=$(if($meta -and $meta.condition){$meta.condition}elseif($input){$input.experiment}else{'unknown'});variantRole=$(if($meta){$meta.variantRole}else{''})
        valid=$valid;failure=($reasons -join '; ');menuReadyMs=$elapsed;firstPluginMs=$first;evidenceDirectory=$directory
        speedupEligible=($valid -and $speedupEligible);treatmentAssessment=$treatmentAssessment
        display=$display;profileSignature=$profileSignature;binarySignature=$binarySignature;generatorSignature=$generatorSignature;displaySignature=$displaySignature
        lifecycleIntervals=$intervals
        observedSpans=$observedSpans;profilerInstallMs=$(if(@($observedSpans|Where-Object name -eq 'profiler.install').Count){($observedSpans|Where-Object name -eq 'profiler.install'|ForEach-Object {$_.elapsedMs}|Measure-Object -Sum).Sum}else{$null});readinessObservationMs=$result.readiness.readinessObservationMilliseconds;readinessObservationCount=$result.readiness.readinessObservationCount
        promptMs=$result.readiness.excludedPromptMilliseconds;stockWarningMs=$result.readiness.stockWarningMilliseconds
        droppedEvents=$result.droppedEvents;visualReviewPresent=($null -ne $visual)
        inputManifest=$input.inputs;profileManifest=$input.profileInputs;conditionInputsStable=$true
        gameSignature=$(if($input){@($input.inputs|Where-Object { ($_.path -notmatch '^(Mods|Plugins|UserLibs)/') -or $_.path -eq 'Plugins/Anomaly.LaunchProfiler.dll' })|ConvertTo-Json -Depth 5 -Compress}else{''})
    })
}
if ($plan.matrixType -in @('loadout','instrumentation')) {
    foreach ($conditionGroup in @($rows|Group-Object condition,mode)) {
        $reference=$conditionGroup.Group|Where-Object valid|Select-Object -First 1
        foreach($row in $conditionGroup.Group) {
            $row.conditionInputsStable=[bool]($reference -and $row.binarySignature -ceq $reference.binarySignature -and $row.profileSignature -ceq $reference.profileSignature)
        }
    }
}
$groups = @($rows | Group-Object condition,mode,loadout,captureClass | ForEach-Object {
    $validRows = @($_.Group | Where-Object valid)
    [ordered]@{condition=$_.Group[0].condition;experiment=$_.Group[0].experiment;mode=$_.Group[0].mode;loadout=$_.Group[0].loadout;captureClass=$_.Group[0].captureClass;total=$_.Count;failed=($_.Count-$validRows.Count);menuReady=(Get-Stats @($validRows | ForEach-Object menuReadyMs));profilerInstall=(Get-Stats @($validRows|Where-Object {$null -ne $_.profilerInstallMs}|ForEach-Object profilerInstallMs));readinessObservation=(Get-Stats @($validRows|Where-Object {$null -ne $_.readinessObservationMs}|ForEach-Object readinessObservationMs))}
})
$comparisons = [Collections.Generic.List[object]]::new()
if ($plan) {
    foreach ($candidate in @($plan.runs.candidate | Select-Object -Unique)) {
        $pairs = [Collections.Generic.List[object]]::new()
        foreach ($pair in @($rows | Where-Object candidate -eq $candidate | Group-Object pairId)) {
            $baseline = if($plan.matrixType -in @('loadout','instrumentation')){$pair.Group|Where-Object variantRole -eq 'baseline'|Select-Object -First 1}else{$pair.Group | Where-Object experiment -eq 'baseline' | Select-Object -First 1}
            $variant = if($plan.matrixType -in @('loadout','instrumentation')){$pair.Group|Where-Object variantRole -eq 'candidate'|Select-Object -First 1}else{$pair.Group | Where-Object experiment -eq $candidate | Select-Object -First 1}
            $valid = $baseline -and $variant -and $baseline.valid -and $variant.valid
            $sameInputs = $valid -and $baseline.binarySignature -ceq $variant.binarySignature -and $baseline.profileSignature -ceq $variant.profileSignature
            if($plan.matrixType -eq 'loadout') {
                $sameInputs=$valid -and $baseline.conditionInputsStable -and $variant.conditionInputsStable -and $baseline.gameSignature -ceq $variant.gameSignature
            }
            if($plan.matrixType -eq 'instrumentation') {
                $sameInputs=$sameInputs -and $baseline.conditionInputsStable -and $variant.conditionInputsStable -and $baseline.mode -eq 'milestones' -and $variant.mode -eq 'detailed' -and $baseline.experiment -eq 'baseline' -and $variant.experiment -eq 'baseline'
            } elseif($baseline.mode -ne $variant.mode) { $sameInputs=$false }
            if (!$baseline.displaySignature -or $baseline.displaySignature -cne $variant.displaySignature) { $sameInputs=$false }
            if ($baseline.captureClass -ne $variant.captureClass) { $sameInputs=$false }
            if ($candidate -eq 'offline' -and $baseline.generatorSignature -cne $variant.generatorSignature) { $sameInputs=$false }
            $eligible = $valid -and $sameInputs -and $variant.speedupEligible
            $pairs.Add([ordered]@{pairId=$pair.Name;valid=([bool]($valid -and $sameInputs));inputsMatched=[bool]$sameInputs;speedupEligible=[bool]$eligible;treatmentAssessment=$variant.treatmentAssessment;baselineMs=$baseline.menuReadyMs;candidateMs=$variant.menuReadyMs;savingMs=$(if($eligible){$baseline.menuReadyMs-$variant.menuReadyMs}else{$null});additionalInstrumentationMs=$(if($plan.matrixType -eq 'instrumentation' -and $eligible){$variant.menuReadyMs-$baseline.menuReadyMs}else{$null})})
        }
        $validPairs = @($pairs | Where-Object valid)
        $baselineStats = Get-Stats @($validPairs | ForEach-Object baselineMs)
        $candidateStats = Get-Stats @($validPairs | ForEach-Object candidateMs)
        $eligiblePairs = @($validPairs | Where-Object speedupEligible)
        $savings = Get-Stats @($eligiblePairs | ForEach-Object savingMs)
        $noiseThreshold = if ($baselineStats) { [Math]::Max(50.0,3.0*$baselineStats.medianAbsoluteDeviationMs) } else { $null }
        $label = 'incomplete or inconclusive'
        if ($validPairs.Count -ge 5 -and $savings -and $savings.medianMs -gt $noiseThreshold -and $savings.minMs -gt 0) {
            $label=if($candidateStats.maxMs -lt $baselineStats.minMs){'consistent local improvement with separated observed ranges; qualify behavior before promotion'}else{'positive paired signal but overlapping ranges; inconclusive'}
        }
        elseif ($validPairs.Count -ge 5 -and $savings -and $savings.medianMs -lt -$noiseThreshold -and $savings.maxMs -lt 0) {
            $label=if($candidateStats.minMs -gt $baselineStats.maxMs){'consistent local regression with separated observed ranges'}else{'negative paired signal but overlapping ranges; inconclusive'}
        }
        if($candidate -eq 'deferred-settings' -and $validPairs.Count -ge 5 -and !@($validPairs|Where-Object {$_.speedupEligible -or $_.treatmentAssessment -ne 'settings work was not on the measured launch critical path'}).Count) {
            $label='not on launch critical path; first-use only'
        }
        if($plan.matrixType -eq 'instrumentation') {
            $label=$label.Replace('consistent local regression with separated observed ranges','consistent additional detailed-mode overhead with separated observed ranges').Replace('consistent local improvement with separated observed ranges; qualify behavior before promotion','detailed mode was unexpectedly faster with separated observed ranges; investigate ordering/noise, no optimization claim')
        }
        $comparisons.Add([ordered]@{candidate=$candidate;validPairs=$validPairs.Count;observedMatchedPairs=$validPairs.Count;speedupEligiblePairs=$eligiblePairs.Count;totalPairs=$pairs.Count;baseline=$baselineStats;candidateTimings=$candidateStats;pairedSavings=$savings;additionalInstrumentation=$(if($plan.matrixType -eq 'instrumentation'){Get-Stats @($eligiblePairs|ForEach-Object additionalInstrumentationMs)}else{$null});noiseThresholdMs=$noiseThreshold;label=$label;pairs=$pairs})
    }
}
$conditionInventories=@($rows|Group-Object condition,mode|ForEach-Object {
    $reference=$_.Group|Where-Object valid|Select-Object -First 1
    if($reference){[ordered]@{condition=$reference.condition;mode=$reference.mode;modFiles=@($reference.inputManifest|Where-Object path -Match '^(Mods|Plugins|UserLibs)/');profileFiles=$reference.profileManifest}}
})
$publicRows = @($rows | Select-Object * -ExcludeProperty profileSignature,binarySignature,generatorSignature,displaySignature,gameSignature,inputManifest,profileManifest)
$report = [ordered]@{
    schemaVersion=1;matrixType=$(if($plan.matrixType){$plan.matrixType}else{'experiments'});createdUtc=[DateTimeOffset]::UtcNow.ToString('O');measurement='QPC before Process.Start to explicit menu.ready; elapsed wall time'
    cacheLabel='Seeded interop except explicitly separate forced-generation captures; prelaunch provenance hashing warms the filesystem cache. No OS-cold claim.'
    caveats=@('Failures remain in raw rows and counts; they are never treated as zero milliseconds.','Inclusive spans and overlapping threads must not be added as total startup cost.','Milestones, detailed instrumentation and forced-generation captures are separate populations.','Improvement requires >=5 valid matched pairs, all improve, median saving exceeds max(50ms,3x baseline MAD), and separated observed ranges; heuristic, not confidence interval.')
    comparisonPolicy=$(if($plan.matrixType -in @('loadout','instrumentation')){$plan.comparisonPolicy}else{'Same private profile and binary inputs except explicitly proven experiment treatment.'})
    plannedRuns=$(if($plan){@($plan.runs).Count}else{$rows.Count});unattemptedRuns=$unattempted
    groups=$groups;comparisons=$comparisons;conditionInventories=$conditionInventories;runs=$publicRows
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence 'report.json')
$publicRows | Select-Object * -ExcludeProperty lifecycleIntervals,observedSpans,display | Export-Csv -LiteralPath (Join-Path $evidence 'runs.csv') -NoTypeInformation
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Launch profiling results')
$lines.Add('')
$lines.Add('Process.Start to explicit menu readiness. Seeded interop, OS cache warmed by provenance hashing. Failed runs remain visible.')
$lines.Add('')
$lines.Add('| Condition | Mode | Loadout / capture | Valid/total | Median ms | Range ms |')
$lines.Add('|---|---|---|---:|---:|---:|')
foreach ($group in $groups) {
    $stats=$group.menuReady
    $median=if($stats){'{0:N1}' -f $stats.medianMs}else{'n/a'}
    $range=if($stats){'{0:N1}–{1:N1}' -f $stats.minMs,$stats.maxMs}else{'n/a'}
    $lines.Add("| $($group.condition) | $($group.mode) | $($group.loadout) / $($group.captureClass) | $($group.total-$group.failed)/$($group.total) | $median | $range |")
}
foreach ($comparison in $comparisons) {
    $lines.Add('')
    if($plan.matrixType -eq 'instrumentation') {
        $overhead=if($comparison.additionalInstrumentation){'{0:N1} ms' -f $comparison.additionalInstrumentation.medianMs}else{'n/a'}
        $lines.Add("$($comparison.candidate): $($comparison.validPairs)/$($comparison.totalPairs) matched pairs; median additional detailed-mode elapsed time $overhead. $($comparison.label).")
    } else {
        $saving=if($comparison.pairedSavings){'{0:N1} ms' -f $comparison.pairedSavings.medianMs}else{'n/a'}
        $lines.Add("$($comparison.candidate): $($comparison.validPairs)/$($comparison.totalPairs) matched pairs; median paired saving $saving. $($comparison.label).")
    }
}
$lines.Add('')
$lines.Add('| Condition / mode | Median profiler install ms | Median readiness observation self-time ms |')
$lines.Add('|---|---:|---:|')
foreach($group in $groups) {
    $install=if($group.profilerInstall){'{0:N2}' -f $group.profilerInstall.medianMs}else{'unobserved'}
    $observation=if($group.readinessObservation){'{0:N2}' -f $group.readinessObservation.medianMs}else{'unobserved'}
    $lines.Add("| $($group.condition) / $($group.mode) | $install | $observation |")
}
$lines.Add('')
$lines.Add('The local improvement threshold is max(50 ms, 3 × baseline median absolute deviation), with at least five matched pairs, a positive saving in every pair, and candidate maximum below baseline minimum. Overlapping observed ranges remain inconclusive. This is a screening heuristic, not statistical confidence or a gameplay qualification. Profiler installation and readiness-observation overhead are retained per run; detailed captures are a separate population.')
$lines.Add('')
$lines.Add($report.comparisonPolicy)
$lines.Add('')
$lines.Add('Every raw run, actual display configuration and lifecycle boundary interval is retained in report.json; runs.csv contains flat timing/failure rows. Time before the first plugin callback is unobserved. Other callback intervals are elapsed envelopes with unresolved native work and waiting, not isolated method costs. Inclusive detailed spans must not be summed.')
$lines | Set-Content -LiteralPath (Join-Path $evidence 'report.md')
Write-Output "Report: $(Join-Path $evidence 'report.md')"
