namespace TikTool.Models;

public class AppSettings
{
    // Download
    public string DefaultOutputDir { get; set; } = TikTool.Services.AppPaths.DefaultDownloadDir;
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
    public bool UseEmbeddedBrowser { get; set; } = true;
    // The built-in browser's work window starts hidden (the page has a button to show it; it also appears by itself when a captcha needs the user)
    public bool RunBrowserHidden { get; set; }

    // Updates: look for a newer release when the app starts; download it in the background and install it the next time the app starts
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoInstallUpdates { get; set; } = false;

    // App
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public string Theme { get; set; } = "Dark";
    // "vi" | "en" (a fresh install starts in English; the user can switch in Settings)
    public string Language { get; set; } = "en";
}
