namespace TikTool.Services;

/// <summary>Per-session log file: every run writes to its OWN new file (never renamed or overwritten,
/// which security software has been seen silently blocking), so the log of the latest run can always
/// be read back. Only the newest 15 sessions are kept.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _file;

    public static string? CurrentFile => _file;
    public static string? LastWriteError { get; private set; }

    public static void Initialize()
    {
        try
        {
            var dir = AppPaths.LogDir;
            _file = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            foreach (var old in new DirectoryInfo(dir).GetFiles("session_*.log").OrderByDescending(f => f.Name).Skip(14))
            {
                try { old.Delete(); } catch { /* best effort */ }
            }

            var version = typeof(AppLog).Assembly.GetName().Version;
            Write("INFO", $"=== TikTool session start | v{version} | {Environment.OSVersion} | {Environment.ProcessPath} ===");
        }
        catch (Exception ex)
        {
            LastWriteError = ex.Message;
        }
    }

    public static void Write(string level, string text)
    {
        var file = _file;
        if (file is null)
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}";
        lock (Gate)
        {
            try { File.AppendAllText(file, line); }
            catch (Exception ex) { LastWriteError = ex.Message; }
        }
    }
}