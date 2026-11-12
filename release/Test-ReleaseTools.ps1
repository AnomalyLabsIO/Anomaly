[CmdletBinding()]
param(
    [switch] $IncludeSigningTests
)

$ErrorActionPreference = 'Stop'
$toolRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolTempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("anomaly-release-tool-test-{0}" -f [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $toolTempDirectory)
$previousSourceDateEpoch = $env:SOURCE_DATE_EPOCH
try {
    $env:SOURCE_DATE_EPOCH = '1787356800'
    & dotnet build (Join-Path $toolRepositoryRoot 'tools/ReleasePackaging/ReleasePackaging.csproj') `
        -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release packaging tool build failed.' }
    & dotnet build (Join-Path $toolRepositoryRoot 'tools/AssetPackIndexUpgrade/AssetPackIndexUpgrade.csproj') `
        -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Asset index rebuild tool build failed.' }

    . (Join-Path $toolRepositoryRoot 'tools/ContentPackFixture.ps1')
    $skinIndexPath = Join-Path $PSScriptRoot 'assets/skins/personal-skins.index.json'
    $skinBundlePath = Join-Path $PSScriptRoot 'assets/skins/personal-skins.bundle'
    & dotnet (Join-Path $toolRepositoryRoot 'tools/ReleasePackaging/bin/Release/net8.0/Anomaly.ReleasePackaging.dll') validate-skins $skinIndexPath
    if ($LASTEXITCODE -ne 0) { throw 'The release skin resource declaration does not match its independently validated bundle.' }
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

    # Execute the SDK-relative tool, staged only from its declared package inputs.
    # This must not need the core checkout or its sibling authoring project.
    $sdkAuthoringStage = Join-Path $toolTempDirectory 'portable-sdk'
    $sourceLayout = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'package-layout.json') | ConvertFrom-Json
    $authoringEntries = @((($sourceLayout.packages | Where-Object packageId -eq 'anomaly-sdk').entries) |
        Where-Object { $_.path.StartsWith('authoring/', [StringComparison]::Ordinal) })
    foreach ($entry in $authoringEntries) {
        $destination = Join-Path $sdkAuthoringStage $entry.path
        [IO.Directory]::CreateDirectory((Split-Path $destination)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $toolRepositoryRoot $entry.source) -Destination $destination
    }
    $sdkUpgrade = Join-Path $sdkAuthoringStage 'authoring/Upgrade-Index.ps1'
    $rebuiltIndex = Join-Path $toolTempDirectory 'rebuilt-skin.index.json'
    $sourceIndexHash = (Get-FileHash -LiteralPath $skinIndexPath -Algorithm SHA256).Hash
    & $sdkUpgrade -InputIndex $skinIndexPath -OutputIndex $rebuiltIndex -Local
    $rebuilt = Get-Content -Raw -LiteralPath $rebuiltIndex | ConvertFrom-Json
    if ($rebuilt.SchemaVersion -ne 2 -or $rebuilt.DecodedBytes -ne 41697280 -or
        (Get-FileHash -LiteralPath $skinIndexPath -Algorithm SHA256).Hash -ne $sourceIndexHash) {
        throw 'The portable authoring kit failed to measure the exact bundled skin without changing its source.'
    }
    $rebuiltHash = (Get-FileHash -LiteralPath $rebuiltIndex -Algorithm SHA256).Hash
    $overwriteRejected = $false
    try { & $sdkUpgrade -InputIndex $skinIndexPath -OutputIndex $rebuiltIndex -Local }
    catch { $overwriteRejected = $true }
    if (!$overwriteRejected -or (Get-FileHash -LiteralPath $rebuiltIndex -Algorithm SHA256).Hash -ne $rebuiltHash) {
        throw 'The portable index tool overwrote an existing destination.'
    }

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
        'authoring/README.md',
        'authoring/Build-Pack.ps1',
        'authoring/Upgrade-Index.ps1',
        'authoring/AssetPackExporter.cs',
        'authoring/bin/AssetPackIndexUpgrade.dll',
        'authoring/bin/AssetPackIndexUpgrade.deps.json',
        'authoring/bin/AssetPackIndexUpgrade.runtimeconfig.json',
        'authoring/bin/Anomaly.AssetValidation.dll',
        'authoring/bin/Anomaly.AssetPackPolicy.dll',
        'authoring/bin/AssetsTools.NET.dll',
        'authoring/licenses/AssetsTools.NET.md',
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

    if ($IncludeSigningTests) {
        & (Join-Path $PSScriptRoot 'Test-ReleaseSigning.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Optional release-signing tests failed.' }
    }

    $global:LASTEXITCODE = 0
    Write-Host 'Unsigned release packaging, scripting payload, reference, notices, SDK exports, and portable authoring gates passed.'
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
