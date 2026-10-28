using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>Drives the user's own Chrome (a persistent, tool-only profile: no cookie reading, no OAuth) on YouTube.
/// One connection serves the whole page: the subscriptions list stays open in the first tab, upload ages are read in
/// a second tab, and unsubscribing happens on the list tab. Verified live (see the probe notes in the git history):
/// Google allows the sign-in, the list reads in seconds, the Videos tab gives a coarse age ("3y ago") because the RSS
/// feed is unreliable, and a DOM click is needed because Playwright refuses the "hidden" unsubscribe button. YouTube's
/// Trusted Types policy blocks string-eval, so only function-style evaluate is used (never WaitForFunction).</summary>
internal sealed class YouTubeService : IAsyncDisposable
{
    private const string FeedUrl = "https://www.youtube.com/feed/channels?hl=en";
    private const string SignInUrl = "https://accounts.google.com/ServiceLogin?service=youtube&hl=en&continue=https%3A%2F%2Fwww.youtube.com%2Ffeed%2Fchannels%3Fhl%3Den";

    private Process? _chrome;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _feedPage;
    private IPage? _agePage;

    public bool IsConnected => _feedPage is not null;

    // ---------------------------------------------------------------------------------------------- connect

    public async Task ConnectAsync(IProgress<string> log, CancellationToken ct)
    {
        if (IsConnected)
        {
            return;
        }

        var chromePath = PlaywrightService.FindRealChromeExecutable()
            ?? throw new InvalidOperationException(T("Không tìm thấy Google Chrome/Edge trên máy.", "Google Chrome/Edge was not found on this computer."));

        try
        {
            var port = GetFreePort();
            _chrome = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{AppPaths.YouTubeProfileDir}\" --no-first-run --no-default-browser-check --window-size=1280,900 about:blank",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException(T("Không khởi chạy được Chrome.", "Could not start Chrome."));

            _playwright = await Playwright.CreateAsync();
            _browser = await ConnectOverCdpAsync(_playwright, port, ct);
            _context = _browser.Contexts.FirstOrDefault() ?? await _browser.NewContextAsync();
            var page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();

            await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            if (await IsSignedInAsync(page))
            {
                log.Report(T("✓ Đã đăng nhập YouTube (profile riêng nhớ phiên).", "✓ Signed in to YouTube (the tool's own profile remembered the login)."));
            }
            else
            {
                log.Report(T("Chưa đăng nhập — mở trang đăng nhập Google. Hãy đăng nhập trong cửa sổ Chrome vừa mở (tối đa 5 phút)...",
                             "Not signed in — opening the Google sign-in page. Please sign in in the Chrome window that just opened (up to 5 minutes)..."));
                await page.GotoAsync(SignInUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

                var signedIn = false;
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline && !signedIn)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(2000, ct);
                    try
                    {
                        signedIn = page.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) && await IsSignedInAsync(page);
                    }
                    catch (PlaywrightException)
                    {
                        // mid-navigation, check again on the next tick
                    }
                }

                if (!signedIn)
                {
                    throw new InvalidOperationException(T("Chưa đăng nhập sau 5 phút.", "Not signed in within 5 minutes."));
                }

                log.Report(T("✓ Đã đăng nhập.", "✓ Signed in."));
            }

            _feedPage = page;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------------------------------- list

    /// <summary>Opens (or reloads) the subscriptions list, scrolls until every channel is loaded and reads the rows.</summary>
    public async Task<List<YouTubeChannel>> LoadSubscriptionsAsync(IProgress<string> log, CancellationToken ct)
    {
        var page = _feedPage ?? throw new InvalidOperationException("Not connected.");
        await page.BringToFrontAsync();
        await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        try
        {
            await page.WaitForSelectorAsync("ytd-channel-renderer", new PageWaitForSelectorOptions { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            log.Report(T("Không thấy kênh nào trong danh sách đăng ký (tài khoản chưa đăng ký kênh nào, hoặc YouTube đã đổi giao diện).",
                         "No channels found in the subscriptions list (the account has none, or YouTube changed the page)."));
            await DumpPageAsync(page, "youtube-feed", log);
            return new List<YouTubeChannel>();
        }

        var previous = -1;
        var stable = 0;
        for (var i = 0; i < 400 && stable < 3; i++)
        {
            ct.ThrowIfCancellationRequested();
            var count = await page.EvaluateAsync<int>("() => document.querySelectorAll('ytd-channel-renderer').length");
            stable = count == previous ? stable + 1 : 0;
            previous = count;
            await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
            await Task.Delay(700, ct);
            if (i % 10 == 9)
            {
                log.Report(T($"Đang tải danh sách... {count} kênh", $"Loading the list... {count} channels"));
            }
        }

        var json = await page.EvaluateAsync<string>(@"() => JSON.stringify([...document.querySelectorAll('ytd-channel-renderer')].map(r => {
            const a = r.querySelector('a#main-link') || r.querySelector('a[href^=""/""]') || r.querySelector('a');
            const nameEl = r.querySelector('ytd-channel-name #text') || r.querySelector('ytd-channel-name');
            const img = r.querySelector('yt-img-shadow img') || r.querySelector('img');
            const metaEl = r.querySelector('#metadata');
            return {
                name: ((nameEl || {}).textContent || '').replace(/\s+/g, ' ').trim(),
                href: a ? a.href : '',
                img: img ? (img.src || '') : '',
                meta: (metaEl ? metaEl.innerText : '').replace(/\s+/g, ' ').trim()
            };
        }))");

        var list = new List<YouTubeChannel>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var href = el.GetProperty("href").GetString() ?? "";
            if (string.IsNullOrEmpty(href))
            {
                continue;
            }

            var avatar = el.GetProperty("img").GetString() ?? "";
            var meta = el.GetProperty("meta").GetString() ?? "";
            var subs = Regex.Match(meta, @"([\d.,]+\s*[KMB]?)\s+subscribers?", RegexOptions.IgnoreCase);

            list.Add(new YouTubeChannel
            {
                Name = el.GetProperty("name").GetString() ?? "",
                Url = href,
                Handle = HandleFromUrl(href),
                AvatarUrl = avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? avatar : "",
                Subscribers = subs.Success ? subs.Groups[1].Value.Trim() + " " + T("người đăng ký", "subscribers") : "",
            });
        }

