[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Build Tools are required for the Win64 native prototype.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'The Visual C++ x64 toolchain is unavailable.' }
$vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
$source = Join-Path $PSScriptRoot 'CodeIntegrity.cpp'
$output = Join-Path $PSScriptRoot "..\Anomaly.Bootstrap\bin\$Configuration\net6.0\Anomaly.Bootstrap.Native.dll"
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$flags = if ($Configuration -eq 'Release') { '/O2 /DNDEBUG' } else { '/Od /Zi' }
$command = 'call "' + $vcvars + '" >nul && cl /nologo /std:c++17 /EHsc /MT /LD ' + $flags + ' /Fd"' + (Join-Path (Split-Path $output) 'Anomaly.Bootstrap.Native.pdb') + '" /Fo"' + (Join-Path (Split-Path $output) 'CodeIntegrity.obj') + '" /Fe:"' + $output + '" "' + $source + '" /link /IMPLIB:"' + (Join-Path (Split-Path $output) 'Anomaly.Bootstrap.Native.lib') + '"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $output)) { throw "Native Win64 build failed ($LASTEXITCODE)." }
Write-Output $output
