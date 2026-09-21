[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseOutput
)

$ErrorActionPreference = 'Stop'
$ReleaseOutput = [System.IO.Path]::GetFullPath($ReleaseOutput)
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

    $bouncyCastlePath = Join-Path $stagedSdk 'lib/net48/BouncyCastle.Cryptography.dll'
    if (-not (Test-Path -LiteralPath $bouncyCastlePath -PathType Leaf)) {
        throw 'Staged SDK is missing BouncyCastle.Cryptography.dll.'
    }
    [IO.File]::Move($bouncyCastlePath, $bouncyCastlePath + '.missing')
    $negativeProject = Join-Path $sdkTestRoot 'negative'
    New-CompileProbe $negativeProject
    & dotnet build (Join-Path $negativeProject 'SdkCompileProbe.csproj') `
        -c Release -warnaserror --nologo
    if ($LASTEXITCODE -eq 0) {
        throw 'SDK probe unexpectedly compiled without its public Bouncy Castle dependency.'
    }

    $global:LASTEXITCODE = 0
    Write-Host 'SDK reference metadata and positive/negative Bouncy Castle compile-closure gates passed.'
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
