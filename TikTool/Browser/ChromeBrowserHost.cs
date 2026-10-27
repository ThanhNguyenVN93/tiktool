using System.Diagnostics;
using Microsoft.Playwright;
using TikTool.Services;

namespace TikTool.Browser;

/// <summary>The installed Chrome/Edge, started as its own process with only a debugging port (no automation flags, TikTok blocks those)
/// and its own throwaway-or-persistent profile folder.</summary>
internal sealed class ChromeBrowserHost : IBrowserHost
{
    private Process? _process;
    private IBrowserContext? _context;

    public bool IsRunning => _process is { HasExited: false };

    public async Task<IBrowserContext> StartAsync(IPlaywright playwright, string profileDir, string windowTitle, CancellationToken ct)
    {
        var chromePath = PlaywrightService.FindRealChromeExecutable()
            ?? throw new InvalidOperationException(T("Không tìm thấy Google Chrome/Edge trên máy.", "Google Chrome/Edge was not found on this computer."));

        var port = BrowserCdp.GetFreePort();
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check --window-size=1280,900 about:blank",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException(T("Không khởi chạy được Chrome.", "Could not start Chrome."));

        var browser = await BrowserCdp.ConnectAsync(playwright, port, ct);
        _context = browser.Contexts.FirstOrDefault() ?? await browser.NewContextAsync();
        return _context;
    }

    public Task<IPage> NewWorkerPageAsync(CancellationToken ct) =>
        (_context ?? throw new InvalidOperationException("Not started.")).NewPageAsync();

    /// <summary>Quits Chrome through its own CDP "Browser.close" so the profile is flushed cleanly (the sign-in cookies survive);
    /// a hard kill can lose the newest cookies. Falls back to killing the process.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            var page = _context?.Pages.FirstOrDefault();
            if (page is not null)
            {
                var session = await page.Context.NewCDPSessionAsync(page);
                await session.SendAsync("Browser.close");
            }
        }
        catch
        {
            // fall through to the process-level cleanup
        }

        try
        {
            if (_process is not null && !_process.WaitForExit(6000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }

        _process = null;
        _context = null;
    }
}