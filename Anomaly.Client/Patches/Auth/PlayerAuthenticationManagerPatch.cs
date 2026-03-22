using Anomaly.Client.API.Features;
using Anomaly.Client.Auth;
using HarmonyLib;
using Il2Cpp;
using Il2CppCentralAuth;
using Il2CppCryptography;
using Il2CppGameCore;
using Il2CppMirror;
using Il2CppSystem.Text;
using MelonLoader;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using UnityEngine;

namespace Anomaly.Client.Patches.Auth;

[HarmonyPatch]
public static class PlayerAuthenticationManagerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAuthenticationManager), nameof(PlayerAuthenticationManager.Start))]
    public static void Start(PlayerAuthenticationManager __instance)
    {
        if (!__instance.isLocalPlayer) return;
        __instance._hub.encryptedChannelManager.EncryptionKey = AuthHandler.CurrentKey;
        MelonLogger.Warning("Starting player authentication manager");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.TrySendMessageToServer), new Type[] { typeof(string), typeof(EncryptedChannelManager.EncryptedChannel) })]
    public static bool OnPack(EncryptedChannelManager __instance, out bool __result,
        string content, EncryptedChannelManager.EncryptedChannel channel)
    {
        if (__instance._txCounter == 4294967295U)
            __instance._txCounter = 0;
        __instance._txCounter++;

        EncryptedChannelManager.EncryptedMessageOutside messageOut;

        var data = new byte[Misc.Utf8Encoding.GetByteCount(content) + 5];
        data[0] = (byte)channel;
        BitConverter.GetBytes(__instance._txCounter).CopyTo(data, 1);
        Encoding.UTF8.GetBytes(content, 0, content.Length, data, 5);

        if (__instance.EncryptionKey == null)
        {
            MelonLogger.Warning("Tried to send encrypted message, but no key was found");
            __result = false;
            return false;
        }

        var encryptedData = AesGcmEncrypt(data, __instance.EncryptionKey, _secureRandom);
        messageOut = new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated, encryptedData);
        MelonLogger.Warning("Sending encrypted message outside");
        NetworkClient.Send(messageOut);
        __result = true;
        return false;
    }

    private static readonly SecureRandom _secureRandom = new();

    private static byte[] AesGcmEncrypt(byte[] data, byte[] secret, SecureRandom secureRandom)
    {
        byte[] array = new byte[32];
        byte[] result;

        secureRandom.NextBytes(array, 0, array.Length);
        GcmBlockCipher gcmBlockCipher = new GcmBlockCipher(new AesEngine());
        gcmBlockCipher.Init(true, new AeadParameters(new KeyParameter(secret), 128, array));
        int outputSize = gcmBlockCipher.GetOutputSize(data.Length);
        var array2 = new byte[outputSize];
        int outOff = gcmBlockCipher.ProcessBytes(data, 0, data.Length, array2, 0);
        gcmBlockCipher.DoFinal(array2, outOff);
        using (MemoryStream memoryStream = new MemoryStream())
        {
            using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream))
            {
                binaryWriter.Write(array);
                binaryWriter.Write(array2, 0, outputSize);
            }
            result = memoryStream.ToArray();
        }
        return result;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EncryptedChannelManager), nameof(EncryptedChannelManager.ClientReceivePackedMessage))]
    public static bool OnReceivePackedMessage(EncryptedChannelManager.EncryptedMessageOutside packed)
    {
        MelonLogger.Warning("Got message back.");
        var hub = LocalPlayer.Instance.ReferenceHub;

        if(hub.encryptedChannelManager.EncryptionKey == null)
        {
            MelonLogger.Warning("Got EncryptedMessage even though no Key was set");
            return false;
        }
        var decryptedData = AES.AesGcmDecrypt(packed.Data, hub.encryptedChannelManager.EncryptionKey);

        var channel = (EncryptedChannelManager.EncryptedChannel)decryptedData[0];
        var counter = BitConverter.ToUInt32(decryptedData, 1);
        var content = Encoding.UTF8.GetString(decryptedData, 5, decryptedData.Length - 5);
        EncryptedChannelManager.EncryptedMessage message = new EncryptedChannelManager.EncryptedMessage(channel, content, counter);

        if (hub.encryptedChannelManager._rxCounter == 4294967295U)
            hub.encryptedChannelManager._rxCounter = 0;

        if(counter <= hub.encryptedChannelManager._rxCounter)
        {
            MelonLogger.Warning(string.Format("Received message with counter {0}, which is lower or equal to last received message counter {1}. Discarding message!", counter, hub.encryptedChannelManager._rxCounter));
            return false;
        }

        hub.encryptedChannelManager._rxCounter = counter;
        

        if (!EncryptedChannelManager.ClientChannelHandlers.ContainsKey(channel))
        {
            MelonLogger.Warning(
                string.Format("No handler is registered for encrypted channel {0} (client).", channel));
            return false;
        }

        try
        {
            EncryptedChannelManager.ClientChannelHandlers[channel].Invoke(message, packed.Level);
        }
        catch(Exception ex)
        {
            Il2CppGameCore.Console.AddLog(string.Format("Exception while handling encrypted message on channel {0} (client, running a handler). Exception: {1}", channel, ex.Message), Color.red, false, ConsoleLogType.Log);
            Il2CppGameCore.Console.AddLog(ex.StackTrace, Color.red, false, ConsoleLogType.Log);
        }
        return false;
    }
}
