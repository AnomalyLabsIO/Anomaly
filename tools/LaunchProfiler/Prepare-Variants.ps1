[CmdletBinding()]
param(
    [ValidateSet('baseline', 'identity-once', 'unchanged-module-lock')]
    [string[]] $Variant = @('baseline', 'identity-once', 'unchanged-module-lock'),
    [string] $SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string] $OutputRoot = '',
    [string] $ClientRoot = '',
    [string] $ServerRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $Build
)

$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
if (!$OutputRoot) { $OutputRoot = Join-Path $SourceRoot 'artifacts/launch-profiler/variants' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (!$ClientRoot) { $ClientRoot = Join-Path (Split-Path $SourceRoot -Parent) 'Client' }
if (!$ServerRoot) { $ServerRoot = Join-Path (Split-Path $SourceRoot -Parent) 'Server' }
$ClientRoot = [IO.Path]::GetFullPath($ClientRoot)
$ServerRoot = [IO.Path]::GetFullPath($ServerRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $SourceRoot 'artifacts/launch-profiler')) + [IO.Path]::DirectorySeparatorChar
if (!$OutputRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Variant output must be under this repository artifacts/launch-profiler directory.'
}

# Source changes must be explicitly reviewed before these experiments can be prepared again.
$expected = @{
    'Anomaly.Client/Auth/PlayerAuthProfileStore.cs' = 'DB90B1F69B2F642266AD575CA31FA863E687DD4E52FE427CD623171EFEB552D2'
    'Anomaly.Client/Internal/Modding/ManagedModRuntime.cs' = '4647992925BD9949A6CDC9F4C995030D92B249E801F83F1D70A3477A0B8502EB'
}
foreach ($relative in $expected.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot $relative) -Algorithm SHA256).Hash -ne $expected[$relative]) {
        throw "Reviewed source hash no longer matches $relative. Review the transform before updating its pinned hash."
    }
}

function Write-Utf8([string] $Path, [string] $Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Copy-SourceTree([string] $Project, [string] $Destination) {
    $projectRoot = Join-Path $SourceRoot $Project
    foreach ($file in Get-ChildItem -LiteralPath $projectRoot -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|\.git)([\\/]|$)' -or $relative.EndsWith('.user')) { continue }
        $target = Join-Path (Join-Path $Destination $Project) $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy($file.FullName, $target, $false)
    }
}

