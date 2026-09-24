[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $PackIndexPath
)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$indexPath = [IO.Path]::GetFullPath($PackIndexPath)
if (Test-Path -LiteralPath $destination) { throw 'Use a new asset directory for each fixture definition.' }
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
if ([IO.Path]::GetFileName($index.BundleFile) -ne $index.BundleFile) { throw 'Bundle must be beside its index.' }
$bundlePath = Join-Path (Split-Path $indexPath) $index.BundleFile
if ((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash -ne $index.BundleSha256 -or
    (Get-Item -LiteralPath $bundlePath).Length -ne $index.BundleBytes) { throw 'Bundle/index identity mismatch.' }
foreach ($required in @('mods/fixture/textures/checker','mods/fixture/audio/tone')) {
    if ($required -notin @($index.Assets | ForEach-Object { $_.Address })) { throw "Missing smoke fixture address: $required" }
}
New-Item -ItemType Directory -Path $destination | Out-Null
Copy-Item -LiteralPath $indexPath -Destination (Join-Path $destination 'smoke.index.json')
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $destination $index.BundleFile)
Get-ChildItem -LiteralPath $destination -File | Get-FileHash -Algorithm SHA256
