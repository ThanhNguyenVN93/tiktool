using Microsoft.Web.WebView2.Core;

namespace TikTool.Browser;

/// <summary>One WebView2 environment per profile folder, created once and kept for the life of the app. A user-data folder can only be opened with one
/// set of options, so the sign-in view on the Accounts tab and the window that does the work must share it: both get their controls from here.
/// The environment also fixes the debugging port Playwright attaches to.</summary>
internal sealed class WebViewSession
{
    private Task<CoreWebView2Environment>? _environment;

    public WebViewSession(string profileDir)
    {
        ProfileDir = profileDir;
        Port = BrowserCdp.GetFreePort();
    }

    public string ProfileDir { get; }

    public int Port { get; }

    /// <summary>Call on the UI thread.</summary>
    public Task<CoreWebView2Environment> GetEnvironmentAsync() => _environment ??= CreateAsync();

    private Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(ProfileDir);
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = $"--remote-debugging-port={Port}" };
        return CoreWebView2Environment.CreateAsync(null, ProfileDir, options);
    }
}

internal static class WebViewSessions
{
    private static readonly Dictionary<string, WebViewSession> Sessions = new(StringComparer.OrdinalIgnoreCase);

    public static WebViewSession For(string profileDir)
    {
        lock (Sessions)
        {
            if (!Sessions.TryGetValue(profileDir, out var session))
            {
                session = new WebViewSession(profileDir);
                Sessions[profileDir] = session;
            }

            return session;
        }
    }
}