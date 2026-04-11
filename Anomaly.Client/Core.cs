using Anomaly.Client.Auth;
using Anomaly.Client.Internal.Bootstrap;
using Il2Cpp;
using Il2CppCentralAuth;
using MelonLoader;

[assembly: MelonInfo(typeof(Anomaly.Client.Core), "Anomaly", "1.0.0", "AnomalyLabs", null)]
[assembly: MelonGame("Northwood", "SCPSL")]

namespace Anomaly.Client;

public class Core : MelonMod
{
    public static Core Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;
        HarmonyInstance.PatchAll();

        CustomNetworkManager.Modded = true;
        CentralAuthManager.NoAuthStartupArg = "-noauth";

        AuthHandler.Init();
        ClientRuntime.Initialize();

        LoggerInstance.Msg("Initialized.");
    }

    public override void OnUpdate()
    {
        ClientRuntime.Update();
    }
}
