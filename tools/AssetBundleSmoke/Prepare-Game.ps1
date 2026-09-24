[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SourceGameRoot,
    [Parameter(Mandatory)] [string] $BundlePath,
    [Parameter(Mandatory)] [ValidatePattern('^[a-fA-F0-9]{64}$')] [string] $ExpectedSha256,
    [string] $PackIndexPath,
    [string] $QualificationIndexPath,
    [string] $QualificationBundlePath,
    [string] $QualificationExpectedSha256,
    [string[]] $AdditionalLocalIndexPaths = @(),
    [switch] $Network,
    [ValidateSet('positive','negative')] [string] $NetworkMode = 'positive',
    [ValidateSet('missing','invalid')] [string] $LocalFallback,
    [string] $RawSkinDirectory,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/assetbundles/native-game-01')
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\','/')
function Resolve-InputPath([string] $Value) {
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $repository $Value))
}
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/assetbundles')).TrimEnd('\','/')
$source = [IO.Path]::GetFullPath($SourceGameRoot).TrimEnd('\','/')
$destination = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
if (!$destination.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage must be beneath this repository artifacts/assetbundles directory.' }
if ($destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $destination.Equals($source, [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage must be separate from the source.' }
if (Test-Path -LiteralPath $destination) { throw 'Use a new stage; existing directories are never overwritten.' }
$bundle = [IO.Path]::GetFullPath($BundlePath)
if ((Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Owner-built fixture SHA256 mismatch.' }
foreach ($path in @($source,$allowedRoot)) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse-point root is forbidden: $path" }
}
foreach ($relative in @('SCPSL.exe','GameAssembly.dll','UnityPlayer.dll','version.dll','MelonLoader/net6/MelonLoader.dll','MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) { throw "Missing seed input: $relative" }
}
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
$entries = @((($layout.packages | Where-Object packageId -eq 'anomaly-client').entries) | ForEach-Object {
    [pscustomobject]@{ source=($_.source -replace '/Release/','/Debug/'); path=$_.path }
})
$entries += [pscustomobject]@{source='tools/AssetBundleSmoke/bin/Debug/net6.0/Anomaly.AssetBundleSmoke.dll';path='Mods/Anomaly.AssetBundleSmoke.dll'}
foreach ($dependency in @('Anomaly.AssetValidation.dll','AssetsTools.NET.dll')) {
    if (!($entries | Where-Object path -eq "UserLibs/$dependency")) {
        $entries += [pscustomobject]@{source="Anomaly.Client/bin/Debug/net6.0/$dependency";path="UserLibs/$dependency"}
    }
}
if ($PackIndexPath) {
    $indexPath = [IO.Path]::GetFullPath($PackIndexPath)
    $index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
    if ($index.BundleSha256 -ne $ExpectedSha256 -or $index.BundleBytes -ne (Get-Item -LiteralPath $bundle).Length -or
        [IO.Path]::GetFileName($index.BundleFile) -ne $index.BundleFile) { throw 'Pipeline fixture index does not match the pinned bundle.' }
    $entries += [pscustomobject]@{source=$indexPath;path='UserData/Anomaly/AssetBundles/pack.index.json'}
    $entries += [pscustomobject]@{source=$bundle;path="UserData/Anomaly/AssetBundles/$($index.BundleFile)"}
}
if ($QualificationIndexPath -or $QualificationBundlePath -or $QualificationExpectedSha256) {
    if (!$PackIndexPath -or !$QualificationIndexPath -or !$QualificationBundlePath -or $QualificationExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Advanced qualification requires pipeline mode and all three qualification fixture arguments.'
    }
    $qualificationIndex = [IO.Path]::GetFullPath($QualificationIndexPath)
    $qualificationBundle = [IO.Path]::GetFullPath($QualificationBundlePath)
    if ((Get-FileHash -LiteralPath $qualificationBundle -Algorithm SHA256).Hash -ne $QualificationExpectedSha256) { throw 'Advanced fixture SHA256 mismatch.' }
    $entries += [pscustomobject]@{source=$qualificationIndex;path='Fixture/qualification.index.json'}
    $entries += [pscustomobject]@{source=$qualificationBundle;path='Fixture/qualification.bundle'}
}
if ($Network -and (!$PackIndexPath -or $QualificationIndexPath)) { throw 'Network mode requires the local pipeline and a separate run from advanced qualification.' }
if ($LocalFallback) {
    if ($Network -or $QualificationIndexPath -or !$RawSkinDirectory -or
        ($LocalFallback -eq 'missing' -and $PackIndexPath) -or ($LocalFallback -eq 'invalid' -and !$PackIndexPath)) { throw 'Fallback mode requires raw skin inputs and either zero packs or one valid pack plus generated invalid pair.' }
    foreach ($skinName in @('logo.png','background.png')) {
        $entries += [pscustomobject]@{source=(Join-Path (Resolve-InputPath $RawSkinDirectory) $skinName);path="UserData/Skins/$skinName"}
    }
}
foreach ($extraIndexInput in $AdditionalLocalIndexPaths) {
    $extraIndexPath = Resolve-InputPath $extraIndexInput
    $extraIndex = Get-Content -Raw -LiteralPath $extraIndexPath | ConvertFrom-Json
    if ([IO.Path]::GetFileName($extraIndex.BundleFile) -ne $extraIndex.BundleFile) { throw 'Additional local bundle must be adjacent to its index.' }
    $extraBundlePath = Join-Path (Split-Path $extraIndexPath) $extraIndex.BundleFile
    if ((Get-FileHash -LiteralPath $extraBundlePath -Algorithm SHA256).Hash -ne $extraIndex.BundleSha256 -or
        (Get-Item -LiteralPath $extraBundlePath).Length -ne $extraIndex.BundleBytes) { throw 'Additional local pack hash/length mismatch.' }
    $entries += [pscustomobject]@{source=$extraIndexPath;path="UserData/Anomaly/AssetBundles/$($extraIndex.BundleFile).index.json"}
    $entries += [pscustomobject]@{source=$extraBundlePath;path="UserData/Anomaly/AssetBundles/$($extraIndex.BundleFile)"}
}
if (@($entries.path | Group-Object | Where-Object Count -gt 1).Count) { throw 'Stage inputs collide on a destination path.' }
foreach ($entry in $entries) {
    if (!(Test-Path -LiteralPath (Resolve-InputPath $entry.source) -PathType Leaf)) { throw "Build the Debug input before staging: $($entry.source)" }
}
New-Item -ItemType Directory -Path $destination | Out-Null
$marker = Join-Path $destination 'assetbundle-smoke-staging.json'
[ordered]@{purpose='AssetBundleSmoke.v1';destination=$destination;source=$source;complete=$false;createdUtc=[DateTimeOffset]::UtcNow.ToString('O')} |
    ConvertTo-Json | Set-Content -LiteralPath $marker
$linked = 0
$copied = 0
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($source,$file.FullName)
    $parts = $relative.Split([IO.Path]::DirectorySeparatorChar)
    if ($parts[0] -in @('Mods','Plugins','UserLibs','UserData','AppData','ProfileAppData') -or
        $relative -match '(?i)(^|[\\/])(Logs|Latest\.log)([\\/]|$)' -or
        $file.Name.StartsWith('.') -or $file.Name -match '(?i)\.(bak|old|good|disabled)') { continue }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse-point file is forbidden: $relative" }
    $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
    if (!$target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage entry escapes destination.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if ($parts[0] -eq 'MelonLoader' -or $file.Extension -notin @('.dll','.exe','.assets','.resource','.ress','.bundle')) {
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $copied++
    } else {
        New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
        $linked++
    }
}
foreach ($entry in $entries) {
    $target = Join-Path $destination $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Resolve-InputPath $entry.source) -Destination $target
}
if ($LocalFallback -eq 'invalid') {
    $invalidRoot = Join-Path $destination 'UserData/Anomaly/AssetBundles'
    $invalidBundle = Join-Path $invalidRoot 'invalid-local.bundle'
    [IO.File]::WriteAllBytes($invalidBundle, [Text.Encoding]::ASCII.GetBytes('not-a-unity-bundle'))
    $invalidIndex = Get-Content -Raw -LiteralPath ([IO.Path]::GetFullPath($PackIndexPath)) | ConvertFrom-Json
    $invalidIndex.PackId = 'fixture:invalid-local'; $invalidIndex.Namespace = 'fixture'
    $invalidIndex.BundleFile = 'invalid-local.bundle'
    $invalidIndex.BundleSha256 = (Get-FileHash -LiteralPath $invalidBundle -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidIndex.BundleBytes = (Get-Item -LiteralPath $invalidBundle).Length
    $invalidAsset = $invalidIndex.Assets[0]
    $invalidAsset.Address = 'mods/fixture/textures/invalid'; $invalidAsset.EntryName = $invalidAsset.Address
    $invalidAsset.Width = 1; $invalidAsset.Height = 1
    $invalidIndex.Assets = @($invalidAsset); $invalidIndex.Visuals = @()
    $invalidIndexPath = Join-Path $invalidRoot 'zz-invalid.index.json'
    $invalidIndex | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $invalidIndexPath
    $entries += [pscustomobject]@{source=$invalidBundle;path='UserData/Anomaly/AssetBundles/invalid-local.bundle'}
    $entries += [pscustomobject]@{source=$invalidIndexPath;path='UserData/Anomaly/AssetBundles/zz-invalid.index.json'}
}
foreach ($folder in @('UserData/Anomaly','ProfileAppData/config','Fixture')) { New-Item -ItemType Directory -Force -Path (Join-Path $destination $folder) | Out-Null }
Copy-Item -LiteralPath $bundle -Destination (Join-Path $destination 'Fixture/skin.bundle')
$approved = @(Get-ChildItem -LiteralPath (Join-Path $destination 'Mods') -Filter '*.dll' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
@{schemaVersion=1;mode='enforce';approvedSha256=$approved} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'UserData/Anomaly/mod-trust.json')
@('[loader]','disable_start_screen = true','','[console]','hide_console = true','','[unityengine]','force_offline_generation = true') |
    Set-Content -LiteralPath (Join-Path $destination 'UserData/Loader.cfg')
$inputs = @($entries | ForEach-Object {
    [ordered]@{path=$_.path;source=$_.source;sha256=(Get-FileHash -LiteralPath (Join-Path $destination $_.path) -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{purpose='AssetBundleSmoke.v1';destination=$destination;source=$source;complete=$true;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');staticHardLinks=$linked;physicalCopies=$copied;bundleSha256=$ExpectedSha256.ToLowerInvariant();productionPipeline=[bool]$PackIndexPath;qualificationSha256=$QualificationExpectedSha256;network=[bool]$Network;networkMode=$NetworkMode;localFallback=$LocalFallback;inputs=$inputs} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $marker
Write-Output "Prepared isolated bundle smoke game: $destination ($linked static links, $copied physical copies)."
