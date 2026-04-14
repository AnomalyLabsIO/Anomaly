namespace Anomaly.Installer.Patching;

public class PatchInfo
{
    public int metadataVersion;
    public PatchMethodInfo[] patchMethods = Array.Empty<PatchMethodInfo>();
}

public class PatchMethodInfo
{
    public string name = string.Empty;
    public int patchOffset;
    public string patchData = string.Empty;
    public int? patchSize = null;
    public bool nowarning = false;
    public string? patchifnotexists = null;
}
