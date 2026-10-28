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

    public static string AvatarDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "avatars");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string YouTubeAgesFile => Path.Combine(AppDataDir, "youtube_ages.dat");
    public static string YouTubeProtectedFile => Path.Combine(AppDataDir, "youtube_protected.dat");

    public static string YouTubeBackupDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "youtube_backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string YouTubeProfileDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "youtube_profile");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>User-data folder of the built-in (WebView2) browser for YouTube; kept apart from the Chrome profile because the two engines lay out their folders differently.</summary>
    public static string YouTubeWebViewDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "youtube_webview");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>User-data folder of the TikTok sign-in browser (WebView2). The session cookies themselves are kept in the tool's own cookie file.</summary>
    public static string TikTokWebViewDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "tiktok_webview");
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
