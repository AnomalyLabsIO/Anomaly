namespace Anomaly.Installer;

public class InstallerException : Exception
{
    public InstallerException(string message) : base(message) { }
    public InstallerException(string message, Exception inner) : base(message, inner) { }
}
