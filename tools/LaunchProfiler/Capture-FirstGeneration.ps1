[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [ValidateSet('milestones','detailed')] [string] $Mode='milestones',
    [ValidateRange(60,1800)] [int] $TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$game=[IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
$markerPath=Join-Path $game 'launch-profiler-staging.json'
if(!(Test-Path -LiteralPath $markerPath)){throw 'Only a marked isolated profiling game may be used.'}
$marker=Get-Content -Raw -LiteralPath $markerPath|ConvertFrom-Json
if(![string]::Equals($marker.destination,$game,[StringComparison]::OrdinalIgnoreCase)){throw 'Stage marker destination mismatch.'}
$pendingPath=Join-Path $game 'launch-profiler-generation-pending.json'
if(Test-Path -LiteralPath $pendingPath){throw 'An earlier generation capture has pending restoration; inspect its recovery record.'}
if(Test-Path -LiteralPath $evidence){throw 'Use a new first-generation evidence directory.'}
$loaderConfig=Join-Path $game 'UserData/Loader.cfg'
if(!(Test-Path -LiteralPath $loaderConfig) -or !(Select-String -LiteralPath $loaderConfig -SimpleMatch '--melonloader.agfregenerate' -Quiet)){throw 'The seeded loader configuration must document the supported --melonloader.agfregenerate option.'}
foreach($process in Get-Process -Name SCPSL -ErrorAction SilentlyContinue){if([string]::Equals($process.Path,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)){throw 'This stage is already running.'}}
New-Item -ItemType Directory -Path $evidence|Out-Null
$backup=Join-Path $evidence 'private-generator-backup'
New-Item -ItemType Directory -Path $backup|Out-Null
$roots=@('MelonLoader/Il2CppAssemblies','MelonLoader/Dependencies/Il2CppAssemblyGenerator','UserData/Loader.cfg')
$manifest=@()
foreach($relative in $roots){
    $source=[IO.Path]::GetFullPath((Join-Path $game $relative))
    if(!$source.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Backup source escaped stage.'}
    if(!(Test-Path -LiteralPath $source)){throw "Missing generator input: $relative"}
    if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Generator inputs must be private physical files/directories.'}
    $target=Join-Path $backup $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target)|Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Recurse
    $files=if(Test-Path -LiteralPath $source -PathType Container){@(Get-ChildItem -LiteralPath $source -File -Recurse)}else{@(Get-Item -LiteralPath $source)}
    foreach($file in $files){$manifest += [ordered]@{path=([IO.Path]::GetRelativePath($game,$file.FullName)).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}
}
$state=[ordered]@{schemaVersion=1;game=$game;backupDirectory=$backup;roots=$roots;manifest=$manifest;captureClass='forced-assembly-generation-with-warm-tool-packages';note='Existing interop remains available until the supported generator performs its own cleanup. This does not measure first download or OS-cold launch.'}
$state|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $pendingPath
$state|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $evidence 'generation-inputs.json')
$run=Join-Path $evidence 'capture'
try {
    & (Join-Path $PSScriptRoot 'Run-Profile.ps1') -GameDirectory $game -EvidenceDirectory $run -Mode $Mode -Experiment baseline -ForceGeneration -TimeoutSeconds $TimeoutSeconds
} finally {
    # The PID receipt can be absent if the runner faults just after Process.Start.
    # Check the exact private image path as well before replacing any loader files.
    foreach($active in Get-Process -Name SCPSL,Cpp2IL -ErrorAction SilentlyContinue){
        $image=$active.Path
        if($image -and ([string]::Equals($image,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase) -or $image.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'A private game/generator process remains alive; cache restoration is pending.'}
    }
    $ownedPidPath=Join-Path $run 'client.pid'
    if(Test-Path -LiteralPath $ownedPidPath){
        $ownedId=[int](Get-Content -Raw -LiteralPath $ownedPidPath)
        $owned=Get-Process -Id $ownedId -ErrorAction SilentlyContinue
        if($owned -and [string]::Equals($owned.Path,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)){throw 'Owned process remains alive; generator cache restoration is pending and was not attempted.'}
    }
    foreach($relative in $roots){
        $target=[IO.Path]::GetFullPath((Join-Path $game $relative))
        if(!$target.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Restore target escaped stage.'}
        if(Test-Path -LiteralPath $target){
            $after=Join-Path (Join-Path $evidence 'generated-after-run') $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $after)|Out-Null
            Move-Item -LiteralPath $target -Destination $after
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target)|Out-Null
        Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination $target -Recurse
    }
    foreach($entry in $manifest){if((Get-FileHash -LiteralPath (Join-Path $game $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256){throw "Generator cache restoration hash mismatch: $($entry.path)"}}
    [ordered]@{schemaVersion=1;restored=$true;fileCount=$manifest.Count;completedUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'restoration.json')
    Remove-Item -LiteralPath $pendingPath
}
& (Join-Path $PSScriptRoot 'New-Report.ps1') -EvidenceDirectory $run
