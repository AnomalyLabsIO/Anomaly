[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $GameDirectory,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [string] $LoopbackServer = '',
    [switch] $ConnectionOnly,
    [string] $AdmissionExpect = '',
    [ValidateSet('', 'clean', 'blocked', 'allowed-rejected', 'allowed-accepted', 'gap-rejected', 'gap-accepted', 'late')]
    [string] $IntegrityMode = '',
    [switch] $UseModLaunch,
    [switch] $ServerApi,
    [string] $ServerApiMode = 'full',
    [switch] $LocalHosting,
    [switch] $ClientScripting,
    [switch] $DummyVisibility,
    [switch] $DedicatedDummyControl,
    [switch] $NewsOnly,
    [string] $BackupDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game = [IO.Path]::GetFullPath($GameDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$UseModLaunch) { throw 'Always select -UseModLaunch; direct SCPSL.exe launches are forbidden for this qualification.' }
if (![string]::Equals($game,'E:\Steam\steamapps\common\SCP Secret Laboratory - Copy',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use the explicitly authorized SCP Secret Laboratory - Copy installation and its .modlaunch launcher.'
}
if (!(Test-Path -LiteralPath (Join-Path $game '.modlaunch/SCPSL-ModLaunch.exe'))) { throw 'The installed ModLaunch launcher is required.' }
. (Join-Path $PSScriptRoot 'ModLaunchBackup.ps1')
Assert-SmokeGameIdle $game
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory for each run.' }
if ($LoopbackServer) {
    $uri = [Uri]('udp://' + $LoopbackServer)
    $address = $null
    if (![Net.IPAddress]::TryParse($uri.Host, [ref]$address) -or ![Net.IPAddress]::IsLoopback($address)) {
        throw 'The test server must be an explicit loopback IP endpoint.'
    }
}
if ($ConnectionOnly -and !$LoopbackServer) { throw 'ConnectionOnly requires a loopback test server.' }
if ($IntegrityMode -and (!$LoopbackServer -or $ConnectionOnly -or $AdmissionExpect -or $ServerApi -or $LocalHosting -or $DedicatedDummyControl)) {
    throw 'IntegrityMode requires a loopback test server and runs its own scenario.'
}
if ($AdmissionExpect -and !$LoopbackServer) { throw 'AdmissionExpect requires a loopback test server.' }
if ($ServerApi -and !$LoopbackServer) { throw 'ServerApi requires a loopback test server.' }
if ($AdmissionExpect -and ($ConnectionOnly -or $ServerApi -or $LocalHosting -or $DedicatedDummyControl)) {
    throw 'AdmissionExpect runs its own connection scenario.'
}
if ($LocalHosting -and ($ConnectionOnly -or $ServerApi)) { throw 'LocalHosting runs its own scenario and cannot be combined with ConnectionOnly or ServerApi.' }
if ($DummyVisibility -and !$LocalHosting) { throw 'DummyVisibility requires LocalHosting.' }
if ($DedicatedDummyControl -and !$LoopbackServer) { throw 'DedicatedDummyControl requires a loopback test server.' }
if ($ClientScripting -and (!$LocalHosting -or $LoopbackServer -or $DummyVisibility)) {
    throw 'ClientScripting requires LocalHosting and runs without a remote server or dummy fixture.'
}
if ($ClientScripting) {
    $scriptRoot = Join-Path $game 'UserData/Anomaly/ClientScripts'
    if (Test-Path -LiteralPath $scriptRoot) {
        $existingSources = @(Get-ChildItem -LiteralPath $scriptRoot -Recurse -File -Filter '*.lua')
        if ($existingSources.Count -ne 0) { throw 'ClientScripting requires an empty local script source tree; existing owner scripts are never replaced.' }
    }
}
if ($DedicatedDummyControl -and ($ConnectionOnly -or $ServerApi -or $LocalHosting -or $DummyVisibility)) {
    throw 'DedicatedDummyControl runs its own scenario and cannot be combined with other smoke modes.'
}
if ($NewsOnly -and ($LoopbackServer -or $ConnectionOnly -or $AdmissionExpect -or $IntegrityMode -or $ServerApi -or $LocalHosting -or $DummyVisibility -or $DedicatedDummyControl)) {
    throw 'NewsOnly runs its own UI scenario and cannot be combined with connection or hosting fixtures.'
}
$screenWidth = 1280
$screenHeight = 720
if ($DummyVisibility -or $DedicatedDummyControl) {
    Add-Type -AssemblyName System.Windows.Forms
    $primaryDisplay = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $screenWidth = $primaryDisplay.Width
    $screenHeight = $primaryDisplay.Height
    if ($screenWidth -le 0 -or $screenHeight -le 0) { throw 'Cannot determine the primary display resolution.' }
}
foreach ($project in @('Anomaly.Client/Anomaly.Client.csproj','samples/HelloAnomalyMod/HelloAnomalyMod.csproj','tools/EarlyAccessUiSmoke/EarlyAccessUiSmoke.csproj')) {
    & dotnet build (Join-Path $repository $project) -c Release -p:DeployLocal=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Smoke build failed: $project" }
}
$layout = Get-Content -Raw -LiteralPath (Join-Path $repository 'release/package-layout.json') | ConvertFrom-Json
$backupPaths = @((($layout.packages | Where-Object packageId -eq 'anomaly-client').entries).path) +
    @('Mods/HelloAnomalyMod.dll','Mods/Anomaly.EarlyAccessUiSmoke.dll','UserData/Anomaly/mod-trust.json',
      'UserData/Anomaly/user.json','UserData/Anomaly/server-trust.json','UserData/Anomaly/content-consent.json',
      'UserData/Loader.cfg','UserData/MelonPreferences.cfg')
if ($ClientScripting) {
    $backupPaths += @('start.lua','messages.json','owner-a.lua','owner-b.lua' | ForEach-Object {
        'UserData/Anomaly/ClientScripts/anomaly_smoke/output/' + $_
    })
}
$backupPaths += @(Get-ChildItem -LiteralPath (Join-Path $repository 'Anomaly.Client/Resources/i18n') -File -Filter '*.yaml' |
    ForEach-Object { 'UserData/Anomaly/i18n/anomaly/' + $_.Name })
if (!$BackupDirectory) { $BackupDirectory = Join-Path $repository ('artifacts/news-ui-smoke/modlaunch-backup-' + [IO.Path]::GetFileName($evidence)) }
$backupRoot = New-ModLaunchSmokeBackup $repository $game $BackupDirectory $backupPaths
Write-Output "Reversible test backup: $backupRoot. Restore with tools/EarlyAccessUiSmoke/Restore-ModLaunchSmoke.ps1 after both launcher and child exit and ModLaunch restores its session."
foreach ($entry in ($layout.packages | Where-Object packageId -eq 'anomaly-client').entries) {
    $target = Join-Path $game $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination $target
}
foreach ($mod in @('samples/HelloAnomalyMod/bin/Release/net6.0/HelloAnomalyMod.dll','tools/EarlyAccessUiSmoke/bin/Release/net6.0/Anomaly.EarlyAccessUiSmoke.dll')) {
    Copy-Item -LiteralPath (Join-Path $repository $mod) -Destination (Join-Path $game 'Mods')
}
if ($ClientScripting) {
    $scriptPack = Join-Path $game 'UserData/Anomaly/ClientScripts/anomaly_smoke/output'
    [void](New-Item -ItemType Directory -Force -Path $scriptPack)
    foreach ($file in @('start.lua','messages.json')) {
        Copy-Item -LiteralPath (Join-Path $repository "samples/HelloAnomalyServer/ClientScripts/$file") -Destination (Join-Path $scriptPack $file)
    }
    $ownedFixture = @'
Output:Log("Owned output fixture initialized.")
function ShowOwner(text, duration) return Output:ShowText(text, duration) end
function ClearOwner() Output:ClearText() end
function ReadGame()
    local player = Game.LocalPlayer
    return player ~= nil and not player.IsDestroyed and player.PlayerId >= 0
        and Game:GetPosition(player.PlayerId) ~= nil
end
'@
    foreach ($file in @('owner-a.lua','owner-b.lua')) {
        [IO.File]::WriteAllText((Join-Path $scriptPack $file), $ownedFixture, [Text.UTF8Encoding]::new($false))
    }
}
# Approve only the three artifacts staged by this runner. Preserve prior approvals;
# never implicitly approve unrelated mods in an explicitly selected installation.
$policyPath = Join-Path $game 'UserData/Anomaly/mod-trust.json'
$previousApprovals = @()
$policy = [PSCustomObject]@{schemaVersion=1;mode='enforce';approvedSha256=@()}
if (Test-Path -LiteralPath $policyPath) {
    $policy = Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json
    $previousApprovals = @($policy.approvedSha256)
}
$approved = @($previousApprovals) + @('Anomaly.Client.dll','HelloAnomalyMod.dll','Anomaly.EarlyAccessUiSmoke.dll' | ForEach-Object {
    (Get-FileHash -LiteralPath (Join-Path $game ('Mods/' + $_)) -Algorithm SHA256).Hash.ToLowerInvariant()
})
$approved = @($approved | Where-Object { $_ } | Sort-Object -Unique)
$policy | Add-Member -NotePropertyName approvedSha256 -NotePropertyValue $approved -Force
$policy | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $policyPath
New-Item -ItemType Directory -Path $evidence | Out-Null
$gitDir = Join-Path $repository '.git'
$sourceCommit = (& git --git-dir=$gitDir --work-tree=$repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the smoke source commit.' }
$sourceStatus = @(& git --git-dir=$gitDir --work-tree=$repository status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the smoke source worktree.' }
$inputPaths = @(($layout.packages | Where-Object packageId -eq 'anomaly-client').entries.path) +
    @('Mods/HelloAnomalyMod.dll','Mods/Anomaly.EarlyAccessUiSmoke.dll','MelonLoader/net6/MelonLoader.dll',
      'SCPSL.exe','GameAssembly.dll','UnityPlayer.dll','version.dll','SCPSL_Data/il2cpp_data/Metadata/global-metadata.dat')
$inputPaths += '.modlaunch/SCPSL-ModLaunch.exe'
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    $inputPath = Join-Path $game $_
    if (Test-Path -LiteralPath $inputPath -PathType Leaf) {
        $file = Get-Item -LiteralPath $inputPath
        [ordered]@{ path=$_; exists=$true; size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    } else { [ordered]@{path=$_;exists=$false;size=0;sha256=$null} }
})
[ordered]@{ sourceCommit=$sourceCommit; sourceWorktreeClean=($sourceStatus.Count -eq 0); beforeLaunch=$true;
    backupDirectory=$backupRoot; startedUtc=[DateTimeOffset]::UtcNow.ToString('O'); inputs=$inputs } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
$launchPath = Join-Path $game '.modlaunch/SCPSL-ModLaunch.exe'
$startInfo = [Diagnostics.ProcessStartInfo]::new($launchPath)
$startInfo.WorkingDirectory = $game
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
$startInfo.Environment['ANOMALY_UI_SMOKE_DIR'] = $evidence
$startInfo.Environment.Remove('ANOMALY_INTEGRITY_SMOKE_MODE') | Out-Null
if ($IntegrityMode) { $startInfo.Environment['ANOMALY_INTEGRITY_SMOKE_MODE'] = $IntegrityMode }
foreach ($name in @('ANOMALY_SMOKE_SERVER','ANOMALY_UI_SMOKE_CONNECTION_ONLY','ANOMALY_MOD_ADMISSION_EXPECT','ANOMALY_SERVER_API_SMOKE','ANOMALY_SERVER_API_SMOKE_MODE','ANOMALY_LOCAL_HOSTING_SMOKE','ANOMALY_CLIENT_SCRIPTING_SMOKE','ANOMALY_DUMMY_VISIBILITY_SMOKE','ANOMALY_DEDICATED_DUMMY_SMOKE','ANOMALY_DUMMY_SCREEN_WIDTH','ANOMALY_DUMMY_SCREEN_HEIGHT','ANOMALY_UI_SMOKE_NEWS_ONLY')) {
    $startInfo.Environment.Remove($name) | Out-Null
}
if ($NewsOnly) { $startInfo.Environment['ANOMALY_UI_SMOKE_NEWS_ONLY'] = '1' }
if ($LoopbackServer) { $startInfo.Environment['ANOMALY_SMOKE_SERVER'] = $LoopbackServer }
if ($ConnectionOnly) { $startInfo.Environment['ANOMALY_UI_SMOKE_CONNECTION_ONLY'] = '1' }
if ($AdmissionExpect) { $startInfo.Environment['ANOMALY_MOD_ADMISSION_EXPECT'] = $AdmissionExpect }
if ($ServerApi) {
    $startInfo.Environment['ANOMALY_SERVER_API_SMOKE'] = '1'
    $startInfo.Environment['ANOMALY_SERVER_API_SMOKE_MODE'] = $ServerApiMode
}
if ($LocalHosting) {
    $startInfo.Environment['ANOMALY_LOCAL_HOSTING_SMOKE'] = '1'
    if ($ClientScripting) { $startInfo.Environment['ANOMALY_CLIENT_SCRIPTING_SMOKE'] = '1' }
    if ($DummyVisibility) {
        $startInfo.Environment['ANOMALY_DUMMY_VISIBILITY_SMOKE'] = '1'
        $startInfo.Environment['ANOMALY_DUMMY_SCREEN_WIDTH'] = [string]$screenWidth
        $startInfo.Environment['ANOMALY_DUMMY_SCREEN_HEIGHT'] = [string]$screenHeight
    }
    # Only the isolated smoke copy hosts a server. Keep its writable stock data,
    # permissions and bind configuration private; production behavior is unchanged.
    $hostData = Join-Path $game 'LocalHostingSmoke/AppData'
    $hostConfig = Join-Path $hostData 'config'
    New-Item -ItemType Directory -Force -Path $hostConfig | Out-Null
    @('gamedir_for_configs: true','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1') |
        Set-Content -LiteralPath (Join-Path $game 'hoster_policy.txt')
    @('server_name: Anomaly isolated local hosting smoke','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1',
        'server_ip: 127.0.0.1','forward_ports: false','online_mode: false','enable_query: false',
        'contact_email: default','max_players: 4') |
        Set-Content -LiteralPath (Join-Path $hostConfig 'config_gameplay.txt')
    foreach ($argument in @('-appdatapath',$hostData,'-configpath',$hostConfig)) {
        $startInfo.ArgumentList.Add($argument)
    }
}
if ($DedicatedDummyControl) {
    $startInfo.Environment['ANOMALY_DEDICATED_DUMMY_SMOKE'] = '1'
    $startInfo.Environment['ANOMALY_DUMMY_SCREEN_WIDTH'] = [string]$screenWidth
    $startInfo.Environment['ANOMALY_DUMMY_SCREEN_HEIGHT'] = [string]$screenHeight
}
foreach ($argument in @('-noauth','-screen-fullscreen','0','-screen-width',[string]$screenWidth,'-screen-height',[string]$screenHeight,'-logFile',(Join-Path $evidence 'player.log'))) {
    $startInfo.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($startInfo)
$pidFile = 'launcher.pid'
$process.Id | Set-Content -LiteralPath (Join-Path $evidence $pidFile)
Write-Output "Started smoke entry point $launchPath, PID $($process.Id). Evidence: $evidence"
