using System.Text;
using Anomaly.Client.API.Networking.Messages;
using MelonLoader;

namespace Anomaly.Client.API.Networking;

public static class AnomalyMessageRegistry
{
    private static readonly Dictionary<uint, Func<IAnomalyMessage>> Factories = new();
    private static readonly Dictionary<uint, string> HashToName = new();
    private static readonly Dictionary<string, uint> NameToHash = new();

    public static void Register<T>(string name, Func<T> factory) where T : IAnomalyMessage
    {
        uint hash = ComputeHash(name);

        if (HashToName.TryGetValue(hash, out var existing))
        {
            if (existing == name)
            {
                MelonLogger.Warning($"AnomalyMessageRegistry: '{name}' is already registered, skipping.");
                return;
            }
            throw new InvalidOperationException(
                $"AnomalyMessageRegistry: FNV-1a hash collision between '{existing}' and '{name}' (hash=0x{hash:X8}). Rename one of them.");
        }

        Factories[hash] = () => factory();
        HashToName[hash] = name;
        NameToHash[name] = hash;

        MelonLogger.Msg($"AnomalyMessageRegistry: Registered '{name}' (hash=0x{hash:X8})");
    }

    public static bool TryCreate(uint hash, out IAnomalyMessage message)
    {
        if (Factories.TryGetValue(hash, out var factory))
        {
            message = factory();
            return true;
        }
        message = null;
        return false;
    }

    public static uint GetHash(string name)
    {
        if (NameToHash.TryGetValue(name, out var hash))
            return hash;
        throw new KeyNotFoundException($"AnomalyMessageRegistry: '{name}' is not registered.");
    }

    public static void RegisterAll()
    {
        Register("anomaly:blendshapes", () => new EmotionBlendShapesMessage());
        Register("anomaly:bonetransforms", () => new BoneTransformsMessage());
    }

    /// <summary>
    /// FNV-1a 32-bit hash of the UTF-8 bytes of the input string.
    /// </summary>
    public static uint ComputeHash(string input)
    {
        uint hash = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(input))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return hash;
    }
}
