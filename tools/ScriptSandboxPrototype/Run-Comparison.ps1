[CmdletBinding()]
param(
    [switch]$SkipNativeBuild,
    [string]$CMake = 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
    [string]$OutputDirectory,
    [string]$MoonSharpAssemblyPath,
    [ValidateSet('all', 'moonsharp', 'luau')][string]$Runtime = 'all',
    [string]$Case
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'ScriptSandboxPrototype.csproj'
$binaryPath = Join-Path $PSScriptRoot 'bin/Release/net6.0/Anomaly.ScriptSandboxPrototype.dll'
$buildProperties = @('-p:DeployLocal=false')
$restoreOptions = @('--locked-mode')
if ($MoonSharpAssemblyPath) {
    $MoonSharpAssemblyPath = (Resolve-Path -LiteralPath $MoonSharpAssemblyPath).Path
    $projectPath = Join-Path $PSScriptRoot 'LocalMoonSharp/ScriptSandboxPrototype.LocalMoonSharp.csproj'
    $binaryPath = Join-Path $PSScriptRoot 'LocalMoonSharp/bin/Release/net6.0/Anomaly.ScriptSandboxPrototype.dll'
    $buildProperties += "-p:MoonSharpAssemblyPath=$MoonSharpAssemblyPath"
    $restoreOptions = @()
    if (-not $OutputDirectory) {
        $repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
        $OutputDirectory = Join-Path $repositoryRoot 'artifacts/script-sandbox/moonsharp-local'
    }
}
if (-not $SkipNativeBuild -and $Runtime -ne 'moonsharp') {
    & (Join-Path $PSScriptRoot 'Build-Luau.ps1') -CMake $CMake -Configuration Release
}

& dotnet restore $projectPath @restoreOptions @buildProperties --nologo
if ($LASTEXITCODE -ne 0) { throw 'Prototype restore failed.' }
& dotnet build $projectPath -c Release --no-restore @buildProperties -p:UseSharedCompilation=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed.' }

$comparisonArgs = @($binaryPath, '--runtime', $Runtime)
if ($OutputDirectory) { $comparisonArgs += @('--output', $OutputDirectory) }
if ($Case) { $comparisonArgs += @('--case', $Case) }
& dotnet @comparisonArgs
if ($LASTEXITCODE -ne 0) { throw 'Comparison did not match its documented expectations; inspect comparison.json.' }
