[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$marker = Get-Content -LiteralPath (Join-Path $game 'launch-profiler-staging.json') -Raw | ConvertFrom-Json
if (![string]::Equals($marker.destination,$game,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only a matching private profiler stage is supported.' }
foreach ($process in Get-Process -Name SCPSL -ErrorAction SilentlyContinue) {
    if ([string]::Equals($process.Path,(Join-Path $game 'SCPSL.exe'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Configure the private display while the game is stopped.' }
}
# Match SettingsKeyGenerator's stable ushort type hash and least-significant
# nibble-first key encoding, and PlayerPrefsSl's Int (07) prefix. Only the
# private appdatapath registry is touched. Stock settings consume this normally.
$hash = 23
foreach ($character in 'DisplayVideoSetting'.ToCharArray()) { $hash = (($hash * 31) + [int]$character) -band 65535 }
$digits = $hash.ToString('X4').ToCharArray()
[Array]::Reverse($digits)
$key = '07UserSettings_' + (-join $digits) + '_2000'
$path = Join-Path $game 'ProfileAppData/registry.txt'
[IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
$backup = Join-Path $game ('registry-before-windowed-' + [Guid]::NewGuid().ToString('N') + '.txt')
$lines = if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $backup; @(Get-Content -LiteralPath $path) } else { @() }
$lines = @($lines | Where-Object { !$_.StartsWith($key + '::-%(|::',[StringComparison]::Ordinal) })
$lines += $key + '::-%(|::3'
[IO.File]::WriteAllLines($path,$lines,[Text.UTF8Encoding]::new($false))
[ordered]@{schemaVersion=1;key=$key;fullscreenMode=3;meaning='Windowed; stock resolution/default selection retained';registry=$path;backup=$backup;configuredUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $game 'display-fixture.json')
Write-Output "Configured private stock Windowed mode: $game"
