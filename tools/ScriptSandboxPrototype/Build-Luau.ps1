[CmdletBinding()]
param(
    [string]$CMake = 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = $PSScriptRoot
$pin = Get-Content -LiteralPath (Join-Path $prototypeRoot 'native/luau-pin.json') -Raw | ConvertFrom-Json
$sourceRoot = Join-Path $prototypeRoot 'obj/luau-src'
$buildRoot = Join-Path $prototypeRoot 'obj/luau-native'
$repositoryRoot = Split-Path (Split-Path $prototypeRoot -Parent) -Parent
$outputRoot = Join-Path $repositoryRoot 'artifacts/native'

if (-not (Test-Path -LiteralPath $CMake)) { throw "CMake not found: $CMake" }
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    New-Item -ItemType Directory -Force (Split-Path $sourceRoot -Parent) | Out-Null
    & git clone --depth 1 --branch $pin.tag $pin.repository $sourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Official Luau source clone failed.' }
}
$actualCommit = (& git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $pin.commit) {
    throw "Luau checkout differs from pin. Expected $($pin.commit), found $actualCommit."
}
$sourceChanges = & git -C $sourceRoot status --porcelain --untracked-files=all
if ($LASTEXITCODE -ne 0 -or $sourceChanges) { throw 'Pinned Luau checkout must be clean.' }

& $CMake -S (Join-Path $prototypeRoot 'native') -B $buildRoot -G 'Visual Studio 17 2022' -A x64 "-DLUAU_SOURCE_DIR=$sourceRoot"
if ($LASTEXITCODE -ne 0) { throw 'Luau native configuration failed.' }
& $CMake --build $buildRoot --config $Configuration --target luau_sandbox --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Luau native build failed.' }

New-Item -ItemType Directory -Force $outputRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $buildRoot "$Configuration/luau_sandbox.dll") -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE.txt') -Destination (Join-Path $outputRoot 'Luau-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $prototypeRoot 'native/luau-pin.json') -Destination $outputRoot
Write-Output "Built Luau $($pin.tag) ($actualCommit): $(Join-Path $outputRoot 'luau_sandbox.dll')"
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $outputRoot 'luau_sandbox.dll')
