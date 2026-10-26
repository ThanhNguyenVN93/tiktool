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

    public static string CookieFile => Path.Combine(AppDataDir, "cookie.json");
    public static string SettingsFile => Path.Combine(AppDataDir, "settings.dat");
    public static string HistoryFile => Path.Combine(AppDataDir, "history.dat");
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
}
