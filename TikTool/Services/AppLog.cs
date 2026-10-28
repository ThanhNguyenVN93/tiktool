namespace TikTool.Services;

/// <summary>Besides the per-session file, a line can also go to one persistent file per kind of event, so one place answers
/// "what happened with TikTok / YouTube / captchas / updates / downloads / tool errors" across many runs.</summary>
public enum LogCategory
{
    None,
    TikTok,
    YouTube,
    Challenges,
    Update,
    Download,
    Errors,
}

/// <summary>Per-session log file: every run writes to its OWN new file (never renamed or overwritten), so the log of the latest run can always
/// be read back. Only the newest 15 sessions are kept. On top of that, lines tagged with a <see cref="LogCategory"/> are appended to a
/// persistent file (tiktok.log, youtube.log, challenges.log, update.log, download.log, errors.log; one rotation at 1 MB), and every ERROR or
/// FATAL line from anywhere also lands in errors.log. The persistent files carry the full date because they span many days.</summary>
internal static class AppLog
{
    private const long MaxCategoryBytes = 1_000_000;

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

    public static string CategoryPath(LogCategory category) => Path.Combine(AppPaths.LogDir, category switch
    {
        LogCategory.TikTok => "tiktok.log",
        LogCategory.YouTube => "youtube.log",
        LogCategory.Challenges => "challenges.log",
        LogCategory.Update => "update.log",
        LogCategory.Download => "download.log",
        _ => "errors.log",
    });

    public static void Write(string level, string text) => Write(level, text, LogCategory.None);

    public static void Write(string level, string text, LogCategory category)
    {
        var now = DateTime.Now;
        lock (Gate)
        {
            if (_file is not null)
            {
                try { File.AppendAllText(_file, $"{now:HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}"); }
                catch (Exception ex) { LastWriteError = ex.Message; }
            }

            var full = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}";
            if (category != LogCategory.None)
            {
                AppendTo(category, full);
            }

            if (category != LogCategory.Errors && level is "ERROR" or "FATAL")
            {
                AppendTo(LogCategory.Errors, full);
            }
        }
    }

    private static void AppendTo(LogCategory category, string line)
    {
        try
        {
            var path = CategoryPath(category);
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxCategoryBytes)
            {
                File.Move(path, path + ".1", overwrite: true);
            }

            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            LastWriteError = ex.Message;
        }
    }
}