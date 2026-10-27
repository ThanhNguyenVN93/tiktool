namespace TikTool.Services;

internal static class AppPaths
{
    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TikTool");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CookieFile => Path.Combine(AppDataDir, "cookie.dat");
    public static string SettingsFile => Path.Combine(AppDataDir, "settings.dat");
    public static string HistoryFile => Path.Combine(AppDataDir, "history.dat");
    public static string LastPostCacheFile => Path.Combine(AppDataDir, "lastpost.dat");
    public static string SelectorsFile => Path.Combine(AppContext.BaseDirectory, "Config", "selectors.json");

    public static string DebugDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "debug");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SessionFile => Path.Combine(AppDataDir, "session_resume.dat");
    public static string WhitelistFile => Path.Combine(AppDataDir, "whitelist.dat");

    public static string LogDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>One-time migration: rename legacy .json files to .dat so Kaspersky's
    /// ransomware-protection layer no longer silently blocks File.Move overwrites.</summary>
    public static void MigrateLegacyFiles()
    {
        TryRename(Path.Combine(AppDataDir, "cookie.json"), CookieFile);
    }

    private static void TryRename(string from, string to)
    {
        try
        {
            if (File.Exists(from) && !File.Exists(to))
                File.Move(from, to);
        }
        catch { /* best effort — sẽ retry lần sau */ }
    }
}
