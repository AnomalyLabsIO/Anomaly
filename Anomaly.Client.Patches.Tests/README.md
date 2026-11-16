# Client patch regression tests

This harness compiles the real encrypted-channel send/receive patches, receive
handler registration, mandatory-patch verifier, transactional initializer and
client replay table. It uses Anomaly.Shared's real crypto and rate limits.
The bulk transport tests also compile the real client lane-ownership runtime
and SetOptions/batch-threshold patches against inert native transport objects.

Game objects, Harmony metadata, IL2CPP delegate conversion, UI and the network
connection are inert test boundaries. Tests call the actual patch entry points;
they do not load Unity, install native hooks, deploy files or connect to a server.
These checks cannot prove native invocation counts or transport throughput.

Run from the repository root:

```powershell
dotnet test Anomaly.Client.Patches.Tests/Anomaly.Client.Patches.Tests.csproj -c Debug -p:DeployLocal=false
```
