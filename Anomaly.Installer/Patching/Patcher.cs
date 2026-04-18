using Anomaly.Installer.Extensions;
using Anomaly.Installer.IL2Cpp;
using Il2CppDumper;
using System.Text;
using System.Text.RegularExpressions;

namespace Anomaly.Installer.Patching;

internal class Patcher
{
    const byte NOP = 144;
    const byte RET = 195;

    private readonly Il2CppManager _il2CppManager;
    private readonly string _gameAssemblyPath;
    private readonly byte[] _gameAssemblyData;
    private readonly PatchInfo _patchInfo;

    public Patcher(string gameAssemblyPath, byte[] gameAssemblyData, Il2CppManager il2CppManager, PatchInfo patchInfo)
    {
        _gameAssemblyPath = gameAssemblyPath;
        _gameAssemblyData = gameAssemblyData;
        _il2CppManager = il2CppManager;
        _patchInfo = patchInfo;
    }

    /// <summary>
    /// Reads GameAssembly.dll bytes from disk.
    /// </summary>
    public static byte[] GetGameAssemblyData(string gameAssemblyPath)
    {
        if (!File.Exists(gameAssemblyPath))
            throw new InstallerException("Couldn't find GameAssembly.dll. Please check your game path.");
        return File.ReadAllBytes(gameAssemblyPath);
    }

    /// <summary>
    /// Gets the file offset from a function name using the IL2CPP method dictionary.
    /// </summary>
    private static int GetOffsetFromFuncName(Il2Cpp il2Cpp, string functionName, IReadOnlyDictionary<string, ScriptMethod> methodsDictionary)
    {
        if (methodsDictionary.ContainsKey(functionName))
        {
            ScriptMethod method = methodsDictionary[functionName];
            ulong address = method.Address;
            ulong rvaOffset = il2Cpp.GetRVA(il2Cpp.MapRTVA(address)) - address;
            ulong offset = address - rvaOffset;
            return Convert.ToInt32(offset);
        }
        return -1;
    }

    private int GetOffsetFromFuncName(string functionName)
    {
        return GetOffsetFromFuncName(_il2CppManager.IL2CPP, functionName, _il2CppManager.MethodsDictionary);
    }

