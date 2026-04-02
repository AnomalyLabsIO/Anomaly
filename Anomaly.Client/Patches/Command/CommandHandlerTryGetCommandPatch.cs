using HarmonyLib;
using MelonLoader;

namespace Anomaly.Client.Patches.Command;

[HarmonyPatch(typeof(Il2CppGameCore.Console),nameof(Il2CppGameCore.Console.TypeCommand))]
public class CommandHandlerTryGetCommandPatch
{
    [HarmonyPrefix]
    public static bool OnTryGetCommand(string cmd)
    {
        try
        {
           
        }
        catch(Exception e)
        {
            MelonLogger.Msg("Patch.Il2CppGameCore.Console.TypeCommand: " + e);
            return true;
        }

        return false;
    }
}
