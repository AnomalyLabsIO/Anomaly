namespace Anomaly.InstallerCore;

/// <summary>
/// Legacy static entry point used throughout installer business logic. Delegates to
/// <see cref="Current"/>, which defaults to a console-backed sink and can be swapped
/// by a host (e.g. the Launcher) via <see cref="SetSink"/>.
/// </summary>
public static class ConsoleUI
{
    private static IInstallerUI _sink = new ConsoleInstallerUI();

    /// <summary>
    /// Install a host-specific sink. Returns the previous sink so callers can restore
    /// it when their scope ends.
    /// </summary>
    public static IInstallerUI SetSink(IInstallerUI sink)
    {
        var previous = _sink;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        return previous;
    }

    public static IInstallerUI Current => _sink;

    public static void PrintBanner() => _sink.Banner();
    public static void PrintStep(int stepNumber, int totalSteps, string description) =>
        _sink.StepStarted(stepNumber, totalSteps, description);
    public static void PrintSuccess(string message) => _sink.Log(InstallerLogLevel.Success, message);
    public static void PrintWarning(string message) => _sink.Log(InstallerLogLevel.Warning, message);
    public static void PrintError(string message) => _sink.Log(InstallerLogLevel.Error, message);
    public static void PrintInfo(string message) => _sink.Log(InstallerLogLevel.Info, message);
    public static string PromptPath(string prompt) => _sink.PromptPath(prompt);
    public static bool PromptYesNo(string question) => _sink.PromptYesNo(question);
    public static void PrintProgressBar(string label, long current, long total, string? suffix = null) =>
        _sink.Progress(label, current, total, suffix);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

/// <summary>
/// Default CLI sink. Identical behavior to the previous static ConsoleUI implementation.
/// </summary>
public sealed class ConsoleInstallerUI : IInstallerUI
{
    private const int BarWidth = 30;

    public void Banner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("========================================");
        Console.WriteLine("       Anomaly Client Installer");
        Console.WriteLine("========================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    public void StepStarted(int stepNumber, int totalSteps, string description)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"[{stepNumber}/{totalSteps}] ");
        Console.ResetColor();
        Console.WriteLine(description);
    }

    public void Log(InstallerLogLevel level, string message)
    {
        switch (level)
        {
            case InstallerLogLevel.Success:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(message);
                break;
            case InstallerLogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARNING] {message}");
                break;
            case InstallerLogLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {message}");
                break;
            default:
                Console.WriteLine(message);
                break;
        }
        Console.ResetColor();
    }

    public void Progress(string label, long current, long total, string? suffix = null)
    {
        if (total <= 0) return;

        int percent = (int)(current * 100 / total);
        int filled = (int)(current * BarWidth / total);
        int empty = BarWidth - filled;

        Console.Write($"\r  {label,-18} [");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(new string('#', filled));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', empty));
        Console.ResetColor();
        Console.Write($"] {percent,3}%");

        if (suffix != null)
            Console.Write($"  {suffix}");

        Console.Write("   ");

        if (current >= total)
            Console.WriteLine();
    }

    public string PromptPath(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? input = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(input))
            throw new InstallerException("No path provided.");
        return input;
    }

    public bool PromptYesNo(string question)
    {
        Console.Write($"{question} (y/n): ");
        string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return input is "y" or "yes";
    }
}
