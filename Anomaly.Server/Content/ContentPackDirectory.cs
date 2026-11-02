using System;
using System.Globalization;
using System.IO;

namespace Anomaly.Server.Content;

/// <summary>Resolves disk packs against the LabAPI plugin install scope, never the working directory.</summary>
internal static class ContentPackDirectory
{
    internal static string Resolve(string pluginsDirectory, string pluginFilePath, ushort port, string configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            return null;

        string pluginsRoot = Path.GetFullPath(pluginsDirectory);
        StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string globalRoot = Path.Combine(pluginsRoot, "global") + Path.DirectorySeparatorChar;
        bool global = !string.IsNullOrEmpty(pluginFilePath) && Path.IsPathRooted(pluginFilePath)
            && Path.GetFullPath(pluginFilePath).StartsWith(globalRoot, pathComparison);
        string anomalyRoot = Path.Combine(pluginsRoot, global ? "global" : port.ToString(CultureInfo.InvariantCulture), "Anomaly");
        if (!Path.IsPathRooted(configuredDirectory))
            return Path.GetFullPath(Path.Combine(anomalyRoot, configuredDirectory));

        string absoluteDirectory = Path.GetFullPath(configuredDirectory);
        // A drive-relative/root-relative Windows path still depends on process state.
        if (!string.Equals(Path.GetPathRoot(configuredDirectory), Path.GetPathRoot(absoluteDirectory), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ContentPackDirectory must be relative to the Anomaly directory or fully absolute.", nameof(configuredDirectory));
        return absoluteDirectory;
    }
}
