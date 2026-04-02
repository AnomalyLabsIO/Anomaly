
namespace Anomaly.Installer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        /*
         * Steps to be performed
         * - Select SCPSL.exe
         * - Select destination directory
         * - Copy SCPSL folder contents to destination
         * - Run SCPSL-ModPatch with updated patchinfo
         * - Download and install MelonLoader 0.7.2
         * - Run game once to generate assemblies
         * - Place Anomaly.Client and Anomaly.Shared in plugins.
         */
    }
}