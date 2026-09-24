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
content catalog to activate, and grants the item with `TryGiveItem`. Skipping
content never triggers that grant. Item use is handled by native
`PlayerEvents.UsedItem`; no Anomaly item-logic subclass is required.

With the matching `HelloAnomalyMod` installed on the client, run
`example.hello.server` after connecting. Both plugins register the contracts in
`../HelloMessages.cs`; the server receives the authenticated request through
`ClientEvents.MessageReceived`, queries `CustomItems.GetPlayerReadiness`, and
uses `ClientMessaging.TrySend` to reply. Messaging remains available when the
player declines content. The client command waits for enrollment disclosure;
the transport still checks admission when sending and receiving.

To require the matching client mod at connection time, set
`RequireClientCompanion: true` in this sample plugin's LabAPI config. The sample
registers `example.hello` in the inclusive `0.1.0` to exclusive `0.2.0` range
during `Enable()` and disposes the requirement during `Disable()`. Without that
setting, the companion remains optional as before. Missing or incompatible
versions produce a detailed client-facing rejection.

The companion does not initiate custom traffic to clients without a request.
Install the matching companion before using the client command. Its message
subscriptions, like native item handlers, are removed during `Disable()`.

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

This is a breaking prerelease API reset that retains version 1.0. Rebuild existing
server plugins against the matching SDK. The source namespace is
`Anomaly.Server.Api`, while the distributable assembly remains `Anomaly.Server.dll`.
