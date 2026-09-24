# Server boundary regressions

Run `dotnet test Anomaly.Server.Tests/Anomaly.Server.Tests.csproj -c Debug -p:DeployLocal=false`.

The project compiles the actual server message dispatcher, connection bootstrap,
replay table, authenticated sessions, enrollment registry and player storage into
an isolated .NET Framework test host. Inert engine, plugin entry point, logging,
and transport doubles avoid requiring a running Unity server.

Tests cover quarantine between subscribers, subscriber exception isolation, and
bootstrap rejection with successful or throwing disconnect initiation before
Mirror has registered the connection. They verify key/enrollment revocation and
connection bookkeeping; they do not establish real packet delivery, Harmony
hook execution, LabAPI lifecycle behavior, or live server/client compatibility.