        log.Report(T($"✓ Đã tải {list.Count} kênh đăng ký.", $"✓ Loaded {list.Count} subscribed channels."));
        return list;
    }

    private static string HandleFromUrl(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath.Trim('/');
            return path.StartsWith("@", StringComparison.Ordinal) ? path : path.Split('/').LastOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    // ---------------------------------------------------------------------------------------------- upload age

    public readonly record struct AgeResult(long? LastUploadUnix, string Text, bool NoVideos);

    /// <summary>Reads how long ago the newest video was posted from the channel's Videos tab (default sort: Latest) in a
    /// second tab. Approximate by design: YouTube shows "13h ago", "5d ago", "9mo ago", "3y ago".</summary>
    public async Task<AgeResult> ReadLatestUploadAsync(string channelUrl, CancellationToken ct)
    {
        var context = _context ?? throw new InvalidOperationException("Not connected.");
        _agePage ??= await context.NewPageAsync();
        var page = _agePage;
        await page.BringToFrontAsync();

        var path = new Uri(channelUrl).AbsolutePath.TrimEnd('/');
        await page.GotoAsync($"https://www.youtube.com{path}/videos?hl=en", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        // The grid fills in after the page loads, so poll until an age line exists (function-style evaluate only).
        var waitUntil = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < waitUntil)
        {
            ct.ThrowIfCancellationRequested();
            var present = await page.EvaluateAsync<bool>(
                "() => /(\\d+)\\s*(second|minute|hour|day|week|month|year|sec|min|mo|[smhdwy])s?\\s+ago/.test(document.body ? document.body.innerText : '')");
            if (present)
            {
                break;
            }

            await Task.Delay(400, ct);
        }

        var text = await page.EvaluateAsync<string>(
            "() => { const g = document.querySelector('ytd-rich-grid-renderer'); return (g && g.innerText.trim()) ? g.innerText : (document.body ? document.body.innerText : ''); }");
        var m = Regex.Match(text, @"(\d+)\s*(second|minute|hour|day|week|month|year|sec|min|mo|[smhdwy])s?\s+ago");
        if (m.Success)
        {
            var days = ApproxDays(int.Parse(m.Groups[1].Value), m.Groups[2].Value);
            return new AgeResult(DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds(), m.Value, false);
        }

        if (text.Contains("doesn't have any content", StringComparison.OrdinalIgnoreCase))
        {
            return new AgeResult(null, "", true);
        }

        throw new InvalidOperationException(T("Không đọc được tuổi video trên trang kênh.", "Could not read the upload age on the channel page."));
    }

    private static int ApproxDays(int amount, string unit) => unit.ToLowerInvariant() switch
    {
        "y" or "year" => amount * 365,
        "mo" or "month" => amount * 30,
        "w" or "week" => amount * 7,
        "d" or "day" => amount,
        _ => 0, // seconds, minutes, hours
    };

    // ---------------------------------------------------------------------------------------------- unsubscribe

    /// <summary>Unsubscribes one channel on the list tab: finds its row by link, DOM-clicks the (hidden) subscribed button,
    /// confirms YouTube's dialog and waits for the server's HTTP answer, which is the only real proof it worked (the
    /// button flips instantly in the UI, and navigating away too early cancels the request).</summary>
    public async Task<(bool Ok, string? Error)> UnsubscribeAsync(YouTubeChannel channel, CancellationToken ct)
    {
        var page = _feedPage ?? throw new InvalidOperationException("Not connected.");
        await page.BringToFrontAsync();

        var index = await FindRowIndexAsync(page, channel.Url);
        if (index < 0)
        {
            return (false, T("Không tìm thấy kênh trong danh sách đang mở (hãy bấm Tải lại).", "The channel is not in the open list (press Reload)."));
        }

        var row = page.Locator("ytd-channel-renderer").Nth(index);
        if (await row.Locator("ytd-subscribe-button-renderer[subscribed]").CountAsync() == 0)
        {
            return (true, null); // already not subscribed
        }

        var button = row.Locator("ytd-subscribe-button-renderer button").First;
        var response = page.WaitForResponseAsync(
            r => r.Url.Contains("/subscription/unsubscribe", StringComparison.OrdinalIgnoreCase),
            new PageWaitForResponseOptions { Timeout = 15_000 });
        _ = response.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            await button.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 3_000 });
        }
        catch (TimeoutException)
        {
            // a DOM click does not need the element in view
        }

        await button.EvaluateAsync("b => b.click()");

        string[] selectors =
        {
            "yt-confirm-dialog-renderer #confirm-button button",
            "button[aria-label=\"Unsubscribe\"]",
            "#confirm-button button",
        };
        var clicked = false;
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until && !clicked)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var selector in selectors)
            {
                var candidate = page.Locator(selector).First;
                if (await candidate.CountAsync() > 0 && await candidate.IsVisibleAsync())
                {
                    try
                    {
                        await candidate.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    }
                    catch (TimeoutException)
                    {
                        await candidate.EvaluateAsync("b => b.click()");
                    }

                    clicked = true;
                    break;
                }
            }

            if (!clicked)
            {
                await Task.Delay(300, ct);
            }
        }

        if (!clicked)
        {
            return (false, T("YouTube không hiện hộp xác nhận hủy đăng ký.", "YouTube did not show its unsubscribe confirmation."));
        }

        try
        {
            var answer = await response;
            if (!answer.Ok)
            {
                return (false, $"HTTP {answer.Status}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, T("Không thấy phản hồi của máy chủ YouTube.", "No answer from YouTube's server."));
        }

        await Task.Delay(500, ct);
        return (true, null);
    }

    private static async Task<int> FindRowIndexAsync(IPage page, string href)
    {
        return await page.EvaluateAsync<int>(
            @"(target) => {
                const rows = [...document.querySelectorAll('ytd-channel-renderer')];
                return rows.findIndex(r => {
                    const a = r.querySelector('a#main-link') || r.querySelector('a[href^=""/""]') || r.querySelector('a');
                    return a && a.href === target;
                });
            }",
            href);
    }

    // ---------------------------------------------------------------------------------------------- helpers

    private static async Task<bool> IsSignedInAsync(IPage page)
    {
        try
        {
            // The account menu renders a moment after the masthead; deciding too early reported a false "not signed in".
            await page.WaitForSelectorAsync("ytd-masthead button#avatar-btn, ytd-masthead a[href*='accounts.google.com']", new PageWaitForSelectorOptions { Timeout = 15_000 });
            return await page.EvaluateAsync<bool>(
                "() => !!document.querySelector('ytd-masthead button#avatar-btn') && !document.querySelector('ytd-masthead a[href*=\"accounts.google.com\"]')");
        }
        catch
        {
            return false;
        }
    }

    private static async Task DumpPageAsync(IPage page, string label, IProgress<string> log)
    {
        try
        {
            var baseName = Path.Combine(AppPaths.DebugDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{label}");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = baseName + ".png" });
            await File.WriteAllTextAsync(baseName + ".html", await page.ContentAsync());
            log.Report($"   (diagnosis saved: {baseName}.png / .html)");
        }
        catch
        {
            // diagnostics must never break the caller
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<IBrowser> ConnectOverCdpAsync(IPlaywright playwright, int port, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{port}");
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500, ct);
            }
        }

        throw new InvalidOperationException(
            T($"Không kết nối được vào Chrome (cổng {port}). Nếu một cửa sổ Chrome của tool đang mở sẵn, hãy đóng nó rồi thử lại.",
              $"Could not connect to Chrome (port {port}). If a Chrome window opened by this tool is already open, close it and try again."), last);
    }

    /// <summary>Quits Chrome through its own CDP "Browser.close" so the profile is flushed cleanly (the sign-in cookies
    /// survive); a hard kill can lose the newest cookies. Falls back to killing the process.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            var page = _feedPage ?? _agePage ?? _context?.Pages.FirstOrDefault();
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
            if (_chrome is not null && !_chrome.WaitForExit(6000))
            {
                _chrome.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }

        try { _playwright?.Dispose(); } catch { /* best effort */ }

        _chrome = null;
        _playwright = null;
        _browser = null;
        _context = null;
        _feedPage = null;
        _agePage = null;
    }
}