using Microsoft.Playwright;
using TikTool.Models;

namespace TikTool.Browser;

/// <summary>The browser the tool drives. Two implementations: the installed Chrome/Edge as a separate process
/// (<see cref="ChromeBrowserHost"/>) and a WebView2 window owned by the app (<see cref="WebViewBrowserHost"/>). Both are driven the
/// same way: Playwright attaches over CDP to a local debugging port, so the services above do not care which one they got.</summary>
internal interface IBrowserHost : IAsyncDisposable
{
    /// <summary>False once the browser window was closed or the process exited.</summary>
    bool IsRunning { get; }

    /// <summary>True when the background-reading page is a single shared one (WebView2), so reads must run one at a time.
    /// False when every call gets its own tab (Chrome) and reads may run in parallel.</summary>
    bool WorkerRunsOneAtATime { get; }

    /// <summary>Starts the browser with a persistent profile folder and returns the Playwright context attached to it.</summary>
    Task<IBrowserContext> StartAsync(IPlaywright playwright, string profileDir, string windowTitle, CancellationToken ct);

    /// <summary>The page the work happens on. With WebView2 the context also holds pages of other controls (for example the sign-in view
    /// on the Accounts tab), so "the first page" is not necessarily the right one.</summary>
    Task<IPage> GetMainPageAsync(CancellationToken ct);

    /// <summary>A page for background reading while the main page stays where it is. Chrome: a new tab per call. WebView2: one second,
    /// hidden control inside the same window (Playwright's own NewPage would open a visible pop-up window there); ask once and reuse it.</summary>
    Task<IPage> NewWorkerPageAsync(CancellationToken ct);
}

internal static class BrowserHosts
{
    public static IBrowserHost Create(bool embedded) => embedded ? new WebViewBrowserHost() : new ChromeBrowserHost();

    /// <summary>The setting says "built-in browser" AND the WebView2 runtime is there (it ships with Windows 11 and current Windows 10).</summary>
    public static bool UseEmbedded(AppSettings settings) => settings.UseEmbeddedBrowser && EmbeddedAvailable();

    public static bool EmbeddedAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception ex)
        {
            TikTool.Services.AppLog.Write("WARN", "WebView2 runtime check failed: " + ex.Message);
            return false;
        }
    }
}