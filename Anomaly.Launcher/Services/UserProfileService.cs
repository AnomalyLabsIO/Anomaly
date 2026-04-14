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
}
