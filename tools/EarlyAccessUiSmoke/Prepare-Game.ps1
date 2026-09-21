[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SourceGameRoot,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceGameRoot)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($destination.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $destination.Equals($source, [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke destination must be separate from the source game.' }
if (Test-Path -LiteralPath $destination) { throw 'Use a new empty destination; existing test games are never overwritten.' }
foreach ($required in @('SCPSL.exe','GameAssembly.dll','version.dll','MelonLoader/net6/MelonLoader.dll','MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing seeded modded game input: $required" }
}
New-Item -ItemType Directory -Path $destination | Out-Null
$linked = 0
$copied = 0
# Static game assets are shared read-only by convention. Every writable loader/profile
# location is a separate physical copy or a fresh directory; no existing identity is imported.
foreach ($file in (Get-ChildItem -LiteralPath $source -Recurse -File)) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $parts = $relative.Split([IO.Path]::DirectorySeparatorChar)
    if ($parts[0] -in @('Mods','Plugins','UserLibs','UserData') -or
        $relative -match '(?i)(^|[\\/])(Logs|Latest\.log)([\\/]|$)' -or
        $file.Name.StartsWith('.') -or $file.Name -match '(?i)\.(bak|old|good|disabled)') { continue }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected reparse-point file: $relative" }
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if ($parts[0] -eq 'MelonLoader' -or $file.Extension -notin @('.dll','.exe','.assets','.resource','.ress','.bundle','.resS')) {
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $copied++
    } else {
        New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
        $linked++
    }
}
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
$runtime = $layout.packages | Where-Object packageId -eq 'anomaly-client'
foreach ($entry in $runtime.entries) {
    $target = Join-Path $destination $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination $target
}
foreach ($mod in @('samples/HelloAnomalyMod/bin/Release/net6.0/HelloAnomalyMod.dll','tools/EarlyAccessUiSmoke/bin/Release/net6.0/Anomaly.EarlyAccessUiSmoke.dll')) {
    Copy-Item -LiteralPath (Join-Path $repository $mod) -Destination (Join-Path $destination 'Mods')
}
$anomalyData = Join-Path $destination 'UserData/Anomaly'
New-Item -ItemType Directory -Force -Path $anomalyData | Out-Null
$approved = @(Get-ChildItem -LiteralPath (Join-Path $destination 'Mods') -Filter '*.dll' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
@{ schemaVersion=1; mode='enforce'; approvedSha256=$approved } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $anomalyData 'mod-trust.json')
@{ sourceGame=$source; destination=$destination; staticHardLinks=$linked; physicalCopies=$copied; profile='fresh'; purpose='local UI validation only; not official delivery qualification' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'smoke-staging.json')
Write-Output "Prepared isolated smoke game: $destination ($linked static hard links, $copied copies)."
