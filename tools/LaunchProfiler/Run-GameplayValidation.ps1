# Reuses the existing smoke mod without Run-Smoke.ps1's build/package replacement.
# ConnectionOnly checks two authenticated cycles and menu/settings cleanup, not locomotion.
# DedicatedDummyControl additionally uses stock RA and real input-driven movement.
# Results still require screenshot review; neither mode certifies physical input or menu-ready timing.
# Runtime-only profiler experiments are intentionally unsupported while that plugin is parked.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameDirectory,
    [Parameter(Mandatory)] [string] $ServerDirectory,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [ValidateRange(1024,65535)] [int] $Port = 27995,
    [switch] $ConnectionOnly,
    [switch] $DedicatedDummyControl,
    [string] $ServerIdentityFile = '',
    [string] $VariantManifest = '',
    [switch] $OfflineGenerator,
    [string] $HarnessDll = (Join-Path $PSScriptRoot '../EarlyAccessUiSmoke/bin/Release/net6.0/Anomaly.EarlyAccessUiSmoke.dll'),
    [string] $StarterDll = (Join-Path $PSScriptRoot '../../samples/HelloAnomalyMod/bin/Release/net6.0/HelloAnomalyMod.dll'),
    [ValidateRange(1,7680)] [int] $ScreenWidth = 1280,
    [ValidateRange(1,4320)] [int] $ScreenHeight = 720,
    [ValidateRange(60,1800)] [int] $TimeoutSeconds = 600,
    [switch] $PrepareOnly,
    [switch] $Recover
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required for exact argument forwarding.' }
if ($ConnectionOnly -and $DedicatedDummyControl) { throw 'ConnectionOnly and DedicatedDummyControl are separate scenarios.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $repository 'artifacts'
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$server = [IO.Path]::GetFullPath($ServerDirectory).TrimEnd('\','/')
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\','/')
function Assert-Within([string] $Path, [string] $Root) {
    if (![IO.Path]::GetFullPath($Path).StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must be beneath the private root: $Path"
    }
}
function Assert-NoLinks([string] $Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse points are not accepted: $cursor"
        }
    }
    if (Test-Path -LiteralPath $Path -PathType Container) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse points are not accepted: $($item.FullName)" }
        }
    }
}
foreach ($path in @($game,$server,$evidence)) { Assert-Within $path $artifacts; Assert-NoLinks $path }
foreach ($a in @($game,$server,$evidence)) {
    foreach ($b in @($game,$server,$evidence)) {
        if ($a -ne $b -and $a.StartsWith($b + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Game, server, and evidence directories must be separate.' }
    }
}
if ($game -eq $server -or $evidence -eq $game -or $evidence -eq $server) { throw 'Game, server, and evidence directories must differ.' }
$marker = Join-Path $game 'launch-profiler-staging.json'
if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'A marked LaunchProfiler private client stage is required.' }
$stage = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if (![string]::Equals($stage.destination,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Client marker destination mismatch.' }
$journalPath = Join-Path $evidence 'private-recovery.json'
$script:journal = [ordered]@{schemaVersion=1; game=$game; server=$server; restored=$false; entries=@(); processes=@()}
function Save-Journal {
    $script:journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($journalPath + '.tmp')
    Move-Item -LiteralPath ($journalPath + '.tmp') -Destination $journalPath -Force
}
function Preserve-Path([string] $Target, [string] $Label, [switch] $Clone) {
    $root = if ($Target.StartsWith($game + '\',[StringComparison]::OrdinalIgnoreCase)) { $game } else { $server }
    Assert-Within $Target $root
    Assert-NoLinks $Target
    $backup = Join-Path $evidence ('private-original-' + $Label)
    $entry = [ordered]@{target=$Target; backup=$backup; observed=(Join-Path $evidence ('private-observed-' + $Label)); existed=(Test-Path -LiteralPath $Target)}
    $script:journal.entries += $entry
    Save-Journal
    if ($entry.existed) {
        Move-Item -LiteralPath $Target -Destination $backup
        if ($Clone) { Copy-Item -LiteralPath $backup -Destination $Target -Recurse }
    }
    return $entry
}
function Stop-OwnedProcesses {
    $processes = @($script:journal.processes)
    [array]::Reverse($processes)
    foreach ($owned in $processes) {
        if ($owned.path -notin @((Join-Path $game 'SCPSL.exe'),(Join-Path $server 'SCPSL.exe'))) { throw 'Recovery process path is not one of the private stage executables.' }
        $process = Get-Process -Id $owned.id -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        try {
            if ($process.HasExited) { continue }
            if ($process.StartTime.ToUniversalTime().Ticks -ne [long]$owned.startUtcTicks -or
                ![string]::Equals($process.Path,$owned.path,[StringComparison]::OrdinalIgnoreCase)) {
                throw 'Recorded PID was reused; refusing to stop or restore underneath an unrelated process.'
            }
            Stop-Process -Id $process.Id
            if (!$process.WaitForExit(10000)) { throw 'Owned process did not exit; profiles remain parked for recovery.' }
        }
        catch { if (Get-Process -Id $owned.id -ErrorAction SilentlyContinue) { throw } }
    }
}
function Restore-Paths {
    # A host interruption can occur between Process.Start and durable PID save.
    # Do not kill that unrecorded process, and never restore profiles beneath it.
    foreach ($running in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
        if ($running.Path -in @((Join-Path $game 'SCPSL.exe'),(Join-Path $server 'SCPSL.exe'))) {
            throw 'A private stage is still running; stop its verified process before retrying -Recover.'
        }
    }
    $entries = @($script:journal.entries)
    [array]::Reverse($entries)
    foreach ($entry in $entries) {
        $target = [IO.Path]::GetFullPath($entry.target)
        if (!$target.StartsWith($game + '\',[StringComparison]::OrdinalIgnoreCase) -and !$target.StartsWith($server + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Recovery target escapes private stages.' }
        Assert-Within $entry.backup $evidence; Assert-Within $entry.observed $evidence
        Assert-NoLinks $target; Assert-NoLinks $entry.backup
        if ($entry.existed -and !(Test-Path -LiteralPath $entry.backup)) { continue }
        if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $entry.observed }
        if ($entry.existed) { Move-Item -LiteralPath $entry.backup -Destination $target }
    }
    $script:journal.restored = $true
    Save-Journal
}
if ($Recover) {
    if ($PrepareOnly -or !(Test-Path -LiteralPath $journalPath)) { throw 'Recover requires an existing recovery journal and cannot use PrepareOnly.' }
    $script:journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json -AsHashtable
    if ($script:journal.game -ne $game -or $script:journal.server -ne $server) { throw 'Recovery journal stage mismatch.' }
    if (!$script:journal.restored) { Stop-OwnedProcesses; Restore-Paths }
    Write-Output 'Owned processes stopped and private paths restored.'
    return
}
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory for each validation.' }
foreach ($exe in @((Join-Path $game 'SCPSL.exe'),(Join-Path $server 'SCPSL.exe'))) {
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing executable: $exe" }
    foreach ($running in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
        if ([string]::Equals($running.Path,$exe,[StringComparison]::OrdinalIgnoreCase)) { throw "Private stage already running: PID $($running.Id)" }
    }
}
$policy = Get-Content -LiteralPath (Join-Path $server 'hoster_policy.txt') -Raw
foreach ($required in @('(?im)^gamedir_for_configs:\s*true\s*$','(?im)^ipv4_bind_ip:\s*127\.0\.0\.1\s*$','(?im)^ipv6_bind_ip:\s*::1\s*$')) {
    if ($policy -notmatch $required) { throw 'Private server must already have explicit loopback-only hoster policy.' }
}
$sourceLab = Join-Path $server 'AppData/SCP Secret Laboratory/LabAPI'
foreach ($file in @($HarnessDll,$StarterDll,(Join-Path $game 'Mods/Anomaly.Client.dll'),(Join-Path $sourceLab 'plugins/global/Anomaly.Server.dll'))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing prebuilt input: $file" }
}
$clientShared = (Get-FileHash -LiteralPath (Join-Path $game 'UserLibs/Anomaly.Shared.dll') -Algorithm SHA256).Hash
$serverShared = (Get-FileHash -LiteralPath (Join-Path $sourceLab 'dependencies/global/Anomaly.Shared.dll') -Algorithm SHA256).Hash
if ($clientShared -ne $serverShared) { throw 'Client and server Anomaly.Shared must be the identical matching contract build. Prepare a current private server package before validation.' }
$plugins = @(Get-ChildItem -LiteralPath (Join-Path $sourceLab 'plugins') -File -Filter '*.dll' -Recurse)
if ($plugins.Count -ne 1 -or $plugins[0].Name -ne 'Anomaly.Server.dll') { throw 'Server fixture must contain only Anomaly.Server; unrelated plugins are not part of this check.' }
$profile = Get-Content -LiteralPath (Join-Path $game 'UserData/Anomaly/user.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($profile.username)) { throw 'Complete the private fixture nickname once before validation; ConnectionOnly does not fill first-run nicknames.' }
$clientPublic = [Convert]::FromBase64String($profile.identity_public)
if ($clientPublic.Length -ne 65) { throw 'Private client identity public key must contain 65 bytes.' }
$variant = $null
if ($VariantManifest) {
    $variant = Get-Content -LiteralPath $VariantManifest -Raw | ConvertFrom-Json
    if ($variant.variant -notin @('identity-once','unchanged-module-lock') -or !$variant.buildSucceeded -or !$variant.testsSucceeded) { throw 'Variant requires a supported built and tested binary manifest.' }
    if ((Get-FileHash -LiteralPath $variant.clientDll -Algorithm SHA256).Hash -ne $variant.clientSha256) { throw 'Candidate hash does not match variant manifest.' }
}
function Get-Manifest([string] $Root, [string[]] $Paths) {
    @($Paths | ForEach-Object {
        $path = Join-Path $Root $_
        if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Item -LiteralPath $path }
        elseif (Test-Path -LiteralPath $path -PathType Container) { Get-ChildItem -LiteralPath $path -File -Recurse }
    } | Sort-Object FullName -Unique | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($Root,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$originalInputs = Get-Manifest $game @('Mods','Plugins','UserLibs')
$sourceServerInputs = Get-Manifest $server @('SCPSL.exe','SCPSL_Data/Managed/Assembly-CSharp.dll','AppData/SCP Secret Laboratory/LabAPI/plugins','AppData/SCP Secret Laboratory/LabAPI/dependencies')
# This is the server-specific HMAC enrollment ID used by ServerIdentityStore,
# never a hash of the bare client key. The secret is never printed or put in the report.
$ownerId = $null
if ($DedicatedDummyControl) {
    if (!$ServerIdentityFile) { throw 'DedicatedDummyControl requires an explicit private ServerIdentityFile from this server fixture.' }
    $ServerIdentityFile = [IO.Path]::GetFullPath($ServerIdentityFile)
    Assert-Within $ServerIdentityFile $sourceLab
    $identityText = Get-Content -LiteralPath $ServerIdentityFile -Raw
    $secretMatch = [regex]::Match($identityText,'(?m)^enrollment_secret_base64:\s*([A-Za-z0-9+/=]+)\s*$')
    if (!$secretMatch.Success) { throw 'A current server identity with enrollment_secret_base64 is required; legacy migration is outside this harness.' }
    $secret = [Convert]::FromBase64String($secretMatch.Groups[1].Value)
    if ($secret.Length -notin @(32,97)) { throw 'Unsupported server enrollment secret length.' }
    $hmac = [Security.Cryptography.HMACSHA256]::new($secret)
    try { $digest = $hmac.ComputeHash([byte[]]([Text.Encoding]::ASCII.GetBytes('ANOMALY-ENROLLMENT-V1') + $clientPublic)) }
    finally { $hmac.Dispose(); [Array]::Clear($secret,0,$secret.Length) }
    $number = [Numerics.BigInteger]::Zero
    foreach ($value in $digest) { $number = $number * 256 + $value }
    $encoded = ''; $alphabet = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz'
    while ($number -gt 0) { $encoded = $alphabet[[int]($number % 58)] + $encoded; $number = [Numerics.BigInteger]::Divide($number,58) }
    foreach ($value in $digest) { if ($value -ne 0) { break }; $encoded = '1' + $encoded }
    $ownerId = $encoded + '@anomaly'
}
New-Item -ItemType Directory -Path $evidence | Out-Null
$harnessInputs = @(@{path=[IO.Path]::GetFullPath($HarnessDll);sha256=(Get-FileHash -LiteralPath $HarnessDll -Algorithm SHA256).Hash.ToLowerInvariant()},@{path=[IO.Path]::GetFullPath($StarterDll);sha256=(Get-FileHash -LiteralPath $StarterDll -Algorithm SHA256).Hash.ToLowerInvariant()})
$plan = [ordered]@{schemaVersion=1; game=$game; server=$server; port=$Port; mode=$(if($DedicatedDummyControl){'DedicatedDummyControl'}else{'ConnectionOnly'}); prepareOnly=[bool]$PrepareOnly; offlineGenerator=[bool]$OfflineGenerator; originalInputs=$originalInputs; serverInputs=$sourceServerInputs; harnessInputs=$harnessInputs; variant=$variant; fixtureOwnerAssigned=[bool]$DedicatedDummyControl; limitations=@('No launch timing certification','Scripted API/EventSystem input; physical input untested','Visual screenshot review required','Profiler runtime-only experiments are not active','Existing harness does not certify stock warning or menu raycast readiness'); containsPrivateProfiles=$true}
$plan | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'validation-plan.json')
if ($PrepareOnly) { Write-Output "Validated inputs without launching or changing either stage. Plan: $evidence"; return }
$client = $null; $serverProcess = $null; $failure = $null
function Start-Owned([string] $Exe, [string] $WorkingDirectory, [string[]] $Arguments, [hashtable] $EnvironmentVariables) {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.WorkingDirectory = $WorkingDirectory; $info.UseShellExecute = $false; $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($key in @($info.Environment.Keys)) {
        if ($key.StartsWith('ANOMALY_') -and ($key.Contains('SMOKE') -or $key.StartsWith('ANOMALY_LAUNCH_PROFILE_'))) { $info.Environment.Remove($key) | Out-Null }
    }
    foreach ($key in $EnvironmentVariables.Keys) { $info.Environment[$key] = $EnvironmentVariables[$key] }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $script:journal.processes += [ordered]@{id=$process.Id; path=$Exe; startUtcTicks=$process.StartTime.ToUniversalTime().Ticks}
    Save-Journal
    return $process
}
try {
    Save-Journal
    Preserve-Path (Join-Path $game 'UserData') 'client-UserData' -Clone | Out-Null
    Preserve-Path (Join-Path $game 'ProfileAppData') 'client-ProfileAppData' -Clone | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $game 'ProfileAppData') -Force | Out-Null
    Preserve-Path (Join-Path $game 'Plugins/Anomaly.LaunchProfiler.dll') 'profiler.dll' | Out-Null
    foreach ($item in @(@{source=$HarnessDll; name='Anomaly.EarlyAccessUiSmoke.dll'},@{source=$StarterDll; name='HelloAnomalyMod.dll'})) {
        $target = Join-Path $game ('Mods/' + $item.name)
        Preserve-Path $target $item.name | Out-Null
        Copy-Item -LiteralPath $item.source -Destination $target
    }
    if ($variant) {
        $target = Join-Path $game 'Mods/Anomaly.Client.dll'
        Preserve-Path $target 'client-candidate.dll' | Out-Null
        Copy-Item -LiteralPath $variant.clientDll -Destination $target
    }
    $approved = @(Get-ChildItem -LiteralPath (Join-Path $game 'Mods') -File -Filter '*.dll' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    @{schemaVersion=1;mode='enforce';approvedSha256=$approved} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $game 'UserData/Anomaly/mod-trust.json')
    $candidateInputs = Get-Manifest $game @('Mods','UserLibs')
    $candidateInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'candidate-inputs.json')
    $serverData = Preserve-Path (Join-Path $server 'AppData') 'server-AppData'
    $newLab = Join-Path $server 'AppData/SCP Secret Laboratory/LabAPI'
    New-Item -ItemType Directory -Path $newLab -Force | Out-Null
    $oldLab = Join-Path $serverData.backup 'SCP Secret Laboratory/LabAPI'
    foreach ($folder in @('plugins','dependencies')) { Copy-Item -LiteralPath (Join-Path $oldLab $folder) -Destination $newLab -Recurse }
    $anomalyConfig = Join-Path $newLab "configs/$Port/Anomaly"
    New-Item -ItemType Directory -Path $anomalyConfig -Force | Out-Null
    @('content_pack_directory: ''''','require_password: false','enable_legacy_password_auth_on_loopback: false') | Set-Content -LiteralPath (Join-Path $anomalyConfig 'config.yml')
    if ($DedicatedDummyControl) { Copy-Item -LiteralPath (Join-Path $serverData.backup ([IO.Path]::GetRelativePath((Join-Path $server 'AppData'),$ServerIdentityFile))) -Destination (Join-Path $anomalyConfig 'server_identity.yml') }
    $configuration = Join-Path $server "AppData/config/$Port"
    New-Item -ItemType Directory -Path $configuration -Force | Out-Null
    @('server_name: Anomaly private launch candidate validation','ipv4_bind_ip: 127.0.0.1','ipv6_bind_ip: ::1','server_ip: 127.0.0.1','online_mode: false','forward_ports: false','enable_query: false','contact_email: default','max_players: 4') | Set-Content -LiteralPath (Join-Path $configuration 'config_gameplay.txt')
    $permissions = @('Members:')
    if ($DedicatedDummyControl) { $permissions += " - ${ownerId}: owner" }
    $permissions += @('enable_staff_access: false','enable_manager_access: false','enable_banteam_access: false','enable_banteam_reserved_slots: false','enable_banteam_bypass_geoblocking: false','owner_badge: FIXTURE OWNER','owner_color: red','owner_cover: true','owner_hidden: false','owner_kick_power: 255','owner_required_kick_power: 255','Roles:',' - owner','Permissions:',' - ForceclassSelf: [owner]',' - ForceclassWithoutRestrictions: [owner]',' - RoundEvents: [owner]',' - GameplayData: [owner]',' - PlayersManagement: [owner]',' - FacilityManagement: [owner]',' - Noclip: [owner]','override_password: none','allow_central_server_commands_as_ServerConsoleCommands: false')
    $permissions | Set-Content -LiteralPath (Join-Path $configuration 'config_remoteadmin.txt')
    $serverArguments = @('-batchmode','-nographics','-stdout',"-port$Port",'-appdatapath',(Join-Path $server 'AppData'),'-configpath',$configuration,'-logFile',(Join-Path $evidence 'server-player.log'))
    $serverProcess = Start-Owned (Join-Path $server 'SCPSL.exe') $server $serverArguments @{}
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        if ($serverProcess.HasExited) { throw 'Private server exited during startup.' }
        $listeners = @(Get-NetUDPEndpoint -OwningProcess $serverProcess.Id -ErrorAction SilentlyContinue)
        $gameListeners = @($listeners | Where-Object LocalPort -eq $Port)
        if ($gameListeners.Count) {
            foreach ($listener in $gameListeners) { if (![Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($listener.LocalAddress))) { throw 'Server game port is not bound exclusively to loopback.' } }
            break
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Server did not expose its owned loopback listener within 120 seconds.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
    $gameListeners | Select-Object LocalAddress,LocalPort,OwningProcess | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'server-listeners.json')
    $environment = @{ANOMALY_UI_SMOKE_DIR=$evidence;ANOMALY_SMOKE_SERVER="127.0.0.1:$Port"}
    if ($DedicatedDummyControl) { $environment.ANOMALY_DEDICATED_DUMMY_SMOKE='1'; $environment.ANOMALY_DUMMY_SCREEN_WIDTH=[string]$ScreenWidth; $environment.ANOMALY_DUMMY_SCREEN_HEIGHT=[string]$ScreenHeight }
    else { $environment.ANOMALY_UI_SMOKE_CONNECTION_ONLY='1' }
    $arguments = @('-noauth','-force-d3d11','-screen-fullscreen','0','-screen-width',[string]$ScreenWidth,'-screen-height',[string]$ScreenHeight,'-appdatapath',(Join-Path $game 'ProfileAppData'),'-logFile',(Join-Path $evidence 'player.log'))
    if ($OfflineGenerator) { $arguments += '--melonloader.agfoffline' }
    @{clientArguments=$arguments;serverArguments=$serverArguments;environment=$environment} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-arguments.json')
    $client = Start-Owned (Join-Path $game 'SCPSL.exe') $game $arguments $environment
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$client.HasExited) {
        if (Test-Path -LiteralPath (Join-Path $evidence 'failure.txt')) { throw 'Gameplay harness reported failure; see failure.txt.' }
        if ($serverProcess.HasExited) { throw 'Private server exited before client completion.' }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Gameplay validation exceeded $TimeoutSeconds seconds." }
        Start-Sleep -Milliseconds 500
    }
    if ($client.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $evidence 'result.txt')) -or (Test-Path -LiteralPath (Join-Path $evidence 'failure.txt'))) { throw 'Client exited without a successful harness result.' }
    $expectedResult = if ($DedicatedDummyControl) { 'Dedicated dummy control completed;' } else { 'UI smoke passed;' }
    if (!(Get-Content -LiteralPath (Join-Path $evidence 'result.txt') -Raw).StartsWith($expectedResult,[StringComparison]::Ordinal)) { throw 'Harness result does not match the requested scenario.' }
    $after = Get-Manifest $game @('Mods','UserLibs')
    if (($after | ConvertTo-Json -Depth 5 -Compress) -ne ($candidateInputs | ConvertTo-Json -Depth 5 -Compress)) { throw 'Candidate binaries changed during gameplay validation.' }
}
catch { $failure = $_.ToString(); $failure | Set-Content -LiteralPath (Join-Path $evidence 'runner-failure.txt') }
finally {
    Stop-OwnedProcesses
    Restore-Paths
    $restoredInputs = Get-Manifest $game @('Mods','Plugins','UserLibs')
    $restored = ($restoredInputs | ConvertTo-Json -Depth 5 -Compress) -eq ($originalInputs | ConvertTo-Json -Depth 5 -Compress)
    $restoredServerInputs = Get-Manifest $server @('SCPSL.exe','SCPSL_Data/Managed/Assembly-CSharp.dll','AppData/SCP Secret Laboratory/LabAPI/plugins','AppData/SCP Secret Laboratory/LabAPI/dependencies')
    $serverRestored = ($restoredServerInputs | ConvertTo-Json -Depth 5 -Compress) -eq ($sourceServerInputs | ConvertTo-Json -Depth 5 -Compress)
    [ordered]@{harnessPassed=(!$failure); restored=$restored; serverRestored=$serverRestored; profilesRestored=$script:journal.restored; requiresVisualReview=$true; mode=$plan.mode} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'validation-result.json')
    if (!$restored) { throw 'Original candidate/loadout hashes were not restored; inspect private-recovery.json.' }
    if (!$serverRestored) { throw 'Original private server binary hashes were not restored; inspect private-recovery.json.' }
}
if ($failure) { throw $failure }
Write-Output "Gameplay harness completed; private stages restored. Review screenshots in $evidence before certifying visuals."
