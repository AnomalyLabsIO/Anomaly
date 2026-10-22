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
    [switch] $DummyVisibility,
    [switch] $DedicatedDummyControl
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game = [IO.Path]::GetFullPath($GameDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$stagingPath = Join-Path $game 'smoke-staging.json'
if ($UseModLaunch) {
    if (!(Test-Path -LiteralPath (Join-Path $game '.modlaunch/SCPSL-ModLaunch.exe'))) { throw 'The installed ModLaunch launcher is required.' }
} else {
    if (!(Test-Path -LiteralPath $stagingPath)) { throw 'Only a prepared game or an explicitly selected ModLaunch installation may be used.' }
    $staging = Get-Content -Raw -LiteralPath $stagingPath | ConvertFrom-Json
    if (![string]::Equals($staging.destination, $game, [StringComparison]::OrdinalIgnoreCase)) { throw 'The staging destination does not match.' }
}
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
if ($DedicatedDummyControl -and ($ConnectionOnly -or $ServerApi -or $LocalHosting -or $DummyVisibility)) {
    throw 'DedicatedDummyControl runs its own scenario and cannot be combined with other smoke modes.'
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
foreach ($entry in ($layout.packages | Where-Object packageId -eq 'anomaly-client').entries) {
    $target = Join-Path $game $entry.path
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $entry.source) -Destination $target
}
foreach ($mod in @('samples/HelloAnomalyMod/bin/Release/net6.0/HelloAnomalyMod.dll','tools/EarlyAccessUiSmoke/bin/Release/net6.0/Anomaly.EarlyAccessUiSmoke.dll')) {
    Copy-Item -LiteralPath (Join-Path $repository $mod) -Destination (Join-Path $game 'Mods')
}
# Approve only the three artifacts staged by this runner. Preserve prior approvals;
# never implicitly approve unrelated mods in an explicitly selected installation.
$policyPath = Join-Path $game 'UserData/Anomaly/mod-trust.json'
$previousApprovals = @()
if (Test-Path -LiteralPath $policyPath) {
    $previousApprovals = @((Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json).approvedSha256)
}
$approved = @($previousApprovals) + @('Anomaly.Client.dll','HelloAnomalyMod.dll','Anomaly.EarlyAccessUiSmoke.dll' | ForEach-Object {
    (Get-FileHash -LiteralPath (Join-Path $game ('Mods/' + $_)) -Algorithm SHA256).Hash.ToLowerInvariant()
})
$approved = @($approved | Where-Object { $_ } | Sort-Object -Unique)
@{ schemaVersion=1; mode='enforce'; approvedSha256=$approved } | ConvertTo-Json |
    Set-Content -LiteralPath $policyPath
New-Item -ItemType Directory -Path $evidence | Out-Null
$gitDir = Join-Path $repository '.git'
$sourceCommit = (& git --git-dir=$gitDir --work-tree=$repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the smoke source commit.' }
$sourceStatus = @(& git --git-dir=$gitDir --work-tree=$repository status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the smoke source worktree.' }
$inputPaths = @(($layout.packages | Where-Object packageId -eq 'anomaly-client').entries.path) +
    @('Mods/HelloAnomalyMod.dll','Mods/Anomaly.EarlyAccessUiSmoke.dll','MelonLoader/net6/MelonLoader.dll','SCPSL.exe')
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    $file = Get-Item -LiteralPath (Join-Path $game $_)
    [ordered]@{ path=$_; size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ sourceCommit=$sourceCommit; sourceWorktreeClean=($sourceStatus.Count -eq 0); startedUtc=[DateTimeOffset]::UtcNow.ToString('O'); inputs=$inputs } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-inputs.json')
$launchPath = if ($UseModLaunch) { Join-Path $game '.modlaunch/SCPSL-ModLaunch.exe' } else { Join-Path $game 'SCPSL.exe' }
$startInfo = [Diagnostics.ProcessStartInfo]::new($launchPath)
$startInfo.WorkingDirectory = $game
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = if ($UseModLaunch) { [Diagnostics.ProcessWindowStyle]::Normal } else { [Diagnostics.ProcessWindowStyle]::Hidden }
$startInfo.Environment['ANOMALY_UI_SMOKE_DIR'] = $evidence
$startInfo.Environment.Remove('ANOMALY_INTEGRITY_SMOKE_MODE') | Out-Null
if ($IntegrityMode) { $startInfo.Environment['ANOMALY_INTEGRITY_SMOKE_MODE'] = $IntegrityMode }
foreach ($name in @('ANOMALY_SMOKE_SERVER','ANOMALY_UI_SMOKE_CONNECTION_ONLY','ANOMALY_MOD_ADMISSION_EXPECT','ANOMALY_SERVER_API_SMOKE','ANOMALY_SERVER_API_SMOKE_MODE','ANOMALY_LOCAL_HOSTING_SMOKE','ANOMALY_DUMMY_VISIBILITY_SMOKE','ANOMALY_DEDICATED_DUMMY_SMOKE','ANOMALY_DUMMY_SCREEN_WIDTH','ANOMALY_DUMMY_SCREEN_HEIGHT')) {
    $startInfo.Environment.Remove($name) | Out-Null
}
if ($LoopbackServer) { $startInfo.Environment['ANOMALY_SMOKE_SERVER'] = $LoopbackServer }
if ($ConnectionOnly) { $startInfo.Environment['ANOMALY_UI_SMOKE_CONNECTION_ONLY'] = '1' }
if ($AdmissionExpect) { $startInfo.Environment['ANOMALY_MOD_ADMISSION_EXPECT'] = $AdmissionExpect }
if ($ServerApi) {
    $startInfo.Environment['ANOMALY_SERVER_API_SMOKE'] = '1'
    $startInfo.Environment['ANOMALY_SERVER_API_SMOKE_MODE'] = $ServerApiMode
}
if ($LocalHosting) {
    $startInfo.Environment['ANOMALY_LOCAL_HOSTING_SMOKE'] = '1'
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
$pidFile = if ($UseModLaunch) { 'launcher.pid' } else { 'client.pid' }
$process.Id | Set-Content -LiteralPath (Join-Path $evidence $pidFile)
Write-Output "Started smoke entry point $launchPath, PID $($process.Id). Evidence: $evidence"
