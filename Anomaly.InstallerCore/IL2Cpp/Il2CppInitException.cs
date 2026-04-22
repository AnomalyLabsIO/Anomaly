namespace Anomaly.InstallerCore.IL2Cpp;

[Serializable]
public class Il2CppInitException : Exception
{
    public Il2CppInitException() { }
    public Il2CppInitException(string message) : base(message) { }
}
