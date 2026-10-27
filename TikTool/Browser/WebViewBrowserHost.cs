using Microsoft.Playwright;

namespace TikTool.Browser;

/// <summary>A WebView2 window owned by the app (see <see cref="BrowserWindow"/>): no Chrome needed, the sign-in happens inside an app window.
/// Verified in the spike: Google and TikTok accept the sign-in, Playwright attaches over CDP, hidden reading works (about 3x slower).</summary>
internal sealed class WebViewBrowserHost : IBrowserHost
{
    private BrowserWindow? _window;
    private IBrowserContext? _context;

    public bool IsRunning => _window is { IsOpen: true };

    public async Task<IBrowserContext> StartAsync(IPlaywright playwright, string profileDir, string windowTitle, CancellationToken ct)
    {
        if (!BrowserHosts.EmbeddedAvailable())
        {
            throw new InvalidOperationException(
                T("Máy chưa có WebView2 Runtime. Tải tại https://developer.microsoft.com/microsoft-edge/webview2/ hoặc tắt \"Dùng trình duyệt tích hợp\" trong Cài đặt.",
                  "The WebView2 Runtime is not installed. Get it from https://developer.microsoft.com/microsoft-edge/webview2/ or turn off \"Use the built-in browser\" in Settings."));
        }

        var port = BrowserCdp.GetFreePort();
        _window = await UiThread.InvokeAsync(() => BrowserWindow.OpenAsync(profileDir, port, windowTitle));

        var browser = await BrowserCdp.ConnectAsync(playwright, port, ct);
        _context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException(T("Trình duyệt tích hợp chưa sẵn sàng.", "The built-in browser is not ready."));
        return _context;
    }

    public async Task<IPage> NewWorkerPageAsync(CancellationToken ct)
    {
        var context = _context ?? throw new InvalidOperationException("Not started.");
        var window = _window ?? throw new InvalidOperationException("Not started.");

        var marker = "tiktool-worker-" + Guid.NewGuid().ToString("N");
        await UiThread.InvokeAsync(() => window.EnsureWorkerAsync(marker));

        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = context.Pages.FirstOrDefault(p => p.Url.Contains(marker, StringComparison.Ordinal));
            if (page is not null)
            {
                return page;
            }

            await Task.Delay(250, ct);
        }

        throw new InvalidOperationException(T("Không tạo được khung đọc nền của trình duyệt tích hợp.", "Could not create the built-in browser's background reading frame."));
    }

    public async ValueTask DisposeAsync()
    {
        var window = _window;
        _window = null;
        _context = null;
        if (window is { IsDisposed: false })
        {
            try
            {
                await UiThread.InvokeAsync(() =>
                {
                    window.Close();
                    return Task.CompletedTask;
                });
            }
            catch
            {
                // best effort
            }
        }
    }
}