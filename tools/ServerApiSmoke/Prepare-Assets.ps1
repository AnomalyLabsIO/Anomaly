[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $PackIndexPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../ContentPackFixture.ps1')
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$indexPath = [IO.Path]::GetFullPath($PackIndexPath)
if (Test-Path -LiteralPath $destination) { throw 'Use a new content pack directory for each fixture definition.' }
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
if ([IO.Path]::GetFileName($index.BundleFile) -ne $index.BundleFile) { throw 'Bundle must be beside its index.' }
$bundlePath = Join-Path (Split-Path $indexPath) $index.BundleFile
if ((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash -ne $index.BundleSha256 -or
    (Get-Item -LiteralPath $bundlePath).Length -ne $index.BundleBytes) { throw 'Bundle/index identity mismatch.' }
foreach ($required in @('mods/fixture/textures/checker','mods/fixture/audio/tone')) {
    if ($required -notin @($index.Assets | ForEach-Object { $_.Address })) { throw "Missing smoke fixture address: $required" }
}
$entries = @(Get-AssetContentPackFixtureEntries -IndexPath $indexPath -BundlePath $bundlePath)
foreach ($entry in $entries) {
    $target = Join-Path $destination $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if ($null -eq $entry.source) {
        [IO.File]::WriteAllText($target, $entry.content, [Text.UTF8Encoding]::new($false))
    } else {
        Copy-Item -LiteralPath $entry.source -Destination $target
    }
    Get-FileHash -LiteralPath $target -Algorithm SHA256
}
