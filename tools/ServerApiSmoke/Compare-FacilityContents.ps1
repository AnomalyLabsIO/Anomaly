[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ServerCapture,
    [Parameter(Mandatory=$true)] [string] $ClientCapture,
    [string] $BaselineCapture,
    [string] $NativeServerCapture,
    [string] $NativeClientCapture,
    [switch] $CompareNativeLabels,
    [string[]] $RemovedRoomKeys = @(),
    [double] $PositionTolerance = 0.001,
    [double] $AngularToleranceDegrees = 0.1,
    [string] $OutputPath
)
$ErrorActionPreference = 'Stop'
$script:checkCount = 0
$script:maximumPositionDifference = 0.0
$script:maximumAngularDifference = 0.0
function Require([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
    $script:checkCount++
}
function Read-Capture([string] $Path) {
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Index-Rooms($Capture) {
    $index = @{}
    foreach ($room in $Capture.Rooms) {
        Require (!$index.ContainsKey($room.Key)) "Repeated room key $($room.Key)"
        $index.Add($room.Key, $room)
    }
    return $index
}
function Compare-Labels($Actual, $Expected, [string] $Label) {
    Require (@($Actual.Labels).Count -eq @($Expected.Labels).Count) "$Label has matching LCZ label count"
    $actualLabels = @{}
    $expectedLabels = @{}
    foreach ($entry in $Actual.Labels) {
        # Native cleanup changes sibling indices. Keep the entire name ancestry
        # and reject duplicate canonical paths rather than guessing correspondence.
        $key = $entry.Path -creplace '\[\d+\](?=/|$)', ''
        Require (!$actualLabels.ContainsKey($key)) "$Label has unique actual LCZ label name path $key"
        $actualLabels.Add($key, $entry)
    }
    foreach ($entry in $Expected.Labels) {
        $key = $entry.Path -creplace '\[\d+\](?=/|$)', ''
        Require (!$expectedLabels.ContainsKey($key)) "$Label has unique expected LCZ label name path $key"
        $expectedLabels.Add($key, $entry)
        Require ($actualLabels.ContainsKey($key)) "$Label contains LCZ label name path $key"
        foreach ($field in @('CharacterRendererPresent','NumberRendererPresent','CharacterMaterial','NumberMaterial')) {
            Require ($actualLabels[$key].$field -ceq $entry.$field) "$Label LCZ label $key $field matches"
        }
    }
}
function Compare-Pose($Actual, $Expected, [string] $Label) {
    Require ($null -ne $Actual.Position -and $null -ne $Expected.Position -and
        $Actual.Position.Count -eq 3 -and $Expected.Position.Count -eq 3) "$Label has two positions"
    for ($axis = 0; $axis -lt 3; $axis++) {
        $difference = [Math]::Abs([double]$Actual.Position[$axis] - [double]$Expected.Position[$axis])
        $script:maximumPositionDifference = [Math]::Max($script:maximumPositionDifference, $difference)
        Require ($difference -le $PositionTolerance) "$Label position axis $axis differs by $difference"
    }
    Require ($null -ne $Actual.Rotation -and $null -ne $Expected.Rotation -and
        $Actual.Rotation.Count -eq 4 -and $Expected.Rotation.Count -eq 4) "$Label has two quaternions"
    $dot = 0.0
    $actualNorm = 0.0
    $expectedNorm = 0.0
    for ($axis = 0; $axis -lt 4; $axis++) {
        $a = [double]$Actual.Rotation[$axis]
        $b = [double]$Expected.Rotation[$axis]
        $dot += $a * $b
        $actualNorm += $a * $a
        $expectedNorm += $b * $b
    }
    Require ($actualNorm -gt 0 -and $expectedNorm -gt 0 -and
        ![double]::IsInfinity($actualNorm) -and ![double]::IsInfinity($expectedNorm)) "$Label has finite nonzero quaternions"
    # Normalize float32 quaternions before measuring the angle; q and -q are the same rotation.
    $cosHalfAngle = [Math]::Min(1.0, [Math]::Abs($dot) / [Math]::Sqrt($actualNorm * $expectedNorm))
    $angle = 2.0 * [Math]::Acos($cosHalfAngle) * 180.0 / [Math]::PI
    $script:maximumAngularDifference = [Math]::Max($script:maximumAngularDifference, $angle)
    Require ($angle -le $AngularToleranceDegrees) "$Label differs by $angle degrees (limit $AngularToleranceDegrees)"
}

Require ($PositionTolerance -gt 0 -and ![double]::IsInfinity($PositionTolerance) -and
    $AngularToleranceDegrees -gt 0 -and ![double]::IsInfinity($AngularToleranceDegrees)) 'Pose tolerances must be finite and positive'
$server = Read-Capture $ServerCapture
$client = Read-Capture $ClientCapture
Require ($server.SchemaVersion -eq 2 -and $client.SchemaVersion -eq 2) 'Both content captures use schema version 2'
Require ($server.MapGenerated -and $client.MapGenerated) 'Both maps completed generation before content capture'
Require ($server.Seed -eq $client.Seed) 'Content captures have the same seed'
Require ($server.RoomCount -eq @($server.Rooms).Count -and $client.RoomCount -eq @($client.Rooms).Count) 'Recorded room counts match the captures'
Require ($server.RoomCount -eq $client.RoomCount) 'Server and client room counts match'
Require ($server.IdentityCount -eq $client.IdentityCount) 'Server and client owned-identity counts match'
$serverRooms = Index-Rooms $server
$clientRooms = Index-Rooms $client
$identities = 0
$labels = 0
foreach ($key in $serverRooms.Keys) {
    Require ($clientRooms.ContainsKey($key)) "Client contains room $key"
    $remote = $serverRooms[$key]
    $local = $clientRooms[$key]
    foreach ($field in @('Zone','X','Y','Name','Shape','DuplicateId')) {
        Require ($local.$field -ceq $remote.$field) "Room $key metadata $field matches"
    }
    Compare-Pose $local $remote "Room $key"
    Compare-Labels $local $remote "Room $key"
    $labels += @($remote.Labels).Count
    Require (@($local.Identities).Count -eq @($remote.Identities).Count) "Room $key owns the same identity count"
    for ($index = 0; $index -lt @($remote.Identities).Count; $index++) {
        $a = $local.Identities[$index]
        $b = $remote.Identities[$index]
        $label = "Room $key identity $index ($($b.Path))"
        Require ($a.Index -eq $index -and $b.Index -eq $index) "$label retains its original identity ordinal"
        Require ($a.PathFromPrefab -and $b.PathFromPrefab) "$label uses a prefab-owned stable path"
        foreach ($field in @('Path','OriginalAssetId','AssetId','ActualAssetId','NetId','Present','HasNetworkIdentity','Spawned','HasStructurePositionSync','StructureYawCode')) {
            Require ($a.$field -ceq $b.$field) "$label $field matches"
        }
        if ($b.Present) {
            Require ($a.HasNetworkIdentity -and $a.Spawned -and $a.NetId -ne 0) "$label is registered after ready"
            Require ($a.ActualAssetId -eq $a.AssetId -and $b.ActualAssetId -eq $b.AssetId) "$label uses the planned asset ID"
            Compare-Pose $a $b $label
        }
        $identities++
    }
}
Require ($identities -eq $server.IdentityCount) 'Captured identity totals match the traversed identities'
Require ($server.ConnectorCount -eq @($server.Connectors).Count -and $client.ConnectorCount -eq @($client.Connectors).Count) 'Recorded connector counts match the captures'
Require ($server.ConnectorCount -eq $client.ConnectorCount) 'Server and client spawned connector counts match'
$clientConnectors = @{}
foreach ($connector in $client.Connectors) {
    $key = [string]$connector.NetId
    Require (!$clientConnectors.ContainsKey($key)) "Client connector netId $key is unique"
    $clientConnectors.Add($key, $connector)
}
$serverConnectorIds = @{}
foreach ($remote in $server.Connectors) {
    $key = [string]$remote.NetId
    Require (!$serverConnectorIds.ContainsKey($key)) "Server connector netId $key is unique"
    $serverConnectorIds.Add($key, $true)
    Require ($clientConnectors.ContainsKey($key)) "Client contains spawned connector $key"
    $local = $clientConnectors[$key]
    foreach ($field in @('Name','ConnectorType','HasNetworkIdentity','NetId','AssetId','Spawned','HasDoor','DoorRoomsRegistered')) {
        Require ($local.$field -ceq $remote.$field) "Connector $key $field matches"
    }
    Require ($local.HasNetworkIdentity -and $local.Spawned -and $local.NetId -ne 0) "Connector $key is registered on both peers"
    Compare-Pose $local $remote "Connector $key ($($remote.Name))"
    Require ((@($local.DoorRooms | Sort-Object -Unique) -join "`n") -ceq
        (@($remote.DoorRooms | Sort-Object -Unique) -join "`n")) "Connector $key native door room associations match"
}
$remoteEdges = @($server.Edges | ForEach-Object { "$($_.From)>$($_.To)" } | Sort-Object -Unique)
$localEdges = @($client.Edges | ForEach-Object { "$($_.From)>$($_.To)" } | Sort-Object -Unique)
$serverOnlyEdges = @($remoteEdges | Where-Object { $localEdges -cnotcontains $_ })
$clientOnlyEdges = @($localEdges | Where-Object { $remoteEdges -cnotcontains $_ })
$nativeCacheBaselineMatched = $false
if ($serverOnlyEdges.Count -gt 0 -or $clientOnlyEdges.Count -gt 0 -or $CompareNativeLabels) {
    Require (![string]::IsNullOrEmpty($NativeServerCapture) -and ![string]::IsNullOrEmpty($NativeClientCapture)) 'Generation-time ConnectedRooms cache differs; supply pristine native captures to qualify known stock cache omissions'
    $nativeServer = Read-Capture $NativeServerCapture
    $nativeClient = Read-Capture $NativeClientCapture
    Require ($nativeServer.SchemaVersion -eq 2 -and $nativeClient.SchemaVersion -eq 2 -and
        $nativeServer.MapGenerated -and $nativeClient.MapGenerated) 'Native cache baselines are completed schema 2 captures'
    Require ($nativeServer.Seed -eq $server.Seed -and $nativeClient.Seed -eq $server.Seed) 'Native cache baselines use the current seed'
    $nativeServerEdges = @($nativeServer.Edges | ForEach-Object { "$($_.From)>$($_.To)" } | Sort-Object -Unique)
    $nativeClientEdges = @($nativeClient.Edges | ForEach-Object { "$($_.From)>$($_.To)" } | Sort-Object -Unique)
    $nativeServerOnly = @($nativeServerEdges | Where-Object { $nativeClientEdges -cnotcontains $_ })
    $nativeClientOnly = @($nativeClientEdges | Where-Object { $nativeServerEdges -cnotcontains $_ })
    foreach ($edge in $serverOnlyEdges) {
        Require ($nativeServerOnly -ccontains $edge) "Server-only cache edge $edge is also absent in the pristine native client"
    }
    foreach ($edge in $clientOnlyEdges) {
        Require ($nativeClientOnly -ccontains $edge) "Client-only cache edge $edge is also absent in the pristine native server"
    }
    $nativeCacheBaselineMatched = $true
    if ($CompareNativeLabels) {
        $nativeServerRooms = Index-Rooms $nativeServer
        $nativeClientRooms = Index-Rooms $nativeClient
        Require ($serverRooms.Count -eq $nativeServerRooms.Count -and $clientRooms.Count -eq $nativeClientRooms.Count) 'No-op label comparison has the same native room counts'
        foreach ($key in $serverRooms.Keys) {
            Require ($nativeServerRooms.ContainsKey($key) -and $nativeClientRooms.ContainsKey($key)) "No-op room $key exists in both pristine native captures"
            Compare-Labels $serverRooms[$key] $nativeServerRooms[$key] "Server no-op room $key"
            Compare-Labels $clientRooms[$key] $nativeClientRooms[$key] "Client no-op room $key"
        }
    }
}

$removedIdentities = 0
if ($RemovedRoomKeys.Count -gt 0) {
    Require (![string]::IsNullOrEmpty($BaselineCapture)) 'Removed-room checks require a matching original content capture'
    $baseline = Read-Capture $BaselineCapture
    Require ($baseline.Seed -eq $server.Seed) 'Removal baseline uses the current seed'
    $baselineRooms = Index-Rooms $baseline
    foreach ($key in $RemovedRoomKeys) {
        Require ($baselineRooms.ContainsKey($key)) "Removed room $key exists in the baseline"
        Require (!$serverRooms.ContainsKey($key) -and !$clientRooms.ContainsKey($key)) "Removed room $key is absent on both peers"
        foreach ($identity in $baselineRooms[$key].Identities) {
            Require ($server.SpawnedAssetIds -notcontains $identity.AssetId -and
                $client.SpawnedAssetIds -notcontains $identity.AssetId) "Removed room $key asset $($identity.AssetId) is absent from both live network registries"
            $removedIdentities++
        }
        Require (@($server.Edges | Where-Object { $_.From -eq $key -or $_.To -eq $key }).Count -eq 0 -and
            @($client.Edges | Where-Object { $_.From -eq $key -or $_.To -eq $key }).Count -eq 0) "Removed room $key has no remaining atlas connectivity"
        Require (@($server.Connectors | Where-Object { $_.DoorRooms -contains $key }).Count -eq 0 -and
            @($client.Connectors | Where-Object { $_.DoorRooms -contains $key }).Count -eq 0) "Removed room $key has no remaining spawned connector associations"
    }
}
$report = [ordered]@{
    passed = $true; checks = $script:checkCount; rooms = $server.RoomCount; identities = $identities; lczLabels = $labels
    connectors = $server.ConnectorCount; serverDirectedAtlasEdges = $remoteEdges.Count; clientDirectedAtlasEdges = $localEdges.Count
    serverOnlyCacheEdges = $serverOnlyEdges; clientOnlyCacheEdges = $clientOnlyEdges
    nativeCacheBaselineMatched = $nativeCacheBaselineMatched; removedIdentities = $removedIdentities
    nativeNoOpLabelsCompared = [bool]$CompareNativeLabels
    labelPathComparison = 'unique full name ancestry without mutable sibling indices'
    positionTolerance = $PositionTolerance; angularToleranceDegrees = $AngularToleranceDegrees
    maximumPositionDifference = $script:maximumPositionDifference
    maximumAngularDifferenceDegrees = $script:maximumAngularDifference
    serverCapture = [IO.Path]::GetFullPath($ServerCapture); clientCapture = [IO.Path]::GetFullPath($ClientCapture)
    nativeServerCapture = $NativeServerCapture; nativeClientCapture = $NativeClientCapture
    excluded = @('active flags', 'runtime hierarchy paths', 'renderers', 'colliders', 'animated nonidentity children', 'non-atlas edges')
}
if ($OutputPath) { $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
Write-Output "Passed $($script:checkCount) facility content assertions: $($server.RoomCount) rooms, $identities owned identities, $($server.ConnectorCount) spawned connectors, $($remoteEdges.Count)/$($localEdges.Count) server/client cache edges, $removedIdentities removed identities."
