[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseOutput,
    [string] $AnomalyClientInteropDir = '',
    [string] $AnomalyClientMelonLibDir = '',
    [string] $AnomalyServerReferenceDir = '',
    [string] $AnomalyLabApiDir = ''
)

$ErrorActionPreference = 'Stop'
$sdkRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Resolve the same overridable host references used by repository builds.
if (!$AnomalyClientInteropDir) {
    $AnomalyClientInteropDir = (& dotnet msbuild (Join-Path $sdkRepositoryRoot 'Anomaly.Client.Api/Anomaly.Client.Api.csproj') -getProperty:AnomalyClientInteropDir).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the SDK sample interop reference directory.' }
}
if (!$AnomalyClientMelonLibDir) {
    $AnomalyClientMelonLibDir = (& dotnet msbuild (Join-Path $sdkRepositoryRoot 'Anomaly.Client.Api/Anomaly.Client.Api.csproj') -getProperty:AnomalyClientMelonLibDir).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the SDK sample MelonLoader reference directory.' }
}
$ReleaseOutput = [System.IO.Path]::GetFullPath($ReleaseOutput)
foreach ($propertyName in @('AnomalyServerReferenceDir', 'AnomalyLabApiDir')) {
    if (!(Get-Variable -Name $propertyName -ValueOnly)) {
        $resolvedValue = (& dotnet msbuild (Join-Path $sdkRepositoryRoot 'Anomaly.Server/Anomaly.Server.csproj') "-getProperty:$propertyName").Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to resolve server SDK reference property $propertyName." }
        Set-Variable -Name $propertyName -Value $resolvedValue
    }
}
$sdkPayload = Join-Path $ReleaseOutput 'payloads/developer-sdk'
if (-not (Test-Path -LiteralPath $sdkPayload -PathType Container)) {
    throw "Developer SDK payload does not exist: $sdkPayload"
}

$sdkTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("anomaly-sdk-compile-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $sdkTestRoot)
try {
    $stagedSdk = Join-Path $sdkTestRoot 'sdk'
    Copy-Item -LiteralPath $sdkPayload -Destination $stagedSdk -Recurse

    $referenceMetadata = Get-Content -Raw -LiteralPath `
        (Join-Path $stagedSdk 'reference/sdk-reference-metadata.json') | ConvertFrom-Json
    if ($referenceMetadata.schemaVersion -ne 1) {
        throw 'SDK reference metadata schema is invalid.'
    }
    $sharedMetadata = $referenceMetadata.assemblies | Where-Object name -eq 'Anomaly.Shared'
    if ($null -eq $sharedMetadata -or
        $sharedMetadata.compileDependencies -notcontains 'BouncyCastle.Cryptography') {
        throw 'SDK metadata does not declare the public Anomaly.Shared Bouncy Castle dependency.'
    }

    function New-CompileProbe([string] $projectDirectory) {
        [void](New-Item -ItemType Directory -Path $projectDirectory)
        $sharedPath = Join-Path $stagedSdk 'lib/net48/Anomaly.Shared.dll'
        $bouncyPath = Join-Path $stagedSdk 'lib/net48/BouncyCastle.Cryptography.dll'
        $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Anomaly.Shared">
      <HintPath>$([Security.SecurityElement]::Escape($sharedPath))</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BouncyCastle.Cryptography">
      <HintPath>$([Security.SecurityElement]::Escape($bouncyPath))</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
"@
        $source = @"
using Anomaly.Shared.Auth;
using Org.BouncyCastle.Crypto;

public static class SdkCompileProbe
{
    public static AsymmetricCipherKeyPair ReadIdentity(byte[] identity) =>
        AuthCrypto.ReadIdentity(identity);

    public static int SigningCurveBits => AuthCrypto.SingingCurve.N.BitLength;
}
"@
        [IO.File]::WriteAllBytes(
            (Join-Path $projectDirectory 'SdkCompileProbe.csproj'),
            [Text.UTF8Encoding]::new($false).GetBytes($projectXml))
        [IO.File]::WriteAllBytes(
            (Join-Path $projectDirectory 'SdkCompileProbe.cs'),
            [Text.UTF8Encoding]::new($false).GetBytes($source))
    }

    $positiveProject = Join-Path $sdkTestRoot 'positive'
    New-CompileProbe $positiveProject
    & dotnet build (Join-Path $positiveProject 'SdkCompileProbe.csproj') `
        -c Release -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Staged SDK compile closure is incomplete.' }

    foreach ($xmlRelative in @('lib/net6.0/Anomaly.Client.Api.xml', 'lib/net48/Anomaly.Shared.xml', 'lib/net48/Anomaly.Server.xml')) {
        if (!(Test-Path -LiteralPath (Join-Path $stagedSdk $xmlRelative) -PathType Leaf)) {
            throw "SDK IntelliSense documentation is missing: $xmlRelative"
        }
    }
    foreach ($sampleName in @('HelloAnomalyMod', 'HelloAnomalyModule')) {
        $sampleProject = Join-Path $stagedSdk "samples/$sampleName/$sampleName.csproj"
        if (!(Test-Path -LiteralPath $sampleProject -PathType Leaf)) { throw "SDK starter is missing: $sampleName" }
        & dotnet build $sampleProject -c Release -warnaserror --nologo `
            "-p:AnomalySdkRoot=$stagedSdk" "-p:AnomalyClientInteropDir=$AnomalyClientInteropDir" `
            "-p:AnomalyClientMelonLibDir=$AnomalyClientMelonLibDir" -p:DeployLocal=false
        if ($LASTEXITCODE -ne 0) { throw "Packaged SDK starter failed: $sampleName" }
    }

    foreach ($sampleName in @('HelloAnomalyServer', 'HelloPlayerPoses')) {
        $serverSampleProject = Join-Path $stagedSdk "samples/$sampleName/$sampleName.csproj"
        if (!(Test-Path -LiteralPath $serverSampleProject -PathType Leaf)) { throw "Server SDK starter is missing: $sampleName" }
        & dotnet build $serverSampleProject -c Release -warnaserror --nologo `
            "-p:AnomalySdkRoot=$stagedSdk" "-p:AnomalyServerReferenceDir=$AnomalyServerReferenceDir" `
            "-p:AnomalyLabApiDir=$AnomalyLabApiDir" -p:DeployLocal=false
        if ($LASTEXITCODE -ne 0) { throw "Packaged server SDK starter failed: $sampleName" }
    }

    $bouncyCastlePath = Join-Path $stagedSdk 'lib/net48/BouncyCastle.Cryptography.dll'
    if (-not (Test-Path -LiteralPath $bouncyCastlePath -PathType Leaf)) {
        throw 'Staged SDK is missing BouncyCastle.Cryptography.dll.'
    }
    [IO.File]::Move($bouncyCastlePath, $bouncyCastlePath + '.missing')
    $negativeProject = Join-Path $sdkTestRoot 'negative'
    New-CompileProbe $negativeProject
    $negativeOutput = @(& dotnet build (Join-Path $negativeProject 'SdkCompileProbe.csproj') `
        -c Release -warnaserror --nologo 2>&1)
    if ($LASTEXITCODE -eq 0) {
        throw 'SDK probe unexpectedly compiled without its public Bouncy Castle dependency.'
    }
    $negativeText = $negativeOutput -join [Environment]::NewLine
    if ($negativeText -notmatch 'MSB3245[^\r\n]*BouncyCastle\.Cryptography' -or
        $negativeText -notmatch 'CS0246[^\r\n]*(Org|AsymmetricCipherKeyPair)') {
        throw "Negative SDK probe failed for an unexpected reason: $negativeText"
    }

    $global:LASTEXITCODE = 0
    Write-Host 'All client/server packaged starters, adjacent XML docs, and positive/negative SDK compile-closure gates passed.'
}
finally {
    $resolvedSdkTestRoot = [System.IO.Path]::GetFullPath($sdkTestRoot)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedSdkTestRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedSdkTestRoot).StartsWith(
            'anomaly-sdk-compile-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedSdkTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
