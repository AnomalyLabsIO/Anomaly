# Hello Anomaly: ordinary LabAPI server plugin

This `net48` plugin references `Anomaly.Server.dll` and uses native LabAPI players,
configuration, lifecycle and events. Anomaly supplies client-aware features;
LabAPI remains the plugin framework.

Build from this directory using an extracted matching SDK and dedicated-server
references (the source checkout supplies defaults):

```powershell
dotnet build -c Release -p:DeployLocal=false `
  -p:AnomalySdkRoot="X:\SDKs\Anomaly" `
  -p:AnomalyServerGameDir="X:\Servers\SCPSL"
```

Keep `Anomaly.Server.Sample.props` and `HelloMessages.cs` in the parent samples directory. Install only
`HelloAnomalyServer.dll` into LabAPI's `plugins/global` directory. Install the
matching Anomaly server package normally; do not copy its DLL or host reference
DLLs into this plugin's output folder for distribution.

The sample registers an item during `Enable()`, waits for the client's exact
content catalog to activate, and grants the item with `TryGiveItem`. Declining
required content disconnects the client; it does not admit a content-free player
or trigger the grant. Item use is handled by native `PlayerEvents.UsedItem`; no
Anomaly item-logic subclass is required.

With the matching `HelloAnomalyMod` installed on the client, run
`example.hello.server` after connecting. Both plugins register the contracts in
`../HelloMessages.cs`; the server receives the authenticated request through
`ClientEvents.MessageReceived`, queries `CustomItems.GetPlayerReadiness`, and
uses `ClientMessaging.TrySend` to reply. Messaging requires current authenticated
admission; declining required content disconnects before application messaging
becomes available. The client command waits for enrollment disclosure; the
transport still checks admission when sending and receiving.

To require the matching client mod at connection time, set
`RequireClientCompanion: true` in this sample plugin's LabAPI config. The sample
registers `example.hello` in the inclusive `0.1.0` to exclusive `0.2.0` range
during `Enable()` and disposes the requirement during `Disable()`. Without that
setting, the companion remains optional as before. Missing or incompatible
versions produce a detailed client-facing rejection.

The companion does not initiate custom traffic to clients without a request.
Install the matching companion before using the client command. Its message
subscriptions, like native item handlers, are removed during `Disable()`.

`EnableScriptPack` defaults to `false`. Set `EnableScriptPack: true` to offer the
embedded Lua pack to connecting clients. Players need Windows with remote script
support available and must approve both the content download and script execution.
A client that cannot run or declines a required pack cannot join. Keep the full
matching client package installed; the server setting cannot enable script
support on an unsupported client.

Keep the complete [`ContentPacks/example/panel`](ContentPacks/example/panel) directory beside the
project when building. Its [`pack.json`](ContentPacks/example/panel/pack.json)
declares wrapper `example:panel`, diagnostic version `1.0.0`, script component
`example:panel_scripts`, one entrypoint `scripts/main.lua` and the optional
`scripts/messages.json` schemas. The exact embedded CLR names are:

- `HelloAnomalyServer.Panel/pack.json`
- `HelloAnomalyServer.Panel/scripts/main.lua`
- `HelloAnomalyServer.Panel/scripts/messages.json`

The project declares explicit `LogicalName` values; `ContentPacks.RegisterEmbedded(
typeof(Plugin).Assembly, "HelloAnomalyServer.Panel")` captures declared bounded
bytes without resource scanning or dotted-name conversion. Anomaly generates
the script index. The intended exchange after permitted activation sends
`GetState` at ContentReady and replies with `State`, both using component ID
`example:panel_scripts` through the existing authenticated messaging channel.
No client companion mod is required. See the [content-pack guide](../../Anomaly.Client.Scripting/SCRIPT-PACKS.md)
for separate consent, the descriptor, explicit assets and typed `Data.Items`.
Declining required content or execution closes the connection.

For the supported local path, copy the complete pack directory, preserving
`pack.json` and its `scripts` subdirectory, to
`<UserData>/Anomaly/ContentPacks/example/panel/` and start a nondedicated host.
The same Lua logs initialization and displays local player/round observations
without a client companion. Its message handler is installed, but local activation
zero cannot exchange pack messages; use the qualified remote path to exercise them.
Enabling the server registration does not select the local owner entry point.

Disabling the plugin removes its registration from future offers, not from an
already captured connection snapshot. Source/message authority and script-owned
resources belong to the exact admitted offer until offer replacement or disconnect;
registration disposal is not immediate client revocation. The plugin's own
server subscriptions are removed during `Disable()`.

Register all custom definitions before the first catalog is offered. Registration
copies the definition. The catalog's IDs remain fixed for that server session;
restart the server after changing item definitions or the participating plugins.
`Disable()` removes subscriptions but does not invalidate active client catalogs.
All feature calls and event handling run on the Unity game thread.

The sample also subscribes to `MapEvents.LayoutFinalizing`. Leave
`EnableFacilityExample` false for normal use. On a test server, enable it and
select `FacilityExampleRoom` to inspect the complete chosen layout and edit the
first included matching room. With `RemoveFacilityExampleRoom` false it adds
`FacilityExampleOffsetX` to the room's final position; with true it removes the
room and its vanilla contents. The example does not supply replacement geometry
or repair disconnected routes, so choose edits appropriate to your test map.
Restart for configuration changes. The handler runs before the final layout is
frozen for clients and before native `MapGenerated`; saved descriptors cannot be
edited later. See [facility generation](../../Anomaly.Server/MODDING.md#facility-generation)
for ordering, lifetime, and synchronization rules.

Build server plugins against the matching SDK. The source namespace is
`Anomaly.Server.Api`, while the distributable assembly remains `Anomaly.Server.dll`.
