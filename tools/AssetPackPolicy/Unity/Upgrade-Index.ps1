[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InputIndex,
    [Parameter(Mandatory)] [string] $OutputIndex,
    [switch] $Local,
    [string] $BundlePath = ''
)
$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot 'bin/AssetPackIndexUpgrade.dll'
if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Authoring SDK upgrade tool missing: $tool" }
$arguments = @($tool, [IO.Path]::GetFullPath($InputIndex), [IO.Path]::GetFullPath($OutputIndex))
if ($Local) { $arguments += '--local' }
if ($BundlePath) { $arguments += '--bundle=' + [IO.Path]::GetFullPath($BundlePath) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'The index was not rebuilt. Existing indexes and bundle bytes were preserved.' }