    /// <summary>
    /// Fixes version of the metadata.
    /// </summary>
    public static void FixMetadataVersion(int metadataVersion, string gamePath)
    {
        string metadataPath = Path.Combine(gamePath, "SCPSL_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        if (!File.Exists(metadataPath))
            throw new InstallerException($"global-metadata.dat not found at {metadataPath}.");

        byte[] metadataData = File.ReadAllBytes(metadataPath);
        byte[] metadataVersionBytes = BitConverter.GetBytes(metadataVersion);
        const int versionOffset = 4;

        if (metadataData.Length < versionOffset + metadataVersionBytes.Length)
            throw new InstallerException(
                $"global-metadata.dat is too small ({metadataData.Length} bytes) to contain a valid version header. File may be truncated or corrupt.");

        for (int i = 0; i < metadataVersionBytes.Length; i++)
        {
            metadataData[i + versionOffset] = metadataVersionBytes[i];
        }
        File.WriteAllBytes(metadataPath, metadataData);
    }

    /// <summary>
    /// Prepares patch data before patching (resolves [NOP] and [RET] keywords).
    /// </summary>
    private static string PreparePatchData(string patchData)
    {
        StringBuilder sb = new StringBuilder(patchData);
        sb.Replace("[NOP]", "90");
        sb.Replace("[RET]", "C3");
        sb.Replace(" ", string.Empty);
        sb.Replace("-", string.Empty);
        return sb.ToString();
    }

    /// <summary>
    /// Patches a function in the GameAssembly bytes.
    /// </summary>
    private void PatchFunction(int functionOffset, PatchMethodInfo patchMethod)
    {
        string hexPatchData = PreparePatchData(patchMethod.patchData);

        byte[] patchData;
        try
        {
            patchData = Convert.FromHexString(hexPatchData);
        }
        catch (FormatException)
        {
            throw new InstallerException($"Invalid hex patch data ({hexPatchData}) for method ({patchMethod.name}).");
        }

        int patchOffset = functionOffset + patchMethod.patchOffset;
        int patchSize = patchMethod.patchSize ?? patchData.Length;

        if (patchOffset < 0 || patchOffset + patchSize > _gameAssemblyData.Length)
        {
            throw new InstallerException(
                $"Patch for ({patchMethod.name}) would write outside GameAssembly.dll " +
                $"(offset={patchOffset:X}, size={patchSize}, file={_gameAssemblyData.Length:X} bytes). " +
                "Aborting before corrupting the file.");
        }

        for (int i = 0; i < patchSize; i++)
        {
            if (i < patchData.Length)
                _gameAssemblyData[i + patchOffset] = patchData[i];
            else
                _gameAssemblyData[i + patchOffset] = NOP;
        }
    }

    /// <summary>
    /// Patches the GameAssembly using the configured patch methods.
    /// </summary>
    public void PatchGameAssembly()
    {
        PatchMethodInfo[] patchMethods = _patchInfo.patchMethods;

        for (int i = 0; i < patchMethods.Length; i++)
        {
            PatchMethodInfo patchMethod = patchMethods[i];
            int functionOffset = GetOffsetFromFuncName(patchMethod.name);

            if (!string.IsNullOrEmpty(patchMethod.patchifnotexists))
            {
                int notExistsOffset = GetOffsetFromFuncName(patchMethod.patchifnotexists);
                if (notExistsOffset >= 0)
                    continue;
            }

            if (functionOffset < 0)
            {
                if (!patchMethod.nowarning)
                {
                    string msg = $"Couldn't patch ({patchMethod.name}) function.";
                    if (string.IsNullOrEmpty(patchMethod.patchifnotexists))
                        ConsoleUI.PrintWarning($"{msg} This function wasn't found.");
                    else
                        ConsoleUI.PrintWarning($"{msg} This function and ({patchMethod.patchifnotexists}) weren't found.");
                }
                continue;
            }

            PatchFunction(functionOffset, patchMethod);
            Console.WriteLine($"  Patched: {patchMethod.name}");
        }
    }

    /// <summary>
    /// Saves the patched GameAssembly back to disk.
    /// </summary>
    public void SaveGameAssembly()
    {
        File.WriteAllBytes(_gameAssemblyPath, _gameAssemblyData);
    }

    /// <summary>
    /// Re-reads GameAssembly.dll from disk and verifies it matches the patched in-memory bytes.
    /// Guards against partial writes, AV interference, or disk corruption between write and next step.
    /// </summary>
    public void VerifySavedAssembly()
    {
        string expected = ResourceIntegrity.ComputeSha256(_gameAssemblyData);
        string actual = ResourceIntegrity.ComputeFileSha256(_gameAssemblyPath);
        ResourceIntegrity.VerifyHash("patched GameAssembly.dll", expected, actual);
    }

    /// <summary>
    /// Re-parses the patched GameAssembly via Il2CppDumper to confirm the patches didn't
    /// break IL2CPP structure. Catches bad offsets before the user hits a runtime crash.
    /// A hash check alone only proves bytes landed on disk; this proves they're still valid.
    /// </summary>
    public void VerifyPatchedStructure(string metadataPath, int metadataVersion, string workingDir)
    {
        // Use a fresh copy so the dumper can't mutate the bytes we just wrote.
        byte[] copy = new byte[_gameAssemblyData.Length];
        Buffer.BlockCopy(_gameAssemblyData, 0, copy, 0, copy.Length);

        var verifier = new Il2CppManager(copy, workingDir);
        try
        {
            verifier.LoadIl2Cpp(metadataPath, metadataVersion);
        }
        catch (Exception ex)
        {
            throw new InstallerException(
                "Patched GameAssembly.dll failed re-parse — a patch likely wrote to a bad offset. " +
                $"Underlying error: {ex.Message}");
        }
    }

    // --- Game Version Reading ---

    private const string VersionMethodName = "GameCore.Version$$.cctor";

    /// <summary>
    /// Reads the real game version from GameCore.Version's static constructor in GameAssembly.dll.
    /// Uses auto-find to locate the version byte offsets dynamically.
    /// </summary>
    public static (byte major, byte minor, byte revision)? ReadGameVersion(byte[] gameAssemblyData, Il2CppManager il2CppManager)
    {
        int methodOffset = GetOffsetFromFuncName(
            il2CppManager.IL2CPP, VersionMethodName, il2CppManager.MethodsDictionary);

        if (methodOffset < 0)
        {
            ConsoleUI.PrintWarning($"Could not find {VersionMethodName} — skipping version read.");
            return null;
        }

        try
        {
            // Find the pattern that precedes version field writes:
            // mov rcx, [rax+0B8h]
            byte[] pattern = { 0x48, 0x8B, 0x88, 0xB8, 0x00, 0x00, 0x00 };
            long patternOffset = gameAssemblyData.IndexOf(pattern, methodOffset);
            if (patternOffset == -1)
            {
                ConsoleUI.PrintWarning("Version auto-find pattern not found — skipping version read.");
                return null;
            }

            int searchFrom = (int)patternOffset;

            // Field offsets within the GameVersion struct: major=0, minor=1, revision=2
            byte[] fieldOffsets = { 0, 1, 2 };
            byte[] values = new byte[3];

            for (int i = 0; i < fieldOffsets.Length; i++)
            {
                byte[] instrBytes = fieldOffsets[i] == 0
                    ? new byte[] { 0xC6, 0x01 }                        // mov byte ptr [rcx], {value}
                    : new byte[] { 0xC6, 0x41, fieldOffsets[i] };       // mov byte ptr [rcx+offset], {value}

                int instrOffset = (int)gameAssemblyData.IndexOf(instrBytes, searchFrom);
                if (instrOffset == -1)
                {
                    ConsoleUI.PrintWarning($"Version field instruction not found for offset {fieldOffsets[i]} — skipping version read.");
                    return null;
                }

                int valueOffset = instrOffset + instrBytes.Length;
                values[i] = gameAssemblyData[valueOffset];
                searchFrom = valueOffset + 1;
            }

            return (values[0], values[1], values[2]);
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintWarning($"Version auto-find failed: {ex.Message}");
            return null;
        }
    }

    // --- Bundle Version Fix ---

    /// <summary>
    /// Patches the bundleVersion string in SCPSL_Data/globalgamemanagers to match the real game version.
    /// The string is stored as a 4-byte little-endian length prefix followed by the ASCII string.
    /// </summary>
    public static void FixBundleVersion(string gamePath, byte major, byte minor, byte revision)
    {
        string ggmPath = Path.Combine(gamePath, "SCPSL_Data", "globalgamemanagers");
        if (!File.Exists(ggmPath))
        {
            ConsoleUI.PrintWarning("globalgamemanagers not found — skipping bundle version fix.");
            return;
        }

        byte[] data = File.ReadAllBytes(ggmPath);
        string newVersion = $"{major}.{minor}.{revision}";

        // Search for an existing version string pattern: 4-byte length + digits.digits.digits
        int versionOffset = FindBundleVersionOffset(data);
        if (versionOffset == -1)
        {
            ConsoleUI.PrintWarning("Could not locate bundleVersion in globalgamemanagers — skipping.");
            return;
        }

        int oldLength = BitConverter.ToInt32(data, versionOffset);
        string oldVersion = Encoding.UTF8.GetString(data, versionOffset + 4, oldLength);
        byte[] newVersionBytes = Encoding.UTF8.GetBytes(newVersion);

        if (oldVersion == newVersion)
        {
            Console.WriteLine($"  Bundle version already correct ({newVersion}).");
            return;
        }

        if (newVersionBytes.Length > oldLength)
        {
            // Growing in place would overwrite whatever follows the version slot in
            // globalgamemanagers. Refuse rather than silently corrupting the file.
            throw new InstallerException(
                $"Detected game version \"{newVersion}\" ({newVersionBytes.Length} bytes) is longer than the " +
                $"existing bundle version slot (\"{oldVersion}\", {oldLength} bytes). Growing in-place is unsafe.");
        }

        if (newVersionBytes.Length != oldLength)
        {
            ConsoleUI.PrintWarning(
                $"Version string length changed (old=\"{oldVersion}\" [{oldLength}], new=\"{newVersion}\" [{newVersionBytes.Length}]). " +
                "Updating length prefix — verify the game launches correctly.");

            // Update the length prefix
            byte[] newLengthBytes = BitConverter.GetBytes(newVersionBytes.Length);
            Array.Copy(newLengthBytes, 0, data, versionOffset, 4);

            // If new string is shorter, zero out the remaining old bytes
            for (int i = newVersionBytes.Length; i < oldLength; i++)
                data[versionOffset + 4 + i] = 0;
        }

        // Write the new version string
        Array.Copy(newVersionBytes, 0, data, versionOffset + 4, newVersionBytes.Length);

        File.WriteAllBytes(ggmPath, data);
        Console.WriteLine($"  Bundle version updated: {oldVersion} -> {newVersion}");
    }

    /// <summary>
    /// Scans globalgamemanagers bytes for a length-prefixed version string matching N.N.N pattern.
    /// Returns the offset of the 4-byte length prefix, or -1 if not found.
    /// </summary>
    private static int FindBundleVersionOffset(byte[] data)
    {
        // Version strings are short (3-10 chars). Scan for length-prefixed ASCII strings
        // that match a dotted version pattern like "14.1.0" or "14.2.6".
        for (int i = 0; i < data.Length - 14; i++)
        {
            int len = BitConverter.ToInt32(data, i);
            if (len < 3 || len > 20 || i + 4 + len > data.Length)
                continue;

            // Check if the bytes after the length form a version string
            string candidate = Encoding.UTF8.GetString(data, i + 4, len);
            if (Regex.IsMatch(candidate, @"^\d{1,3}\.\d{1,3}\.\d{1,3}$"))
            {
                // Validate it looks like a game version (major >= 10 for SCPSL)
                string[] parts = candidate.Split('.');
                if (byte.TryParse(parts[0], out byte maj) && maj >= 10)
                    return i;
            }
        }
        return -1;
    }
}