function Replace-Once([string] $Text, [string] $Old, [string] $New) {
    $offset = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($offset -lt 0 -or $Text.IndexOf($Old, $offset + $Old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw 'Private source transform did not find exactly one expected anchor.'
    }
    return $Text.Substring(0, $offset) + $New + $Text.Substring($offset + $Old.Length)
}

$results = @()
foreach ($name in $Variant) {
    $destination = Join-Path $OutputRoot ($name + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($project in @('Anomaly.Client', 'Anomaly.Client.Api', 'Anomaly.Shared')) { Copy-SourceTree $project $destination }
    foreach ($relative in @('Anomaly.Server/Internal/Map/FacilityGeneration.cs', 'Anomaly.Server/Internal/Map/FacilityGenerationDiagnostics.cs')) {
        $target = Join-Path $destination $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy((Join-Path $SourceRoot $relative), $target, $false)
    }
    foreach ($file in @('Directory.Build.props', 'Directory.Build.targets')) {
        [IO.File]::Copy((Join-Path $SourceRoot $file), (Join-Path $destination $file), $false)
    }
    $sourceManifest = @(Get-ChildItem -LiteralPath $destination -File -Recurse | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })

    if ($name -eq 'identity-once') {
        $path = Join-Path $destination 'Anomaly.Client/Auth/PlayerAuthProfileStore.cs'
        $source = [IO.File]::ReadAllText($path)
        $start = $source.IndexOf('    private PreparedPlayerAuth PrepareExisting(', [StringComparison]::Ordinal)
        $end = $source.IndexOf('    private static PlayerAuth DeserializeStrict(', [StringComparison]::Ordinal)
        if ($start -lt 0 -or $end -le $start) { throw 'Identity source method boundaries changed.' }
        $replacement = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Variants/IdentityOnce.cs.txt'))
        Write-Utf8 $path ($source.Substring(0, $start) + $replacement + [Environment]::NewLine + $source.Substring($end))
    }
    elseif ($name -eq 'unchanged-module-lock') {
        $path = Join-Path $destination 'Anomaly.Client/Internal/Modding/ManagedModRuntime.cs'
        $source = [IO.File]::ReadAllText($path)
        $anchor = '    private static void WriteJsonAtomic(string path, object value)'
        $start = $source.IndexOf($anchor, [StringComparison]::Ordinal)
        $brace = $source.IndexOf('{', $start)
        if ($start -lt 0 -or $brace -lt 0) { throw 'Module bookkeeping method boundary changed.' }
        $insertion = "`n        if (value is LockFile && !ModuleLockWritePolicy.ShouldWrite(path, value)) return;"
        Write-Utf8 $path ($source.Substring(0, $brace + 1) + $insertion + $source.Substring($brace + 1))
        [IO.File]::Copy((Join-Path $PSScriptRoot 'Variants/ModuleLockWritePolicy.cs.txt'), (Join-Path $destination 'Anomaly.Client/Internal/Modding/ModuleLockWritePolicy.cs'))
        $project = Join-Path $destination 'Anomaly.Client/Anomaly.Client.csproj'
        $source = [IO.File]::ReadAllText($project)
        $anchor = '<Compile Include="Internal\Modding\ManagedModRuntime.cs" />'
        Write-Utf8 $project (Replace-Once $source $anchor ($anchor + "`n        " + '<Compile Include="Internal\Modding\ModuleLockWritePolicy.cs" />'))
    }

    $changedPaths = switch ($name) {
        'identity-once' { 'Anomaly.Client/Auth/PlayerAuthProfileStore.cs' }
        'unchanged-module-lock' {
            'Anomaly.Client/Internal/Modding/ManagedModRuntime.cs'
            'Anomaly.Client/Internal/Modding/ModuleLockWritePolicy.cs'
            'Anomaly.Client/Anomaly.Client.csproj'
        }
    }
    $modifiedSourceFiles = @($changedPaths | ForEach-Object {
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $destination $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $result = [ordered]@{
        variant = $name; sourceRoot = $destination; configuration = $Configuration
        referenceClientRoot = $ClientRoot; referenceServerRoot = $ServerRoot
        clientDll = Join-Path $destination "Anomaly.Client/bin/$Configuration/net6.0/Anomaly.Client.dll"
        buildSucceeded = $false; testsSucceeded = $null; sourceFiles = $sourceManifest; modifiedSourceFiles = $modifiedSourceFiles
        reviewedSourceHashes = $expected; createdUtc = [DateTime]::UtcNow.ToString('O')
    }
    $manifestPath = Join-Path $destination 'variant.json'
    Write-Utf8 $manifestPath ($result | ConvertTo-Json -Depth 7)
    if ($Build) {
        $buildLog = Join-Path $destination 'build.log'
        & dotnet build (Join-Path $destination 'Anomaly.Client/Anomaly.Client.csproj') -c $Configuration '-p:DeployLocal=false' "-p:AnomalyClientRoot=$ClientRoot" "-p:AnomalyServerRoot=$ServerRoot" *> $buildLog
        if ($LASTEXITCODE -ne 0) { throw "Private $name build failed; see $buildLog" }
        $result.buildSucceeded = $true
        $result.clientSha256 = (Get-FileHash -LiteralPath $result.clientDll -Algorithm SHA256).Hash.ToLowerInvariant()

        if ($name -ne 'baseline') {
            $testRoot = Join-Path $destination 'VariantTests'
            [IO.Directory]::CreateDirectory($testRoot) | Out-Null
            if ($name -eq 'identity-once') {
                foreach ($item in @(
                    @((Join-Path $SourceRoot 'Anomaly.Shared.Tests/PlayerAuthProfileStoreTests.cs'), 'ExistingProfileTests.cs'),
                    @((Join-Path $PSScriptRoot 'Variants/IdentityOnceTests.cs.txt'), 'IdentityOnceTests.cs'),
                    @((Join-Path $destination 'Anomaly.Client/Auth/PlayerAuth.cs'), 'PlayerAuth.cs'),
                    @((Join-Path $destination 'Anomaly.Client/Auth/PlayerAuthProfileStore.cs'), 'PlayerAuthProfileStore.cs')
                )) { [IO.File]::Copy($item[0], (Join-Path $testRoot $item[1])) }
            }
            else {
                [IO.File]::Copy((Join-Path $PSScriptRoot 'Variants/ModuleLockWritePolicyTests.cs.txt'), (Join-Path $testRoot 'ModuleLockWritePolicyTests.cs'))
                [IO.File]::Copy((Join-Path $destination 'Anomaly.Client/Internal/Modding/ModuleLockWritePolicy.cs'), (Join-Path $testRoot 'ModuleLockWritePolicy.cs'))
            }
            $sharedDll = [Security.SecurityElement]::Escape((Join-Path $destination "Anomaly.Shared/bin/$Configuration/Anomaly.Shared.dll"))
            $cryptoDll = [Security.SecurityElement]::Escape((Join-Path $ServerRoot 'server/SCPSL_Data/Managed/BouncyCastle.Cryptography.dll'))
            $testProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>latest</LangVersion><IsPackable>false</IsPackable><PlatformTarget>x64</PlatformTarget></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.5-beta1" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <Reference Include="Anomaly.Shared" HintPath="$sharedDll" />
    <Reference Include="BouncyCastle.Cryptography" HintPath="$cryptoDll" />
  </ItemGroup>
</Project>
"@
            $testProjectPath = Join-Path $testRoot 'VariantTests.csproj'
            Write-Utf8 $testProjectPath $testProject
            $testLog = Join-Path $destination 'tests.log'
            & dotnet test $testProjectPath -c $Configuration '-p:DeployLocal=false' '--logger' 'trx;LogFileName=variant.trx' '--results-directory' (Join-Path $destination 'test-results') *> $testLog
            if ($LASTEXITCODE -ne 0) { throw "Private $name tests failed; see $testLog" }
            $result.testsSucceeded = $true
        }
        Write-Utf8 $manifestPath ($result | ConvertTo-Json -Depth 7)
    }
    $results += [pscustomobject]@{ variant = $name; manifest = $manifestPath; clientDll = $result.clientDll; buildSucceeded = $result.buildSucceeded; testsSucceeded = $result.testsSucceeded }
}
$results | ConvertTo-Json -Depth 4
