# Anomaly

Anomaly is a modding framework for **SCP: Secret Laboratory**. It brings client
mods and server plugins together so you can create custom gameplay, interfaces,
and content for your community. Client mods run through MelonLoader, server
plugins use LabAPI, and Anomaly provides the APIs and networking to connect them.

**[Documentation](https://docs.anomalylabs.io/)** |
**[Download the launcher](https://github.com/AnomalyLabsIO/scpsl-modlaunch/releases)** |
**[Server and SDK releases](https://github.com/AnomalyLabsIO/Anomaly/releases)**

Anomaly is in **early access**. The current release supports SCP:SL **14.2.7–14.2.8**. Use compatible Anomaly versions on the client
and server.

## Some features
- Easy to use modding framework for SCP:SL client and server.
- Extensive client API with similar structure to LabAPI and EXILED plugins.
- Multiplayer support for supported servers. Including secure, decentralized UserIDs for granting RemoteAdmin permissions.
- Custom content support for maps, items, and more.
- Intended for Offline Mode modding; follow the [SCP:SL EULA](https://store.steampowered.com/eula/700330_eula_0).

## Set up the client

Use a Windows x64 game installation. Linux/Proton is unconfirmed; do not rely on
it for connecting to Anomaly servers.

Things you'll need:
- Steam
- SCP: Secret Laboratory installed, and preferably copied to a separate folder for modding.
- Windows x64 **.NET 6 Desktop Runtime 6.0.36 or later**. A newer major runtime alone is not enough.

1. Download [ModLaunch](https://github.com/AnomalyLabsIO/SCPSL-ModLaunch/releases)
   and place the exe file in the same folder as your `SCPSL.exe`.
2. Download `anomaly-client-<version>.zip` from the
   [Anomaly releases](https://github.com/AnomalyLabsIO/Anomaly/releases) and extract it into your modded SCP:SL folder.
   `Mods`, `Plugins`, `UserLibs`, and `UserData` should be in the same folder as
   `SCPSL.exe`. Keep all bundled files together.
3. Run `SCPSL-ModLaunch.exe`, review the game folder, and choose **Launch**.
   The launcher prepares the game and MelonLoader; first preparation can take a few minutes.
4. For later sessions, use **SCPSL ModLaunch.lnk** or **SCPSL ModLaunch GUI.lnk**
   in the game folder. The launcher executable moves into `.modlaunch`.
5. Install additional compatible MelonLoader mods according to their authors' directions.

The launcher restores original game files when the game exits by default. After
an interrupted session, use **Restore originals** before vanilla play or Steam
file verification. Keep a separate game copy for modded play.

Server-provided Lua content requires Windows and separate approval to download
content and run scripts. If the client cannot run a required script pack, it
cannot join that server. Keep the complete Anomaly client package installed.

Connect to an Anomaly-enabled server using the address provided by its owner.
To try mods in a local game, select **Create Server** from the Servers menu or run `host`
in the game console.

See the [client installation guide](https://docs.anomalylabs.io/player/03-install-client/)
for the full walkthrough and platform-specific setup.

## Set up a server

Anomaly runs as a **LabAPI plugin** on an SCP:SL dedicated server. Start with a
working server running a supported game version.

1. Stop the server.
2. Download `anomaly-server-<version>.zip` from the
   [Anomaly releases](https://github.com/AnomalyLabsIO/Anomaly/releases), the same version your players will be using.
3. Extract the archive into your **LabAPI root**, preserving its directory
   structure and including all bundled dependencies. The runtime files belong at:

   ```text
   plugins/global/Anomaly.Server.dll
   dependencies/global/Anomaly.Shared.dll
   dependencies/global/0Harmony.dll
   ```

4. Start the server and check the console for the `Anomaly.Server enabled`
   startup message.
5. Join using the Anomaly client. Players need Anomaly to connect to the server.

Server-owned content packs default to
`<LabAPI>/plugins/global/Anomaly/ContentPacks/<namespace>/<pack>/pack.json`
for the packaged global install. A port-scoped install instead uses
`<LabAPI>/plugins/<port>/Anomaly/ContentPacks`. Relative `content_pack_directory`
overrides resolve from that scope's `Anomaly` directory, never the server working
directory; absolute overrides are also supported.

Continue with the [server installation guide](https://docs.anomalylabs.io/server/02-install/)
and [configuration reference](https://docs.anomalylabs.io/server/03-configuration/)
for server settings, custom content, and troubleshooting.

Removing or disabling Anomaly.Server will restore the server to vanilla behavior.

## Make your first mod

The [developer SDK](https://github.com/AnomalyLabsIO/Anomaly/releases) includes
client and server libraries, API documentation, and starter projects. Download
it from the same release as your Anomaly installation, then follow a quickstart:

- [Client mod quickstart](https://docs.anomalylabs.io/modding/03-client-quickstart/)
  — build a MelonLoader mod with Anomaly's client API.
- [Server plugin quickstart](https://docs.anomalylabs.io/modding/05-server-quickstart/)
  — build a LabAPI plugin that works with Anomaly clients.

The [full documentation](https://docs.anomalylabs.io/) covers events, commands,
input, UI, networking, custom content, and packaging your mods.

Content authors can package Lua scripts, supported Unity assets, and custom item
definitions together. See the [content-pack guide](https://docs.anomalylabs.io/modding/21-content-packs/),
[Lua scripting guide](https://docs.anomalylabs.io/modding/22-lua-scripting/),
and [asset authoring guide](ASSETBUNDLES.md).

Machine-readable documentation is available at [llms.txt](https://docs.anomalylabs.io/llms.txt).

## Get help

Start with the [troubleshooting guide](https://docs.anomalylabs.io/player/08-troubleshooting/).
If you need to [report a bug](https://github.com/AnomalyLabsIO/Anomaly/issues),
include what happened, steps to reproduce it, your game and Anomaly versions,
and a relevant log excerpt. Run `anomaly.status --copy` in the client console to
copy a diagnostic summary for your report.

## License

Anomaly is licensed under [LGPL-2.1-or-later](LICENSE). You can use the Anomaly client and server libraries in your own mods and plugins, but you must comply with the license terms. See the [license file](LICENSE) for details.

## To NorthWood Studios

This is a fan project attempting to provide players with a safe modded experience while respecting your intention for the broader game.
For more information on what we have implemented to comply with your EULA, or if you have concerns with the project, please contact me and I'll be happy to discuss it.


## Disclaimer

Anomaly is an independent project and is not affiliated with Northwood Studios.
Use the modded client only with Anomaly-enabled, Offline Mode servers. Report
Anomaly issues to this project rather than Northwood support. Follow the
current SCP:SL EULA; compatibility with today's modding rules is not a guarantee
against future policy changes.