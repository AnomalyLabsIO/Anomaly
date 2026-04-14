namespace Anomaly.Installer;

public static class ConsoleUI
{
    private const int BarWidth = 30;

    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("========================================");
        Console.WriteLine("       Anomaly Client Installer");
        Console.WriteLine("========================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void PrintStep(int stepNumber, int totalSteps, string description)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"[{stepNumber}/{totalSteps}] ");
        Console.ResetColor();
        Console.WriteLine(description);
    }

    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARNING] {message}");
        Console.ResetColor();
    }

    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    public static string PromptPath(string prompt)
    {
        Console.Write($"{prompt}: ");
        string? input = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(input))
            throw new InstallerException("No path provided.");
        return input;
    }

    public static bool PromptYesNo(string question)
    {
        Console.Write($"{question} (y/n): ");
        string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return input is "y" or "yes";
    }

    /// <summary>
    /// Renders a progress bar on the current line.
    /// Example: "  Copying files    [████████████░░░░░░░░░░░░░░░░░░] 42%  (1,234 / 2,345)"
    /// </summary>
    public static void PrintProgressBar(string label, long current, long total, string? suffix = null)
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

        // Pad with spaces to clear any leftover characters from previous longer lines
        Console.Write("   ");

        if (current >= total)
            Console.WriteLine();
    }

    /// <summary>
    /// Formats a byte count as a human-readable size string.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
