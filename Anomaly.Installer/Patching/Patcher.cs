using Anomaly.Installer.IL2Cpp;
using Il2CppDumper;
using System.Text;

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
        byte[] metadataData = File.ReadAllBytes(metadataPath);
        byte[] metadataVersionBytes = BitConverter.GetBytes(metadataVersion);
        int versionOffset = 4;
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
}
