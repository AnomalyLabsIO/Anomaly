[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SourceGameRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [ValidateSet('Core','LoaderOnly','Dev')] [string] $Loadout = 'Core',
    [string] $ProfilerDll = (Join-Path $PSScriptRoot 'bin/Release/net6.0/Anomaly.LaunchProfiler.dll'),
    [string] $CorePackageDirectory = '',
    [string] $DevLocalDirectory = '',
    [ValidateSet('VR')] [string[]] $ExcludedModGroups = @()
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.Path]::GetFullPath($SourceGameRoot).TrimEnd('\','/')
$destination = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
if ($destination.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The profiling game must be separate from its source.'
}
if (Test-Path -LiteralPath $destination) { throw 'Use a new destination; existing games are never overwritten.' }
foreach ($required in @('SCPSL.exe','GameAssembly.dll','version.dll','MelonLoader/net6/MelonLoader.dll','MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "Missing seeded modded game input: $required" }
}
if (!(Test-Path -LiteralPath $ProfilerDll -PathType Leaf)) { throw "Build the profiler first: $ProfilerDll" }
if ($Loadout -eq 'Dev' -and !(Test-Path -LiteralPath $DevLocalDirectory -PathType Container)) { throw 'Dev requires an explicit local loadout directory.' }
if ($ExcludedModGroups.Count -and $Loadout -ne 'Dev') { throw 'ExcludedModGroups is supported only for a cloned Dev loadout.' }
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
$entries = @(($layout.packages | Where-Object packageId -eq 'anomaly-client').entries)
if ($Loadout -ne 'LoaderOnly') {
    foreach ($entry in $entries) {
        $inputFile = if ($CorePackageDirectory) { Join-Path $CorePackageDirectory $entry.path } else { Join-Path $repository $entry.source }
        if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing built core package input: $inputFile" }
    }
}
New-Item -ItemType Directory -Path $destination | Out-Null
$script:linked = 0
$script:copied = 0
function Copy-SeedDirectory([string] $From, [string] $Relative = '') {
    foreach ($item in Get-ChildItem -LiteralPath $From -Force) {
        $rel = if ($Relative) { Join-Path $Relative $item.Name } else { $item.Name }
        if (!$Relative -and $item.Name -in @('Mods','Plugins','UserLibs','UserData','AppData','LocalHostingSmoke')) { continue }
        if ($item.Name.StartsWith('.') -or $item.Name -eq 'Logs' -or $item.Name -eq 'Latest.log' -or $item.Name -match '(?i)\.(bak|old|good|disabled)(\.|$)') { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected reparse point in static seed: $rel" }
        $target = Join-Path $destination $rel
        if ($item.PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-SeedDirectory $item.FullName $rel
        } else {
            $first = $rel.Split([IO.Path]::DirectorySeparatorChar)[0]
            # Never modify static hardlinks. All loader, configuration and generated state is copied.
            if ($first -eq 'MelonLoader' -or $item.Extension -notin @('.dll','.exe','.assets','.resource','.ress','.bundle')) {
                Copy-Item -LiteralPath $item.FullName -Destination $target
                $script:copied++
            } else {
                try { New-Item -ItemType HardLink -Path $target -Target $item.FullName | Out-Null; $script:linked++ }
                catch { Copy-Item -LiteralPath $item.FullName -Destination $target; $script:copied++ }
            }
        }
    }
}
Copy-SeedDirectory $source
foreach ($folder in @('Mods','Plugins','UserLibs','UserData','ProfileAppData')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $destination $folder) | Out-Null
}
# Retain loader settings only. Player identities and game preference directories are fresh.
$loaderConfig = Join-Path $source 'UserData/Loader.cfg'
if (Test-Path -LiteralPath $loaderConfig -PathType Leaf) { Copy-Item -LiteralPath $loaderConfig -Destination (Join-Path $destination 'UserData/Loader.cfg') }
if ($Loadout -ne 'LoaderOnly') {
    foreach ($entry in $entries) {
        $inputFile = if ($CorePackageDirectory) { Join-Path $CorePackageDirectory $entry.path } else { Join-Path $repository $entry.source }
        $target = Join-Path $destination $entry.path
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $inputFile -Destination $target
    }
}
if ($Loadout -eq 'Dev') {
    # Clone executable mod groups and deliberate skin/i18n assets, never a real identity/profile.
    foreach ($folder in @('Mods','Plugins','UserLibs','UserData/Skins','UserData/i18n','UserData/Anomaly/Overrides','UserData/Anomaly.Portals')) {
        $devSource = Join-Path $DevLocalDirectory $folder
        if (!(Test-Path -LiteralPath $devSource -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $devSource -File -Recurse) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected dev file link: $($file.FullName)" }
            $target = Join-Path (Join-Path $destination $folder) ([IO.Path]::GetRelativePath($devSource, $file.FullName))
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    # Dev extras may include an older core closure. Reapply this run's exact package
    # so differences against Core describe extras, not unrelated Anomaly revisions.
    foreach ($entry in $entries) {
        $inputFile = if ($CorePackageDirectory) { Join-Path $CorePackageDirectory $entry.path } else { Join-Path $repository $entry.source }
        $target = Join-Path $destination $entry.path
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $inputFile -Destination $target
    }
}
$removedGroupFiles = @()
if ('VR' -in $ExcludedModGroups) {
    $vrNames = @('SL-VR.dll','UniverseLib.ML.IL2CPP.Interop.dll','openvr_api.dll','openxr_loader.dll','UnityGraphicsHelper.dll')
    $resolved = @()
    foreach ($name in $vrNames) {
        $matches = @(Get-ChildItem -LiteralPath (Join-Path $destination 'Mods'),(Join-Path $destination 'UserLibs'),(Join-Path $destination 'Plugins') -File -Recurse | Where-Object Name -eq $name)
        if ($matches.Count -ne 1) { throw "VR group must contain exactly one active $name; found $($matches.Count). No partial group removal is allowed." }
        $resolved += $matches[0]
    }
    foreach ($file in $resolved) {
        $target = [IO.Path]::GetFullPath($file.FullName)
        if (!$target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Excluded mod path escaped the private stage.' }
        $removedGroupFiles += [ordered]@{path=([IO.Path]::GetRelativePath($destination,$target)).Replace('\','/');sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()}
        Remove-Item -LiteralPath $target
    }
}
Copy-Item -LiteralPath $ProfilerDll -Destination (Join-Path $destination 'Plugins/Anomaly.LaunchProfiler.dll')
$anomalyData = Join-Path $destination 'UserData/Anomaly'
New-Item -ItemType Directory -Force -Path $anomalyData | Out-Null
$approved = @(Get-ChildItem -LiteralPath (Join-Path $destination 'Mods') -Filter '*.dll' -Recurse | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
@{ schemaVersion=1; mode='enforce'; approvedSha256=$approved } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $anomalyData 'mod-trust.json')
[ordered]@{
    schemaVersion=1; sourceGame=$source; destination=$destination; loadout=$Loadout
    staticHardLinks=$script:linked; physicalCopies=$script:copied; profile='fresh'
    createdUtc=[DateTimeOffset]::UtcNow.ToString('O'); purpose='isolated startup profiling; static hardlinks must never be patched'
    excludedModGroups=$ExcludedModGroups;excludedGroupFiles=$removedGroupFiles;corePackageAppliedAfterDevExtras=($Loadout -eq 'Dev')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'launch-profiler-staging.json')
Write-Output "Prepared isolated $Loadout profiling game: $destination ($script:linked static hardlinks, $script:copied copies)."
