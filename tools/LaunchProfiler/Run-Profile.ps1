[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [ValidateSet('milestones','detailed')] [string] $Mode = 'milestones',
    [ValidateSet('baseline','offline','deferred-settings','deferred-cleanup','identity-once','unchanged-module-lock','no-skins')] [string] $Experiment = 'baseline',
    [string] $RunId = ([Guid]::NewGuid().ToString('N')),
    [ValidateRange(10,1800)] [int] $TimeoutSeconds = 120,
    [ValidateRange(1,60)] [int] $GracefulExitSeconds = 15,
    [switch] $NoStop,
    [switch] $SnapshotPrivateProfile,
    [string] $ProfileSnapshotDirectory = '',
    [string] $VariantManifest = '',
    [switch] $ForceGeneration
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
$marker = Join-Path $game 'launch-profiler-staging.json'
if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Only a game created by Prepare-Game.ps1 may be used.' }
$staging = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
if (![string]::Equals($staging.destination, $game, [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging destination does not match the requested game.' }
$treatmentMarker=Join-Path $game 'launch-profiler-treatment-pending.json'
if(Test-Path -LiteralPath $treatmentMarker){throw 'A prior profiling treatment has pending restoration. Inspect launch-profiler-treatment-pending.json before reusing this stage.'}
$generationMarker=Join-Path $game 'launch-profiler-generation-pending.json'
if($ForceGeneration) {
    if($Experiment -ne 'baseline' -or !(Test-Path -LiteralPath $generationMarker)){throw 'Forced generation requires Capture-FirstGeneration.ps1 backup marker and baseline experiment.'}
    $generationInfo=Get-Content -Raw -LiteralPath $generationMarker|ConvertFrom-Json
    if(![string]::Equals($generationInfo.game,$game,[StringComparison]::OrdinalIgnoreCase)){throw 'Generation backup marker belongs to another stage.'}
} elseif(Test-Path -LiteralPath $generationMarker) {throw 'A first-generation capture has pending cache restoration; complete recovery before another run.'}
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory for every launch.' }
if (!(Test-Path -LiteralPath (Join-Path $game 'Plugins/Anomaly.LaunchProfiler.dll') -PathType Leaf)) { throw 'The profiler plugin is missing.' }
foreach ($existing in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
    try { if ([string]::Equals($existing.Path, (Join-Path $game 'SCPSL.exe'), [StringComparison]::OrdinalIgnoreCase)) { throw "This isolated game is already running: PID $($existing.Id)" } }
    catch [System.ComponentModel.Win32Exception] { throw 'Cannot inspect an existing SCP:SL process.' }
}
New-Item -ItemType Directory -Path $evidence | Out-Null
$process=$null
$processStarted=$false
$originalClient=$null
$originalTrust=$null
$parkedSkins=$null
$skinDirectory=Join-Path $game 'UserData/Skins'
$stdoutStream=$null
$stderrStream=$null
$stdoutTask=$null
$stderrTask=$null
try {
if ($ProfileSnapshotDirectory) {
    $snapshot = [IO.Path]::GetFullPath($ProfileSnapshotDirectory)
    $snapshotMarker = Join-Path $snapshot 'profile-snapshot.json'
    if (!(Test-Path -LiteralPath $snapshotMarker -PathType Leaf)) { throw 'Profile snapshots require profile-snapshot.json.' }
    $profileInfo = Get-Content -Raw -LiteralPath $snapshotMarker | ConvertFrom-Json
    if (![string]::Equals($profileInfo.game, $game, [StringComparison]::OrdinalIgnoreCase)) { throw 'Snapshot belongs to another isolated game.' }
    foreach ($folder in @('UserData','ProfileAppData')) {
        $target = [IO.Path]::GetFullPath((Join-Path $game $folder))
        if (!$target.StartsWith($game + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile target escapes the isolated game.' }
        if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination (Join-Path $evidence ('previous-' + $folder)) }
        New-Item -ItemType Directory -Path $target | Out-Null
        $from = Join-Path $snapshot $folder
        if (Test-Path -LiteralPath $from) { Get-ChildItem -LiteralPath $from -Force | Copy-Item -Destination $target -Recurse }
    }
}
function Get-TreeManifest([string[]] $Roots) {
    @($Roots | ForEach-Object {
        $root = Join-Path $game $_
        if (Test-Path -LiteralPath $root -PathType Leaf) { Get-Item -LiteralPath $root }
        elseif (Test-Path -LiteralPath $root -PathType Container) { Get-ChildItem -LiteralPath $root -File -Recurse }
    } | Sort-Object FullName -Unique | ForEach-Object {
        [ordered]@{ path=([IO.Path]::GetRelativePath($game,$_.FullName)).Replace('\','/'); size=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
$variantProof = $null
$baselineProfileInputs = $null
$originalClient = $null
$clientPath = Join-Path $game 'Mods/Anomaly.Client.dll'
if ($Experiment -in @('identity-once','unchanged-module-lock')) {
    if (!$VariantManifest -or !(Test-Path -LiteralPath $VariantManifest -PathType Leaf)) { throw 'This experiment requires its built and tested private VariantManifest.' }
    $variant = Get-Content -Raw -LiteralPath $VariantManifest | ConvertFrom-Json
    if ($variant.variant -ne $Experiment -or !$variant.buildSucceeded -or !$variant.testsSucceeded) { throw 'Variant manifest does not prove the requested built and tested experiment.' }
    $variantHash = (Get-FileHash -LiteralPath $variant.clientDll -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($variantHash -ne $variant.clientSha256) { throw 'Private variant DLL does not match its manifest.' }
    $baseFile = Get-Item -LiteralPath $clientPath
    $baseHash = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($baseHash -eq $variantHash) { throw 'The baseline client already equals the candidate; prepare a baseline stage.' }
    $originalClient = Join-Path $evidence 'private-original-client.dll'
    $baselineProfileInputs = Get-TreeManifest @('UserData','ProfileAppData')
    Copy-Item -LiteralPath $clientPath -Destination $originalClient
    $trustPath = Join-Path $game 'UserData/Anomaly/mod-trust.json'
    $originalTrust = Join-Path $evidence 'private-original-trust.json'
    Copy-Item -LiteralPath $trustPath -Destination $originalTrust
    [ordered]@{schemaVersion=1;game=$game;runId=$RunId;experiment=$Experiment;evidence=$evidence;originalClient=$originalClient;originalTrust=$originalTrust}|ConvertTo-Json|Set-Content -LiteralPath $treatmentMarker
    Copy-Item -LiteralPath $variant.clientDll -Destination $clientPath
    $variantProof = [ordered]@{manifest=[IO.Path]::GetFullPath($VariantManifest);variant=$Experiment;buildSucceeded=$true;testsSucceeded=$true;clientSha256=$variantHash;baselineClientSha256=$baseHash;baselineClientSize=$baseFile.Length}
    Copy-Item -LiteralPath $VariantManifest -Destination (Join-Path $evidence 'variant.json')
    # The explicit private test policy must approve the candidate just as it approved baseline.
    $trust = Get-Content -Raw -LiteralPath $trustPath | ConvertFrom-Json
    $trust.approvedSha256 = @($trust.approvedSha256 | Where-Object { $_ -ne $baseHash }) + @($variantHash)
    $trust | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $trustPath
}
$hashStarted = [Diagnostics.Stopwatch]::GetTimestamp()
$inputs = Get-TreeManifest @('SCPSL.exe','GameAssembly.dll','UnityPlayer.dll','version.dll','SCPSL_Data/il2cpp_data/Metadata/global-metadata.dat','Mods','Plugins','UserLibs','MelonLoader/net6','MelonLoader/Il2CppAssemblies','MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg')
$profileInputs = Get-TreeManifest @('UserData','ProfileAppData')
$hashFinished = [Diagnostics.Stopwatch]::GetTimestamp()
$treatment = [ordered]@{kind=$Experiment;applied=($Experiment -eq 'baseline' -or $null -ne $variantProof);detail=''}
$parkedSkins = $null
$skinDirectory = Join-Path $game 'UserData/Skins'
if ($Experiment -eq 'no-skins') {
    $baselineProfileInputs = $profileInputs
    $skinFiles = @($profileInputs | Where-Object { $_.path.StartsWith('UserData/Skins/',[StringComparison]::OrdinalIgnoreCase) })
    if ($skinFiles.Count -gt 0) {
        $parkedSkins = Join-Path $evidence 'private-parked-skins'
        [ordered]@{schemaVersion=1;game=$game;runId=$RunId;experiment=$Experiment;evidence=$evidence;parkedSkins=$parkedSkins}|ConvertTo-Json|Set-Content -LiteralPath $treatmentMarker
        Move-Item -LiteralPath $skinDirectory -Destination $parkedSkins
        $treatment.applied = $true
        $treatment.detail = "Parked $($skinFiles.Count) private skin file(s) after restoring the common profile snapshot."
    } else { $treatment.detail = 'No skin files were present; this treatment has no effect.' }
}
if ($SnapshotPrivateProfile) {
    $private = Join-Path $evidence 'private-profile'
    New-Item -ItemType Directory -Path $private | Out-Null
    foreach ($folder in @('UserData','ProfileAppData')) { Copy-Item -LiteralPath (Join-Path $game $folder) -Destination $private -Recurse }
    [ordered]@{schemaVersion=1;game=$game;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');containsPrivateIdentity=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $private 'profile-snapshot.json')
}
$gitDir = Join-Path $repository '.git'
$sourceCommit = (& git --git-dir=$gitDir --work-tree=$repository rev-parse HEAD 2>$null | Select-Object -First 1)
$commitAvailable=$LASTEXITCODE -eq 0
$sourceStatus = @(& git --git-dir=$gitDir --work-tree=$repository status --porcelain=v1 --untracked-files=all 2>$null)
$statusAvailable=$LASTEXITCODE -eq 0
$startInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $game 'SCPSL.exe'))
$startInfo.WorkingDirectory = $game
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.RedirectStandardOutput = !$NoStop
$startInfo.RedirectStandardError = !$NoStop
foreach ($key in @($startInfo.Environment.Keys)) {
    if ($key.StartsWith('ANOMALY_') -and ($key.Contains('SMOKE') -or $key.StartsWith('ANOMALY_LAUNCH_PROFILE_'))) { $startInfo.Environment.Remove($key) | Out-Null }
}
$startInfo.Environment['ANOMALY_LAUNCH_PROFILE_DIR'] = $evidence
$startInfo.Environment['ANOMALY_LAUNCH_PROFILE_MODE'] = $Mode
$startInfo.Environment['ANOMALY_LAUNCH_PROFILE_EXPERIMENT'] = $Experiment
$startInfo.Environment['ANOMALY_LAUNCH_PROFILE_RUN_ID'] = $RunId
$arguments = @('-noauth','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $evidence 'player.log'),'-appdatapath',(Join-Path $game 'ProfileAppData'),'--anomaly-profile-fill-nickname','--anomaly-profile-acknowledge-warning','--anomaly-profile-quit')
if ($Experiment -eq 'offline') { $arguments += '--melonloader.agfoffline' }
if ($ForceGeneration) { $arguments += '--melonloader.agfregenerate' }
foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
$record = [ordered]@{
    schemaVersion=1;runId=$RunId;game=$game;loadout=$staging.loadout;mode=$Mode;experiment=$Experiment
    sourceCommit=$sourceCommit;sourceWorktreeClean=$(if($statusAvailable){$sourceStatus.Count -eq 0}else{$null});sourceStatus=$sourceStatus;sourceGitAvailable=($commitAvailable -and $statusAvailable)
    preparedUtc=[DateTimeOffset]::UtcNow.ToString('O');stopwatchFrequency=[Diagnostics.Stopwatch]::Frequency
    hashStartedQpc=$hashStarted;hashFinishedQpc=$hashFinished;inputs=$inputs;profileInputs=$profileInputs
    cacheState=$(if($ForceGeneration){'forced assembly generation; generator packages retained; OS file cache warmed by provenance hashing'}else{'existing seeded interop; OS file cache warmed by provenance hashing'});arguments=$arguments
    captureClass=$(if($ForceGeneration){'forced-assembly-generation-with-warm-tool-packages'}else{'seeded-warm-cache'})
    treatment=$treatment;effectiveProfileInputs=$(if($Experiment -eq 'no-skins'){@($profileInputs | Where-Object { !$_.path.StartsWith('UserData/Skins/',[StringComparison]::OrdinalIgnoreCase) })}else{$profileInputs})
    variant=$variantProof
    baselineProfileInputs=$baselineProfileInputs
    launchQpc=$null;startAfterQpc=$null;processId=$null
}
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$record.launchQpc = [Diagnostics.Stopwatch]::GetTimestamp()
if (!$process.Start()) { throw 'The isolated SCP:SL process could not be started.' }
$processStarted=$true
$record.startAfterQpc = [Diagnostics.Stopwatch]::GetTimestamp()
if(!$NoStop) {
    $stdoutStream=[IO.File]::Create((Join-Path $evidence 'console.stdout.log'))
    $stderrStream=[IO.File]::Create((Join-Path $evidence 'console.stderr.log'))
    $stdoutTask=$process.StandardOutput.BaseStream.CopyToAsync($stdoutStream)
    $stderrTask=$process.StandardError.BaseStream.CopyToAsync($stderrStream)
}
$record.processId = $process.Id
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
$process.Id | Set-Content -LiteralPath (Join-Path $evidence 'client.pid')
Write-Output "Started isolated profiling client PID $($process.Id); evidence: $evidence"
$resultPath = Join-Path $evidence 'result.json'
$watch = [Diagnostics.Stopwatch]::StartNew()
$timedOut = $false
$stopMethod = 'none'
while (!$process.HasExited -and !(Test-Path -LiteralPath $resultPath)) {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $timedOut = $true; break }
    Start-Sleep -Milliseconds 200
}
if (!$process.HasExited -and !$NoStop) {
    # Give the plugin time to finish its screenshot and normal Application.Quit request.
    if (!$process.WaitForExit($GracefulExitSeconds * 1000)) {
        $stopMethod = 'CloseMainWindow'
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit($GracefulExitSeconds * 1000)) {
            $stopMethod = 'owned-child-kill'
            $process.Kill($true)
            $process.WaitForExit()
        }
    } else { $stopMethod = 'plugin-graceful-exit' }
}
$latest = Join-Path $game 'MelonLoader/Latest.log'
if (Test-Path -LiteralPath $latest) { Copy-Item -LiteralPath $latest -Destination (Join-Path $evidence 'melonloader.log') }
$generator = [ordered]@{remoteContactObserved=$false;upToDateObserved=$false;generationObserved=$false;cpp2il='';interop='';unityDependencies='';regex=''}
if (Test-Path -LiteralPath $latest) {
    foreach ($line in Get-Content -LiteralPath $latest) {
        if ($line.Contains('Contacting RemoteAPI...')) { $generator.remoteContactObserved=$true }
        if ($line.Contains('Assembly is up to date. No Generation Needed.')) { $generator.upToDateObserved=$true }
        if ($line.Contains('Assembly Generation Needed!')) { $generator.generationObserved=$true }
        if ($line -match 'Using Cpp2IL Version:\s*(.+)$') { $generator.cpp2il=$Matches[1].Trim() }
        if ($line -match 'Using Il2CppInterop Version\s*=\s*(.+)$') { $generator.interop=$Matches[1].Trim() }
        if ($line -match 'Using Unity Dependencies Version\s*=\s*(.+)$') { $generator.unityDependencies=$Matches[1].Trim() }
        if ($line -match 'Using Deobfuscation Regex\s*=\s*(.*)$') { $generator.regex=$Matches[1].Trim() }
    }
}
if ($Experiment -eq 'offline') {
    $treatment.applied = !$generator.remoteContactObserved -and $generator.upToDateObserved -and !$generator.generationObserved -and ![string]::IsNullOrWhiteSpace($generator.cpp2il)
    $treatment.detail = 'Offline CLI switch supplied; verification requires warm generator completion without remote contact and unchanged generator versions across the pair.'
}
[ordered]@{
    schemaVersion=1;runId=$RunId;processId=$process.Id;timedOut=$timedOut;resultPresent=(Test-Path -LiteralPath $resultPath)
    processExited=$process.HasExited;exitCode=$(if($process.HasExited){$process.ExitCode}else{$null});stopMethod=$stopMethod
    completedUtc=[DateTimeOffset]::UtcNow.ToString('O');elapsedRunnerSeconds=$watch.Elapsed.TotalSeconds
    treatment=$treatment;generator=$generator;skinRestorationPending=([bool]($parkedSkins -and !$process.HasExited))
    variantRestorationPending=([bool]($originalClient -and !$process.HasExited))
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'runner-result.json')
if ($timedOut) { Write-Warning 'Profiling timed out; preserve the failure when comparing runs.' }
elseif (!(Test-Path -LiteralPath $resultPath)) { Write-Warning 'The process exited without a profiler result.' }
} catch {
    try {[ordered]@{schemaVersion=1;runId=$RunId;failure=$_.Exception.Message;exceptionType=$_.Exception.GetType().FullName}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'runner-exception.json')} catch {}
    throw
} finally {
    # Run even when hashing, provenance writes, log copying or process control fails.
    # Never replace a treatment while its exact owned child is still using the stage.
    if($processStarted -and !$process.HasExited -and !$NoStop) {
        $null=$process.CloseMainWindow()
        if(!$process.WaitForExit($GracefulExitSeconds*1000)){$process.Kill($true);$process.WaitForExit()}
    }
    $canRestore=!$processStarted -or $process.HasExited
    if($canRestore) {
        if($parkedSkins -and (Test-Path -LiteralPath $parkedSkins)) {
            if(Test-Path -LiteralPath $skinDirectory){Move-Item -LiteralPath $skinDirectory -Destination (Join-Path $evidence 'private-generated-skins')}
            Move-Item -LiteralPath $parkedSkins -Destination $skinDirectory
        }
        if($originalClient -and (Test-Path -LiteralPath $originalClient)) {
            Copy-Item -LiteralPath $originalClient -Destination $clientPath
            if((Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $originalClient -Algorithm SHA256).Hash){throw 'Private baseline client restoration failed verification.'}
        }
        if($originalTrust -and (Test-Path -LiteralPath $originalTrust)) {Copy-Item -LiteralPath $originalTrust -Destination (Join-Path $game 'UserData/Anomaly/mod-trust.json')}
        if(Test-Path -LiteralPath $treatmentMarker){Remove-Item -LiteralPath $treatmentMarker}
    }
    [ordered]@{schemaVersion=1;runId=$RunId;restored=$canRestore;processStarted=$processStarted;reason=$(if($canRestore){'owned process exited or never started'}else{'NoStop left the owned process alive; treatment restoration pending'})}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'treatment-restoration.json')
    foreach($task in @($stdoutTask,$stderrTask)){if($task){try{$null=$task.Wait(2000)}catch{Write-Warning 'A console capture stream ended with an error; inspect the other evidence logs.'}}}
    if($stdoutStream){$stdoutStream.Dispose()}
    if($stderrStream){$stderrStream.Dispose()}
    if($process -and (!$processStarted -or $process.HasExited)){$process.Dispose()}
}
