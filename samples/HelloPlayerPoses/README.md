# Hello Player Poses

An ordinary LabAPI companion referencing the matching Anomaly server SDK. No
SL-VR client mod is required to view this example; viewers need the base Anomaly
client with `anomaly:player-pose-renderer` version 1.

Build with `dotnet build samples/HelloPlayerPoses/HelloPlayerPoses.csproj -c Debug -p:DeployLocal=false`.
In an extracted SDK, set `AnomalyServerGameDir` to the matching dedicated server.
Install only the resulting `HelloPlayerPoses.dll` through the normal LabAPI plugin
workflow. The build never deploys it.

From the **server console**, use:

```text
poseexample <player-id> start
poseexample <player-id> overlap
poseexample <player-id> overlap
poseexample <player-id> clear
```

Start streams a modest head rotation at 20 Hz using a 500 ms lease. Overlap adds
an independently owned higher-priority contribution at 65% animation blend; a
second overlap releases it and reveals the original contribution. Clear closes
the handles. Role changes and timeout close them automatically, and disabling
the plugin disposes both scopes. Local-player rendering remains off.

The example derives its initial model-relative head orientation from the stock
server model. A tracked-body producer instead captures its solved client bones
with `Anomaly.Client.Api.Features.PlayerPoseRigs.TryCapture`, uploads through its
own authenticated contract, validates that input in its LabAPI companion, and
publishes the resulting frame. Never treat client-authored pose data as movement,
hitbox, or combat authority.
