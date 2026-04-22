# Disclaimer
Anomaly is not affiliated with SCPSL or NorthWood studios. If you experience any issues do not bother them.
This is an unsupported client modification of the game.

Central Authentication (Steam/Discord) and local Anti-Cheat are disabled, only Anomaly-enabled servers will allow you to connect.
The SCPSL EULA allows this type of modified client. However, NorthWood holds the right to ban you for stepping out of bounds such as the following:

- Do not attempt to re-enable Central Authentication or local Anti-Cheat
- Do not attempt to make further modifications to the client unless you have read the EULA and know what you are doing.
- Do not attempt to connect to public/online servers
- Do not use this framework for the creation, distribution, or use of hacked clients or features that provide an unfair advantage.
- Do not attempt to spoof other users
- Do not attempt to bypass bans
- Do not be the reason I have to write more rules.

The developers and those associated with Anomaly are not responsible for any bans or other consequences that may occur from using this modification. Use at your own risk.

# Welcome to Anomaly!
## What is Anomaly?
Anomaly is a client-server modification framework for SCP: Secret Laboratory. 
It allows you to create custom content and features for the Client that can be used on Anomaly-enabled servers. It is designed to be easy to use and flexible, allowing you to create a wide variety of mods.

## How do I install the client?
We provide a Launcher that will create and patch a modded copy of the game with Anomaly installed. This leaves the original game untouched and allows you to easily switch between modded and vanilla versions. The Launcher also handles updates.

Prerequisites:
- Must be on a modern version of Windows or Linux that supports the base game.
- Steam must be installed
- SCP: Secret Laboratory must be installed through Steam.
- Have the directory for the SCP:SL installation ready. This can be found from your Steam library.

1. Download the latest Anomaly.Launcher.exe from the releases page.
2. Place the Launcher in a new directory on your computer, this is where the modded game will live.
3. Run the Launcher, select your SCP:SL installation directory, and click "Install Anomaly". This will create a copy of the game in the Launcher directory with Anomaly installed.
4. Click Start Modded once to allow MelonLoader to generate the required files. Following lanches of the game will be much faster.
5. (Optional) Add Anomaly.Launcher.exe as a non-steam game to your library for easy access.

## Where do I get mods?
**A public, trusted mod source is in the works.** Be careful when downloading mods from untrusted sources, as they may contain malicious code. Always scan files with antivirus software and only download from reputable creators.

## How do I make my own mods?
1. Follow the instructions to install the client above. (Must have started the game once to generate the required files)
2. Follow the instructions for setting up a [Melon Loader](https://melonwiki.xyz/#/modders/quickstart) project. Targeting IL2Cpp and the latest version.
   3. The easiest way is the Visual Studio template, select the SCPSL.exe from the MODDED directory.
   4. Once the template project is created you can any IDE you'd like. I prefer JetBrains Rider.
5. Add references to `Anomaly.Client.Api.dll` and `Anomaly.Shared.dll` from the MODDED directory's `UserLibs/` folder. You do not need to reference `Anomaly.Client.dll` — it loads as a peer mod from `Mods/` at runtime, and declaring `[assembly: MelonAdditionalDependencies("Anomaly")]` in your mod enforces the load order.
6. Follow any guides on creating a IL2Cpp Melon mod, and use the Anomaly API documentation to learn how to use the Anomaly features.

If you would like to add a server component to your mod, create a LabAPI plugin and reference Anomaly.Server.dll and Anomaly.Shared.dll. EXILED should work as well, but note that Anomaly uses LabAPI types you may need to unwrap.

### Why Melon Loader?
Melon Loader is a popular modding framework for Unity games, and it provides a lot of features biggest of which are IL2Cpp support and easy install. 

BepInEx 6.0.0-pre.2 is the competitor to Melon Loader. It is confirmed to work with the patched client created by the installer. However, Anomaly is only built for ML. Please provide your feedback if you want to see BepInEx support in the future.

## Is there Server support?
Yes, Anomaly.Server.dll is a LabAPI plugin that will configure your server automatically to support Anomaly Clients. Note that this will put the server in offline mode, and will not allow Vanilla clients to connect.

Anomaly uses a custom decentralized authentication system that does not touch NorthWood servers or Steam. Credit to the [Axon](https://github.com/AxonSL/Axon) team for this system.
All users are identified by a "@anomaly" user id. This can be used similar to your "@steam" ID for remote admin permissions. This ID is verified by an Asymmetric handshake on server connection and cannot be spoofed. It is unique to the user profile on your computer.
You can find your ID by right-clicking your name in the Anomaly Client Launcher.

