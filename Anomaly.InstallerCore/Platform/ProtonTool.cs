namespace Anomaly.InstallerCore.Platform;

public sealed class ProtonTool
{
    public string Name { get; }
    public string RootPath { get; }
    public string LauncherPath { get; }
    public bool IsCustom { get; }

    internal int Priority { get; }

    public ProtonTool(string name, string rootPath, bool isCustom)
    {
        Name = name;
        RootPath = rootPath;
        LauncherPath = Path.Combine(rootPath, "proton");
        IsCustom = isCustom;
        Priority = ComputePriority(name, isCustom);
    }

    private static int ComputePriority(string name, bool isCustom)
    {
        if (string.Equals(name, "Proton Experimental", StringComparison.OrdinalIgnoreCase))
            return 600;

        if (string.Equals(name, "Proton Hotfix", StringComparison.OrdinalIgnoreCase))
            return 550;

        if (name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(name[7..], out Version? version))
        {
            return 500 + (version.Major * 10) + Math.Max(version.Minor, 0);
        }

        return isCustom ? 400 : 100;
    }
}
