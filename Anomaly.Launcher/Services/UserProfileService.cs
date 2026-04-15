using System.Numerics;
using System.Security.Cryptography;
using Anomaly.Launcher.Models;
using Newtonsoft.Json;

namespace Anomaly.Launcher.Services;

public class UserProfileService
{
    private readonly PathService _paths;

    public UserProfileService(PathService paths)
    {
        _paths = paths;
    }

    public string? GetUserId()
    {
        try
        {
            if (!File.Exists(_paths.UserJsonPath))
                return null;

            var json = File.ReadAllText(_paths.UserJsonPath);
            var profile = JsonConvert.DeserializeObject<UserProfile>(json);
            if (string.IsNullOrEmpty(profile?.Identity) || profile.Identity == "pending")
                return null;

            var identityBytes = Convert.FromBase64String(profile.Identity);
            if (identityBytes.Length < 97)
                return null;

            // Identity is 97 bytes: [0..31] private key, [32..96] public key (65 bytes)
            var publicKey = new byte[65];
            Array.Copy(identityBytes, 32, publicKey, 0, 65);

            var hash = SHA256.HashData(publicKey);
            return Base58Encode(hash) + "@anomaly";
        }
        catch
        {
            return null;
        }
    }

    public string GetUsername()
    {
        try
        {
            if (!File.Exists(_paths.UserJsonPath))
                return Environment.UserName;

            var json = File.ReadAllText(_paths.UserJsonPath);
            var profile = JsonConvert.DeserializeObject<UserProfile>(json);
            return !string.IsNullOrEmpty(profile?.Username) ? profile.Username : Environment.UserName;
        }
        catch
        {
            return Environment.UserName;
        }
    }

    public bool SaveUsername(string newUsername)
    {
        try
        {
            UserProfile? profile = null;
            if (File.Exists(_paths.UserJsonPath))
            {
                var json = File.ReadAllText(_paths.UserJsonPath);
                profile = JsonConvert.DeserializeObject<UserProfile>(json);
            }

            profile ??= new UserProfile();
            profile.Username = newUsername;

            var dir = Path.GetDirectoryName(_paths.UserJsonPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_paths.UserJsonPath, JsonConvert.SerializeObject(profile));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base58Encode(byte[] bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        BigInteger intData = 0;
        for (int i = 0; i < bytes.Length; i++)
            intData = intData * 256 + bytes[i];

        string result = "";
        while (intData > 0)
        {
            int remainder = (int)(intData % 58);
            intData /= 58;
            result = alphabet[remainder] + result;
        }

        for (int i = 0; i < bytes.Length && bytes[i] == 0; i++)
            result = '1' + result;

        return result;
    }
}
