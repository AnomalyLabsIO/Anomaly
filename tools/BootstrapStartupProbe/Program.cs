using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using HarmonyLib;
using MelonLoader;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
        try { return RunMain(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Bootstrap startup probe: " + exception);
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMain(string[] args)
    {
        if (args.Length != 4 || args[3] is not ("missing" or "failed"))
            throw new ArgumentException("Usage: Bootstrap.StartupProbe <loader-net6-directory> <client.dll> <bootstrap.dll> <missing|failed>");
        string loaderDirectory = Path.GetFullPath(args[0]);
        string clientPath = Path.GetFullPath(args[1]);
        string bootstrapPath = Path.GetFullPath(args[2]);
        string mode = args[3];
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == "Anomaly.Bootstrap")
            {
                if (mode == "missing")
                    throw new FileNotFoundException("Bootstrap intentionally absent in startup regression probe.", name.FullName);
                return context.LoadFromAssemblyPath(bootstrapPath);
            }
            foreach (string directory in new[] { loaderDirectory, Path.GetDirectoryName(clientPath)! })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate))
                    return context.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        return Run(clientPath, bootstrapPath, mode);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string clientPath, string bootstrapPath, string mode)
    {
        // Logging is the sole native loader boundary reached by this failure path.
        // Keep the production startup guard and Environment.Exit untouched.
        var harmony = new HarmonyLib.Harmony("anomaly.startup.regression");
        harmony.Patch(typeof(MelonLogger).GetMethod(nameof(MelonLogger.Error), new[] { typeof(string) })!,
            prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(CaptureError), BindingFlags.NonPublic | BindingFlags.Static)!));

        Assembly client = AssemblyLoadContext.Default.LoadFromAssemblyPath(clientPath);
        Type core = client.GetType("Anomaly.Client.Core", throwOnError: true)!;
        object instance = Activator.CreateInstance(core)!;
        Console.WriteLine("PASS Client assembly loaded and Core constructed before Bootstrap validation");
        MethodInfo pullUp = core.GetMethod("PullUpBootstraps", BindingFlags.NonPublic | BindingFlags.Static)!;
        if ((pullUp.MethodImplementationFlags & MethodImplAttributes.NoInlining) == 0)
            throw new InvalidOperationException("The optional-dependency boundary must remain non-inlined.");
        if (mode == "failed")
        {
            Assembly bootstrap = AssemblyLoadContext.Default.LoadFromAssemblyPath(bootstrapPath);
            Type bootstrapCore = bootstrap.GetType("Anomaly.Bootstrap.Core", true)!;
            Type state = bootstrap.GetType("Anomaly.Bootstrap.StartupState", true)!;
            bootstrapCore.GetField("<State>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, Enum.Parse(state, "Failed"));
            if (!Equals(pullUp.Invoke(null, null), false))
                throw new InvalidOperationException("Failed Bootstrap was accepted.");
            Console.WriteLine("PASS failed Bootstrap returns false at startup guard");
        }
        else
        {
            try
            {
                pullUp.Invoke(null, null);
                throw new InvalidOperationException("Missing Bootstrap did not fail at the isolated call boundary.");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is FileNotFoundException missing &&
                missing.FileName?.Contains("Anomaly.Bootstrap", StringComparison.Ordinal) == true)
            {
                Console.WriteLine("PASS absent Bootstrap raises catchable FileNotFoundException at isolated call boundary");
            }
        }
        if (core.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) != null)
            throw new InvalidOperationException("Client initialization ran before Bootstrap acceptance.");
        Console.WriteLine("PASS invoking real early lifecycle; expected process exit code 78");
        core.GetMethod("OnEarlyInitializeMelon", BindingFlags.Public | BindingFlags.Instance)!.Invoke(instance, null);
        throw new InvalidOperationException("Failed startup returned instead of terminating with exit code 78.");
    }

    private static bool CaptureError(string txt)
    {
        Console.WriteLine("CLIENT ERROR: " + txt);
        return false;
    }
}
