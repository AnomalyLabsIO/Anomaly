namespace Anomaly.Installer;

/// <summary>
/// Tees everything written to Console.Out and Console.Error into a timestamped log file
/// under %APPDATA%/Anomaly/Logs/. Enables "what happened during install?" forensics
/// without changing every ConsoleUI callsite.
/// </summary>
internal static class InstallLogger
{
    private static StreamWriter? _fileWriter;
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;

    public static string? LogPath { get; private set; }

    /// <summary>
    /// Initializes logging. Returns the full log file path, or null on failure (logging
    /// is best-effort — if we can't write a log, the install still proceeds).
    /// </summary>
    public static string? Init()
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Anomaly",
                "Logs");
            Directory.CreateDirectory(logDir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            LogPath = Path.Combine(logDir, $"install-{stamp}.log");

            _fileWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
            _originalOut = Console.Out;
            _originalError = Console.Error;

            Console.SetOut(new TeeWriter(_originalOut, _fileWriter));
            Console.SetError(new TeeWriter(_originalError, _fileWriter));

            return LogPath;
        }
        catch
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
            return null;
        }
    }

    public static void Shutdown()
    {
        if (_originalOut != null) Console.SetOut(_originalOut);
        if (_originalError != null) Console.SetError(_originalError);
        _fileWriter?.Dispose();
        _fileWriter = null;
    }

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _file;

        public TeeWriter(TextWriter console, TextWriter file)
        {
            _console = console;
            _file = file;
        }

        public override System.Text.Encoding Encoding => _console.Encoding;

        public override void Write(char value)
        {
            _console.Write(value);
            try { _file.Write(value); } catch { /* logging is best-effort */ }
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            try { _file.Write(value); } catch { /* logging is best-effort */ }
        }

        public override void WriteLine(string? value)
        {
            _console.WriteLine(value);
            try { _file.WriteLine(value); } catch { /* logging is best-effort */ }
        }
    }
}
