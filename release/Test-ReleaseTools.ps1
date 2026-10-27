[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$toolRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolTempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("anomaly-release-tool-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $toolTempDirectory)
$previousSourceDateEpoch = $env:SOURCE_DATE_EPOCH
try {
    $privateKey = Join-Path $toolTempDirectory 'test-private.pem'
    $publicKey = Join-Path $toolTempDirectory 'test-public.pem'
    $otherPrivateKey = Join-Path $toolTempDirectory 'other-private.pem'
    $otherPublicKey = Join-Path $toolTempDirectory 'other-public.pem'
    $inventory = Join-Path $toolTempDirectory 'dependency-inventory.json'
    [IO.File]::WriteAllBytes($inventory, [Text.UTF8Encoding]::new($false).GetBytes('{"schemaVersion":1}'))

    . (Join-Path $toolRepositoryRoot 'tools/ContentPackFixture.ps1')
    $skinIndexPath = Join-Path $PSScriptRoot 'assets/skins/personal-skins.index.json'
    $skinBundlePath = Join-Path $PSScriptRoot 'assets/skins/personal-skins.bundle'
    $fixtureEntries = @(Get-AssetContentPackFixtureEntries -IndexPath $skinIndexPath -BundlePath $skinBundlePath -Root 'UserData/Anomaly/ContentPacks')
    $expectedFixturePaths = @(
        'UserData/Anomaly/ContentPacks/personal/skins/pack.json',
        'UserData/Anomaly/ContentPacks/personal/skins/assets/pack.index.json',
        'UserData/Anomaly/ContentPacks/personal/skins/assets/personal-skins.bundle'
    )
    if (@(Compare-Object ($fixtureEntries.path | Sort-Object) ($expectedFixturePaths | Sort-Object) -CaseSensitive).Count) {
        throw 'Exporter fixture wrapping must stage exactly one descriptor and its declared index/bundle.'
    }
    $fixtureDescriptor = ($fixtureEntries | Where-Object path -eq $expectedFixturePaths[0]).content | ConvertFrom-Json
    if ($fixtureDescriptor.SchemaVersion -ne 1 -or
        $fixtureDescriptor.PackId -cne 'personal:skins' -or $fixtureDescriptor.Assets.PackId -cne 'personal:skins' -or
        $fixtureDescriptor.Assets.Index -cne 'assets/pack.index.json') {
        throw 'Exporter fixture descriptor does not explicitly identify its sole asset component.'
    }
    $badFixtureBundle = Join-Path $toolTempDirectory 'bad-fixture.bundle'
    [IO.File]::WriteAllBytes($badFixtureBundle, [byte[]]@(1,2,3))
    $badFixtureRejected = $false
    try { Get-AssetContentPackFixtureEntries -IndexPath $skinIndexPath -BundlePath $badFixtureBundle | Out-Null }
    catch {
        if ($_.Exception.Message -notlike '*bundle/index identity mismatch*') { throw }
        $badFixtureRejected = $true
    }
    if (!$badFixtureRejected) { throw 'Exporter fixture wrapping accepted bytes not pinned by the index.' }

    $artifactNames = @(
        'Anomaly.Bootstrap.dll',
        'Anomaly.Client.Api.dll',
        'Anomaly.Client.dll',
        'Anomaly.Shared.dll'
    )
    foreach ($artifactName in $artifactNames) {
        [IO.File]::WriteAllBytes(
            (Join-Path $toolTempDirectory $artifactName),
            [Text.UTF8Encoding]::new($false).GetBytes(('test:' + $artifactName)))
    }

    & dotnet build (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release manifest tool build failed.' }
    & dotnet build (Join-Path $toolRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release packaging tool build failed.' }
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- generate-key $privateKey $publicKey
    if ($LASTEXITCODE -ne 0) { throw 'Test key generation failed.' }
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- generate-key $otherPrivateKey $otherPublicKey
    if ($LASTEXITCODE -ne 0) { throw 'Second test key generation failed.' }

    $env:SOURCE_DATE_EPOCH = '1787356800'
    $manifest = Join-Path $toolTempDirectory 'release.json'
    $createArguments = @(
        'run', '--project', (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj'),
        '-c', 'Release', '--no-build', '--', 'create', 'test-rotation-a', 'client-runtime',
        'anomaly-client', 'anomaly-client', '1.0.0', ('a' * 40), $inventory, $manifest,
        $privateKey
    )
    foreach ($artifactName in $artifactNames) { $createArguments += Join-Path $toolTempDirectory $artifactName }
    & dotnet @createArguments
    if ($LASTEXITCODE -ne 0) { throw 'Schema-v2 manifest creation failed.' }

    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -ne 0) { throw 'Schema-v2 manifest verification failed.' }

    $manifestObject = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    $expectedFields = @(
        'schemaVersion', 'keyId', 'target', 'packageId', 'releaseId', 'version',
        'sourceCommit', 'dependencyInventorySha256', 'generatedUtc', 'artifacts'
    )
    $actualFields = @($manifestObject.PSObject.Properties.Name)
    if (($actualFields -join ',') -ne ($expectedFields -join ',')) {
        throw ("Manifest fields do not match schema v2: {0}" -f ($actualFields -join ', '))
    }

    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $otherPublicKey
    if ($LASTEXITCODE -eq 0) { throw 'Wrong-key verification unexpectedly succeeded.' }

    $missingArtifact = Join-Path $toolTempDirectory 'Anomaly.Shared.dll'
    $missingArtifactBackup = $missingArtifact + '.missing'
    [IO.File]::Move($missingArtifact, $missingArtifactBackup)
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -eq 0) { throw 'Missing artifact verification unexpectedly succeeded.' }
    [IO.File]::Move($missingArtifactBackup, $missingArtifact)

    [IO.File]::AppendAllText((Join-Path $toolTempDirectory 'Anomaly.Client.dll'), 'tampered')
    & dotnet run --project (Join-Path $toolRepositoryRoot 'tools/ReleaseManifest/ReleaseManifest.csproj') `
        -c Release --no-build -- verify $manifest ($manifest + '.sig') $publicKey `
        $toolTempDirectory $inventory
    if ($LASTEXITCODE -eq 0) { throw 'Tampered artifact verification unexpectedly succeeded.' }

    # Exercise packaging preflight with an isolated layout. These failures occur
    # before runtime assemblies or game references are read.
    $fixtureRepository = Join-Path $toolTempDirectory 'layout-repository'
    $fixtureReferences = Join-Path $toolTempDirectory 'layout-references'
    $fixtureOutput = Join-Path $toolTempDirectory 'layout-output'
    [void][IO.Directory]::CreateDirectory((Join-Path $fixtureRepository 'release'))
    [void][IO.Directory]::CreateDirectory($fixtureReferences)
    $fixtureLayout = Join-Path $fixtureRepository 'release/package-layout.json'
    $originalLayout = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'package-layout.json')
    $packagerAssembly = Join-Path $toolRepositoryRoot 'tools/ReleasePackaging/bin/Release/net8.0/Anomaly.ReleasePackaging.dll'
    $fixtureArguments = @(
        $packagerAssembly, 'create-runtime', $fixtureRepository, $fixtureOutput,
        '1.0.0', $fixtureReferences, (Join-Path $fixtureRepository 'unused.index.json')
    )
    foreach ($project in @(
        'Anomaly.Bootstrap', 'Anomaly.Client', 'Anomaly.Client.Api',
        'Anomaly.AssetValidation', 'Anomaly.Server', 'Anomaly.Shared'
    )) {
        [IO.File]::WriteAllText((Join-Path $fixtureReferences ($project + '.refs')), $packagerAssembly)
    }
    [IO.File]::WriteAllText($fixtureLayout, $originalLayout, [Text.UTF8Encoding]::new($false))
    $failure = @(& dotnet @fixtureArguments 2>&1)
    if ($LASTEXITCODE -eq 0 -or
        ($failure -join "`n") -notlike '*Resolved-reference list for Anomaly.Client.Scripting is missing.*') {
        throw ("Reviewed scripting layout was rejected or its resolved references were not required: {0}" -f ($failure -join "`n"))
    }
    [IO.File]::WriteAllText((Join-Path $fixtureReferences 'Anomaly.Client.Scripting.refs'), $packagerAssembly)
    $failure = @(& dotnet @fixtureArguments 2>&1)
    if ($LASTEXITCODE -eq 0 -or
        ($failure -join "`n") -notlike '*Resolved-reference list for Anomaly.Client.ScriptWorker is missing.*') {
        throw ("Standalone worker resolved references were not required: {0}" -f ($failure -join "`n"))
    }
    # Restore the complete reference graph before exercising unrelated staging gates.
    [IO.File]::WriteAllText((Join-Path $fixtureReferences 'Anomaly.Client.ScriptWorker.refs'), $packagerAssembly)

    foreach ($requiredPath in @(
        'UserLibs/Anomaly.Client.Scripting.dll', 'UserLibs/Anomaly.MoonSharp.Interpreter.dll',
        'UserLibs/Anomaly.Client.ScriptWorker.dll',
        'UserLibs/Anomaly.Client.ScriptWorker.deps.json',
        'UserLibs/Anomaly.Client.ScriptWorker.runtimeconfig.json',
        'licenses/MoonSharp.LICENSE', 'licenses/MoonSharp.md',
        'UserData/Anomaly/ContentPacks/personal/skins/pack.json',
        'UserData/Anomaly/ContentPacks/personal/skins/assets/personal-skins.index.json',
        'UserData/Anomaly/ContentPacks/personal/skins/assets/personal-skins.bundle'
    )) {
        $layout = $originalLayout | ConvertFrom-Json
        $client = $layout.packages | Where-Object packageId -eq 'anomaly-client'
        $client.entries = @($client.entries | Where-Object path -ne $requiredPath)
        [IO.File]::WriteAllText($fixtureLayout, ($layout | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
        $failure = @(& dotnet @fixtureArguments 2>&1)
        if ($LASTEXITCODE -eq 0 -or
            ($failure -join "`n") -notlike "*Package 'anomaly-client' is missing a required runtime or SDK entry.*") {
            throw ("Missing scripting payload or redistribution notice was not rejected: {0}: {1}" -f $requiredPath,($failure -join "`n"))
        }
    }

    $sdkFixtureArguments = @($fixtureArguments)
    $sdkFixtureArguments[1] = 'create-local'
    foreach ($requiredPath in @(
        'reference/Anomaly.Client.Scripting.ScriptAPI.Shipped.txt',
        'reference/Anomaly.Client.Scripting.ScriptAPI.json',
        'reference/Anomaly.Client.Api.d.lua',
        'samples/HelloAnomalyServer/ContentPacks/example/panel/pack.json',
        'samples/HelloAnomalyServer/ContentPacks/example/panel/scripts/main.lua',
        'samples/HelloAnomalyServer/ContentPacks/example/panel/scripts/messages.json',
        'Anomaly.Server/MODDING.md',
        'Anomaly.Server/PLAYER_POSES.md',
        'Anomaly.Server/COMBAT_POSES.md',
        'Anomaly.Client.Scripting/README.md',
        'Anomaly.Client.Scripting/SCRIPT-PACKS.md',
        'Anomaly.Client.Scripting/MESSAGING-DESIGN.md',
        'Anomaly.Client.Scripting/CAPABILITIES.md',
        'Anomaly.Client.Scripting/CONNECTION-LIFECYCLE.md',
        'Anomaly.Client.Scripting/ALLOCATION-AUDIT.md',
        'Anomaly.Client.Scripting/ScriptAPI.Shipped.txt',
        'Anomaly.Client.Scripting/ScriptAPI.json',
        'Anomaly.Client.Scripting/Anomaly.Client.Api.d.lua',
        'tools/MoonSharpHardening/README.md',
        'tools/MoonSharpHardening/Fork-README.md',
        'tools/MoonSharpHardening/LICENSE'
    )) {
        $layout = $originalLayout | ConvertFrom-Json
        $sdk = $layout.packages | Where-Object packageId -eq 'anomaly-sdk'
        $sdk.entries = @($sdk.entries | Where-Object path -ne $requiredPath)
        [IO.File]::WriteAllText($fixtureLayout, ($layout | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
        $failure = @(& dotnet @sdkFixtureArguments 2>&1)
        if ($LASTEXITCODE -eq 0 -or
            ($failure -join "`n") -notlike "*Package 'anomaly-sdk' is missing a required runtime or SDK entry.*") {
            throw ("Missing SDK scripting resource, guide, or export was not rejected at preflight: {0}: {1}" -f $requiredPath,($failure -join "`n"))
        }
    }

    # A local runtime archive must read its BSD notice, rather than silently
    # dropping non-DLL manifest entries. Put the absent notice first to isolate it.
    $layout = $originalLayout | ConvertFrom-Json
    $client = $layout.packages | Where-Object packageId -eq 'anomaly-client'
    $notice = $client.entries | Where-Object path -eq 'licenses/MoonSharp.LICENSE'
    $client.entries = @($notice) + @($client.entries | Where-Object path -ne 'licenses/MoonSharp.LICENSE')
    [IO.File]::WriteAllText($fixtureLayout, ($layout | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    $failure = @(& dotnet @fixtureArguments 2>&1)
    $missingNotice = Join-Path $fixtureRepository 'third_party/MoonSharp/LICENSE'
    if ($LASTEXITCODE -eq 0 -or
        -not ($failure -join "`n").Contains("Runtime source '$missingNotice' is missing.")) {
        throw ("Local runtime staging did not require the declared MoonSharp BSD notice: {0}" -f ($failure -join "`n"))
    }

    $global:LASTEXITCODE = 0
    Write-Host 'Release manifest signing/tamper gates and scripting payload, reference, BSD notice, and SDK export gates passed.'
}
finally {
    $env:SOURCE_DATE_EPOCH = $previousSourceDateEpoch
    $resolvedToolTemp = [System.IO.Path]::GetFullPath($toolTempDirectory)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedToolTemp.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedToolTemp).StartsWith('anomaly-release-tool-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedToolTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
