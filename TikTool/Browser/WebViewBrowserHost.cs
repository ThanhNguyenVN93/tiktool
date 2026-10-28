using Microsoft.Playwright;

namespace TikTool.Browser;

/// <summary>A WebView2 window owned by the app (see <see cref="BrowserWindow"/>): no Chrome needed. Verified in the spike: Google and TikTok accept the
/// sign-in, Playwright attaches over CDP, hidden reading works (about 3x slower than visible).</summary>
internal sealed class WebViewBrowserHost : IBrowserHost
{
    private BrowserWindow? _window;
    private IBrowserContext? _context;
    private IPage? _worker;

    public bool IsRunning => _window is { IsOpen: true };

    public bool WorkerRunsOneAtATime => true;

    public async Task<IBrowserContext> StartAsync(IPlaywright playwright, string profileDir, string windowTitle, CancellationToken ct)
    {
        if (!BrowserHosts.EmbeddedAvailable())
        {
            throw new InvalidOperationException(
                T("Máy chưa có WebView2 Runtime. Tải tại https://developer.microsoft.com/microsoft-edge/webview2/ hoặc tắt \"Dùng trình duyệt tích hợp\" trong Cài đặt.",
                  "The WebView2 Runtime is not installed. Get it from https://developer.microsoft.com/microsoft-edge/webview2/ or turn off \"Use the built-in browser\" in Settings."));
        }

        var session = WebViewSessions.For(profileDir);
        _window = await UiThread.InvokeAsync(() => BrowserWindow.OpenAsync(session, windowTitle));

        var browser = await BrowserCdp.ConnectAsync(playwright, session.Port, ct);
        _context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException(T("Trình duyệt tích hợp chưa sẵn sàng.", "The built-in browser is not ready."));
        return _context;
    }

    public async Task<IPage> GetMainPageAsync(CancellationToken ct)
    {
        var context = _context ?? throw new InvalidOperationException("Not started.");
        var window = _window ?? throw new InvalidOperationException("Not started.");
        return await FindPageAsync(context, window.MainMarker, ct);
    }

    public async Task<IPage> NewWorkerPageAsync(CancellationToken ct)
    {
        if (_worker is not null)
        {
            return _worker;
        }

        var context = _context ?? throw new InvalidOperationException("Not started.");
        var window = _window ?? throw new InvalidOperationException("Not started.");

        var marker = "tiktool-worker-" + Guid.NewGuid().ToString("N");
        await UiThread.InvokeAsync(() => window.EnsureWorkerAsync(marker));
        _worker = await FindPageAsync(context, marker, ct);
        return _worker;
    }

    private static async Task<IPage> FindPageAsync(IBrowserContext context, string marker, CancellationToken ct)
    {
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

        throw new InvalidOperationException(T("Không tìm thấy trang của trình duyệt tích hợp.", "Could not find the built-in browser's page."));
    }

    public async ValueTask DisposeAsync()
    {
        var window = _window;
        _window = null;
        _context = null;
        _worker = null;
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