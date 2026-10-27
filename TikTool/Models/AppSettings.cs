namespace TikTool.Models;

public class AppSettings
{
    // Download
    public string DefaultOutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TikTool");
    public string DefaultFormat { get; set; } = "mp4";
    public int ConcurrentDownloads { get; set; } = 2;
    public bool AutoUpdateYtDlp { get; set; } = true;
    // "chrome" | "edge" | "firefox" | "none"
    public string CookieBrowser { get; set; } = "chrome";

    // Unfollow
    public int UnfollowDelayMinMs { get; set; } = 2000;
    public int UnfollowDelayMaxMs { get; set; } = 5000;
    public int UnfollowSessionLimit { get; set; } = 50;
    public bool PreferApiOverPlaywright { get; set; } = false;
    public bool SkipFriends { get; set; } = true;
    public bool NotifyOnRateLimit { get; set; } = true;

    // Rate limit & daily quota
    public bool AutoWaitOnRateLimit { get; set; } = true;
    public int RateLimitWaitSeconds { get; set; } = 90;
    public int MaxUnfollowsPerDay { get; set; } = 100;

    // Browser: false = the installed Chrome/Edge as a separate window, true = the built-in WebView2 window
    public bool UseEmbeddedBrowser { get; set; }

    // App
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public string Theme { get; set; } = "Dark";
    // "vi" | "en"
    public string Language { get; set; } = "vi";
}
