using Il2CppDumper;
using Newtonsoft.Json;
using System.IO.Hashing;

namespace Anomaly.Installer.IL2Cpp;

internal class Il2CppManager
{
    private readonly string _il2cppFolder;

    private Il2Cpp? _il2Cpp;
    private Metadata? _metadata;
    private byte[] _gameAssemblyData;
    private ScriptJson? _scriptJson;
    private Dictionary<string, ScriptMethod>? _methodsDictionary;

    public bool IsIl2CppLoaded => _il2Cpp != null;

    public Il2Cpp IL2CPP
    {
        get
        {
            if (_il2Cpp == null)
                throw new InvalidOperationException("IL2CPP is not loaded.");
            return _il2Cpp;
        }
    }

    public Metadata Metadata
    {
        get
        {
            if (_metadata == null)
                throw new InvalidOperationException("Metadata is not loaded.");
            return _metadata;
        }
    }

    public ScriptJson ScriptJson
    {
        get
        {
            if (_scriptJson == null)
                throw new InvalidOperationException("ScriptJson is not loaded.");
            return _scriptJson;
        }
    }

    /// <summary>
    /// Keys are method names.
    /// </summary>
    public IReadOnlyDictionary<string, ScriptMethod> MethodsDictionary
    {
        get
        {
            if (_methodsDictionary == null)
                throw new InvalidOperationException("MethodsDictionary is not loaded.");
            return _methodsDictionary;
        }
    }

    public Il2CppManager(byte[] gameAssemblyData, string workingDirectory)
    {
        _gameAssemblyData = gameAssemblyData;
        _il2cppFolder = Path.Combine(workingDirectory, "il2cppdumper");
    }

    public void LoadIl2Cpp(string metadataPath, double? metadataVersion = null)
    {
        if (_gameAssemblyData == null || _gameAssemblyData.Length == 0)
            throw new ArgumentException("GameAssembly data is null or empty.");
        if (!File.Exists(metadataPath))
            throw new ArgumentException($"Couldn't find {Path.GetFileName(metadataPath)}. Please check your game path.");

        Config il2cppConfig = new Config();
        if (metadataVersion != null)
        {
            il2cppConfig.ForceIl2CppVersion = true;
            il2cppConfig.ForceVersion = metadataVersion.Value;
        }

        byte[] originalHash = XxHash128.Hash(_gameAssemblyData);

        try
        {
            Il2CppDumperWorker.Init(_gameAssemblyData, metadataPath, il2cppConfig, out _metadata, out _il2Cpp);
        }
        catch (FileIsProtectedException)
        {
            _il2Cpp = null;
            throw; // Let this propagate for the unpacker flow
        }
        catch (Exception ex)
        {
            _il2Cpp = null;
            throw new Il2CppInitException($"Loading IL2CPP failed: {ex.Message}");
        }

        byte[] finalHash = XxHash128.Hash(_gameAssemblyData);
        if (!originalHash.SequenceEqual(finalHash))
            throw new Exception("Hash mismatch after IL2CPP load. Il2CppDumper may have modified the GameAssembly data.");
    }

    public void DumpIl2Cpp()
    {
        if (!Directory.Exists(_il2cppFolder))
            Directory.CreateDirectory(_il2cppFolder);

        string outputPath = _il2cppFolder + Path.DirectorySeparatorChar;
        Il2CppDumperWorker.Dump(Metadata, IL2CPP, outputPath);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void GetScriptJson()
    {
        string scriptPath = Path.Combine(_il2cppFolder, "script.json");
        if (!File.Exists(scriptPath))
            throw new Exception("Script file not found. Dump IL2CPP first.");

        ScriptJson? scriptJson = JsonConvert.DeserializeObject<ScriptJson>(File.ReadAllText(scriptPath));
        if (scriptJson == null)
            throw new Exception("Failed to deserialize script.json.");
        _scriptJson = scriptJson;
    }

    public void GetMethodsDictionary()
    {
        _methodsDictionary = new Dictionary<string, ScriptMethod>();
        foreach (ScriptMethod method in ScriptJson.ScriptMethod)
        {
            string methodName = method.Name;
            if (_methodsDictionary.ContainsKey(methodName))
                continue;
            _methodsDictionary.Add(methodName, method);
        }
    }

    public void RemoveDumpFolder()
    {
        if (Directory.Exists(_il2cppFolder))
            Directory.Delete(_il2cppFolder, true);
    }
}
