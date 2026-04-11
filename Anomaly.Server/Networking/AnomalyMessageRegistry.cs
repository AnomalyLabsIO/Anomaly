using System;
using System.Collections.Generic;
using System.Text;
using Anomaly.Server.Networking.Messages;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Networking;

public static class AnomalyMessageRegistry
{
    private static readonly Dictionary<uint, Func<IAnomalyMessage>> Factories = new Dictionary<uint, Func<IAnomalyMessage>>();
    private static readonly Dictionary<uint, string> HashToName = new Dictionary<uint, string>();
    private static readonly Dictionary<string, uint> NameToHash = new Dictionary<string, uint>();

    public static void Register<T>(string name, Func<T> factory) where T : IAnomalyMessage
    {
        uint hash = ComputeHash(name);

        if (HashToName.TryGetValue(hash, out var existing))
        {
            if (existing == name)
            {
                Logger.Warn($"AnomalyMessageRegistry: '{name}' is already registered, skipping.");
                return;
            }
            throw new InvalidOperationException(
                $"AnomalyMessageRegistry: FNV-1a hash collision between '{existing}' and '{name}' (hash=0x{hash:X8}). Rename one of them.");
        }

        Factories[hash] = () => factory();
        HashToName[hash] = name;
        NameToHash[name] = hash;

        Logger.Info($"AnomalyMessageRegistry: Registered '{name}' (hash=0x{hash:X8})");
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
        Register("anomaly:silentrolechance", () => new SilentRoleChangeMessage());
    }

    /// <summary>
    /// FNV-1a 32-bit hash. Must produce identical results to client-side implementation.
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
