using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>
/// Everything here drives the user's REAL, installed Google Chrome via CDP (launched as a plain
/// `chrome.exe` process with only `--remote-debugging-port`, then Playwright attaches — no
/// `--enable-automation` or other flags Playwright's own launcher adds). This replaced an
/// earlier version that used Playwright's bundled Chromium via `Chromium.LaunchAsync`: live
/// testing (2026-09) found TikTok blocks that bundled Chromium outright — QR login never
/// rendered, and the (cookie-authenticated) Following page loaded but yielded 0 items even
/// though the account and cookie were both valid, i.e. it wasn't a bad selector, TikTok was
/// serving different/blocked content to the bot-fingerprinted browser. Real Chrome, merely
/// CDP-attached, was not blocked for either case.
/// <see cref="LaunchAsync"/> keeps a persistent session (own throwaway profile dir) for
/// <see cref="LoadFollowingListAsync"/>/<see cref="ClickUnfollowAsync"/>/<see cref="GetUserDetailAsync"/>;
/// <see cref="LoginInteractivelyAsync"/> runs an independent, separate one-shot session for manual login.
/// </summary>
public sealed class PlaywrightService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private Process? _chromeProcess;
    private string? _profileDir;

    public bool IsLaunched => _page is not null;

    public async Task LaunchAsync(TikTokCookie cookie, bool headless = false)
    {
        TryFlushDnsCache();

        var chromePath = FindRealChromeExecutable()
            ?? throw new InvalidOperationException(
                "Không tìm thấy Google Chrome thật đã cài trên máy (đã kiểm tra Program Files, Program Files (x86), và AppData\\Local\\Google\\Chrome).");

        var port = GetFreeTcpPort();
        _profileDir = Path.Combine(Path.GetTempPath(), $"tiktool_chrome_session_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_profileDir);

        // headless=new is Chrome's own modern headless mode (much closer to a real render than
        // the old one) — left off by default since visible-window is the only configuration
        // confirmed to work against TikTok so far; flip to true once headless is verified safe.
        var headlessArg = headless ? "--headless=new " : "";
        _chromeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = $"{headlessArg}--remote-debugging-port={port} --user-data-dir=\"{_profileDir}\" --no-first-run --no-default-browser-check about:blank",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Không khởi chạy được Chrome thật.");

        _playwright = await Playwright.CreateAsync();
        _browser = await ConnectWithRetryAsync(_playwright, port, CancellationToken.None);
        _context = _browser.Contexts.FirstOrDefault() ?? await _browser.NewContextAsync();
        await _context.AddCookiesAsync(BuildCookies(cookie));
        _page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();

        // Warm-up: this profile dir is brand new every launch (only cookies were injected, no
        // localStorage/device fingerprint), and navigating straight to a deep page like a profile
        // sometimes gets TikTok's "Something went wrong" bot-check page instead of real content.
        // Landing on the homepage first, like a real user would, measurably reduces that. Best
        // effort — a failure here shouldn't block the actual task.
        try
        {
            await _page.GotoAsync("https://www.tiktok.com/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
            await Task.Delay(800);
        }
        catch
        {
            // ignored — worst case we skip straight to the real navigation below
        }
    }

    /// <summary>Best-effort, silent `ipconfig /flushdns` before launching Chrome — the account
    /// owner reported this making TikTok noticeably faster in their own manual testing. Doesn't
    /// need admin rights (flushing the resolver cache is a normal-user operation on Windows).
    /// Fire-and-forget: never blocks launch on it, and a failure (missing ipconfig.exe, no
    /// permission, whatever) is silently ignored rather than failing the whole launch over a
    /// side-channel speed optimization.</summary>
    private static void TryFlushDnsCache()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(3000);
        }
        catch
        {
            // best effort
        }
    }

    private static readonly string[] RealChromeCandidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
    };

    /// <summary>Finds the real, installed Google Chrome executable (not Playwright's bundled
    /// Chromium). Checks the usual per-machine and per-user install locations.</summary>
    public static string? FindRealChromeExecutable() => RealChromeCandidates.FirstOrDefault(File.Exists);

    /// <summary>Opens a VISIBLE window of the user's REAL, installed Chrome (not Playwright's
    /// bundled Chromium — TikTok's QR-code login refuses to render at all in that bundled
    /// Chromium, confirmed 2026-09) on tiktok.com/login, and polls for the "sessionid" cookie to
    /// appear once the user finishes logging in by hand (password, QR, SMS, whatever).
    /// Launches `chrome.exe` itself with only `--remote-debugging-port` (no `--enable-automation`
    /// or other flags Playwright's own launcher adds), then has Playwright attach over CDP — as
    /// close to a normal, human-driven Chrome window as this app can get. Uses a fresh temporary
    /// profile directory so it never touches (or needs to close) the user's regular Chrome
    /// windows/profile. Returns null if the user closes the window without logging in, if Chrome
    /// isn't found, or if <paramref name="timeout"/> elapses first.</summary>
    public async Task<TikTokCookie?> LoginInteractivelyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var chromePath = FindRealChromeExecutable()
            ?? throw new InvalidOperationException(
                "Không tìm thấy Google Chrome thật đã cài trên máy (đã kiểm tra Program Files, Program Files (x86), và AppData\\Local\\Google\\Chrome). " +
                "Nếu Chrome cài ở vị trí khác, hãy dùng cách 'Dán cookie thủ công' thay thế.");

        var port = GetFreeTcpPort();
        var profileDir = Path.Combine(Path.GetTempPath(), $"tiktool_chrome_login_{Guid.NewGuid():N}");
        Directory.CreateDirectory(profileDir);

        using var chromeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check https://www.tiktok.com/login",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Không khởi chạy được Chrome thật.");

        try
        {
            using var playwright = await Playwright.CreateAsync();
            var browser = await ConnectWithRetryAsync(playwright, port, ct);
            var context = browser.Contexts.FirstOrDefault() ?? await browser.NewContextAsync();

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (chromeProcess.HasExited)
                {
                    // User closed the Chrome window (or all its tabs) before logging in.
                    return null;
                }

                var cookies = (await context.CookiesAsync()).ToList();
                var session = cookies.FirstOrDefault(c =>
                    c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) &&
                    c.Domain.Contains("tiktok", StringComparison.OrdinalIgnoreCase));

                if (session is not null)
                {
                    var tiktokCookies = cookies.Where(c => c.Domain.Contains("tiktok", StringComparison.OrdinalIgnoreCase));
                    var raw = string.Join("; ", tiktokCookies.Select(c => $"{c.Name}={c.Value}"));
                    return new TikTokCookie
                    {
                        SessionId = session.Value,
                        MsToken = cookies.FirstOrDefault(c => c.Name == "msToken")?.Value ?? "",
                        TtWebId = cookies.FirstOrDefault(c => c.Name is "tt_webid_v2" or "tt_webid")?.Value ?? "",
                        RawCookieString = raw,
                        CapturedAt = DateTime.Now,
                        Source = "login",
                        ExpiryDate = session.Expires > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)session.Expires).LocalDateTime : null,
                    };
                }

                await Task.Delay(1500, ct);
            }

            return null;
        }
        finally
        {
            TryKillChrome(chromeProcess);
            try { Directory.Delete(profileDir, true); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>Chrome needs a moment after process start before its CDP endpoint accepts
    /// connections — retries instead of a fixed sleep so this isn't flaky on a slow machine.</summary>
    private static async Task<IBrowser> ConnectWithRetryAsync(IPlaywright playwright, int port, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{port}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(500, ct);
            }
        }

        throw new InvalidOperationException($"Không kết nối được vào Chrome thật qua CDP (cổng {port}).", lastError);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryKillChrome(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static IEnumerable<Cookie> BuildCookies(TikTokCookie cookie)
    {
        foreach (var part in cookie.RawCookieString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            yield return new Cookie
            {
                Name = part[..idx].Trim(),
                Value = part[(idx + 1)..].Trim(),
                Domain = ".tiktok.com",
                Path = "/",
            };
        }
    }

    /// <summary>Loads the current user's full following list via the "Following accounts" panel
    /// on tiktok.com/following (opened through its "View all" link) — NOT the modal opened from
    /// the profile page's follow-count stat, which is confirmed (live, 2026-09-13) to silently
    /// cap out after its first ~30 rows on TikTok's web for large lists (reproduced even with a
    /// human manually scrolling that modal in an ordinary browser — a real TikTok bug, not
    /// anything fixable client-side). See SelectorsConfig.FollowingItem for the full story and
    /// why this different panel doesn't have that problem.</summary>
    public async Task<List<FollowingAccount>> LoadFollowingListAsync(
        SelectorsConfig selectors, string username, IProgress<string>? log = null, CancellationToken ct = default)
    {
        EnsureLaunched();

        var page = _page!;
        var accounts = new List<FollowingAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Best-effort: the real total ("528" etc.), read off the profile page, so the caller can
        // tell the user if this run came up short — not load-bearing for the scrape itself.
        int? totalFollowing = null;
        try
        {
            var profileUrl = selectors.ProfilePageUrl.Replace("{username}", Uri.EscapeDataString(username));
            await page.GotoAsync(profileUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            var totalText = await page.Locator("[data-e2e='following-count']").First.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5000 });
            if (int.TryParse(totalText.Trim(), out var parsed))
            {
                totalFollowing = parsed;
            }
        }
        catch
        {
            // best effort — not knowing the total just means the "loaded fewer than total"
            // warning below can't fire, everything else still works
        }

        // The "Following" stat/modal on the profile page is confirmed (2026-09-13, live — see
        // class doc comment above) to stop loading past its first batch. The "Following accounts"
        // panel reachable from tiktok.com/following's left sidebar ("View all") is a DIFFERENT UI
        // surface for the exact same data and does NOT have that problem — confirmed live by the
        // account owner manually scrolling it all the way through. So that's the one this scrapes.
        // A single navigation with a generous wait — not several short retries — because the
        // failure mode confirmed live 2026-09-13 was the "Following accounts" sidebar widget
        // still showing its loading skeleton at the 10s mark, not a dead end that re-navigating
        // would fix; re-navigating repeatedly just restarts that same slow hydration each time.
        var opened = false;
        for (var attempt = 0; attempt < 2 && !opened; attempt++)
        {
            try
            {
                await page.GotoAsync("https://www.tiktok.com/following", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

                if (await IsTikTokErrorPageAsync(page))
                {
                    log?.Report("[Unfollow] TikTok trả về trang lỗi tạm thời — đang thử tải lại...");
                    await Task.Delay(2000, ct);
                    continue;
                }

                // "View all" also appears under other sidebar sections (e.g. suggested accounts),
                // but "Following accounts" is the first such section on the page, so .First is the
                // right one — verified against a live DOM dump 2026-09-13.
                var viewAll = page.GetByText("View all", new PageGetByTextOptions { Exact = true }).First;
                await viewAll.WaitForAsync(new LocatorWaitForOptions { Timeout = 25_000 });
                await viewAll.ClickAsync();
                opened = true;
            }
            catch (Exception) when (ct.IsCancellationRequested == false)
            {
                if (attempt < 1)
                {
                    await Task.Delay(1000, ct);
                }
            }
        }

        if (!opened)
        {
            var dumpPath = await TryDumpDebugStateAsync(page, "view-all-not-found");
            var dumpNote = dumpPath is not null ? $" Đã lưu ảnh chụp + HTML để chẩn đoán: {dumpPath}" : "";
            throw new InvalidOperationException(
                "Không tìm thấy nút 'View all' của panel 'Following accounts' trên tiktok.com/following sau nhiều lần thử — TikTok có thể đã đổi DOM (cần cập nhật selectors.json) hoặc liên tục chặn phiên này." + dumpNote);
        }

        var itemsLocator = page.Locator(selectors.FollowingItem);

        // The panel opens instantly but its first batch of items arrives from a follow-up API
        // call — waiting for at least one row (rather than a fixed delay) avoids concluding "0 tài
        // khoản" if that call is slow, while not wasting time when it's fast.
        try
        {
            await itemsLocator.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        }
        catch (Exception) when (ct.IsCancellationRequested == false)
        {
            // Genuinely empty following list (0 kênh) also lands here — let the loop below run
            // its normal 3-strikes-no-change check and return an empty list rather than throwing.
        }

        var prevCount = 0;
        var noChangeCount = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Confirmed live 2026-09-13: with the multi-tick wheel scroll below, this panel DOES keep
        // loading more (seen progressing 30 -> 55 in one run) — it's just inconsistent about how
        // much patience each batch needs, sometimes stalling harder than others. So this is
        // deliberately very patient (20 no-change strikes, not a handful) and bounded by wall-clock
        // time instead (5 minutes) rather than a small fixed strike count — the failure mode to
        // avoid here is quitting too early on a list that would've kept growing, not spending an
        // extra minute confirming it's actually done.
        var overallBudget = TimeSpan.FromMinutes(5);
        while (noChangeCount < 35 && stopwatch.Elapsed < overallBudget)
        {
            ct.ThrowIfCancellationRequested();

            var count = await itemsLocator.CountAsync();
            for (var i = prevCount; i < count; i++)
            {
                var acc = await ParseFollowingItemAsync(itemsLocator.Nth(i));
                if (acc is not null && seen.Add(acc.Username))
                {
                    accounts.Add(acc);
                }
            }

            if (count == prevCount)
            {
                noChangeCount++;
                // Heartbeat so a long stall (this can now run up to 5 minutes) reads as "still
                // working" in the log rather than going silent and looking hung.
                log?.Report($"[Unfollow] Đang chờ tải thêm... ({count} tài khoản, {stopwatch.Elapsed:mm\\:ss} trôi qua)");
            }
            else
            {
                noChangeCount = 0;
                log?.Report($"[Unfollow] Đã tìm thấy {count} tài khoản đang theo dõi...");
            }

            prevCount = count;

            if (count > 0)
            {
                try
                {
                    // Confirmed live (2026-10-27): Playwright's Mouse.WheelAsync was NOT scrolling
                    // the panel — debug logs showed scrollTop=0 after 10 ticks of 350px. The wheel
                    // events fire on whatever is under the cursor, which may be the page body
                    // rather than the panel's own scroll container. Direct JS scrollBy() on the
                    // actual scrollable ancestor is the only approach confirmed to move scrollTop.
                    var scrolled = await page.EvaluateAsync<bool>(@"(selector) => {
                        const item = document.querySelector(selector);
                        if (!item) return false;
                        let el = item.parentElement;
                        while (el && el !== document.body) {
                            const ov = getComputedStyle(el).overflowY;
                            if ((ov === 'auto' || ov === 'scroll') && el.scrollHeight > el.clientHeight) {
                                el.scrollBy(0, 1800);
                                return true;
                            }
                            el = el.parentElement;
                        }
                        return false;
                    }", selectors.FollowingItem);

                    if (!scrolled)
                    {
                        // The overflowY check missed the container (some TikTok DOM variants use
                        // the shorthand overflow: scroll rather than overflow-y). Try ScrollIntoView
                        // on the last item as a last resort, and also fire a few wheel ticks with
                        // the cursor positioned over the last item so the browser's own hit-test
                        // picks the right element.
                        await itemsLocator.Nth(count - 1).ScrollIntoViewIfNeededAsync();
                        var box = await itemsLocator.Nth(count - 1).BoundingBoxAsync();
                        if (box is not null)
                        {
                            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                            for (var tick = 0; tick < 5; tick++)
                            {
                                await page.Mouse.WheelAsync(0, 400);
                                await Task.Delay(200, ct);
                            }
                        }
                    }
                }
                catch
                {
                    await itemsLocator.Nth(count - 1).ScrollIntoViewIfNeededAsync();
                }
            }

            // Shorter than before — with the account owner actively helping by scrolling the
            // panel by hand alongside this loop, checking more often picks up manually-triggered
            // progress sooner. noChangeCount's ceiling was raised to 35 (from 20) to keep the same
            // overall patience floor despite polling twice as often.
            await Task.Delay(1000, ct);
        }

        if (accounts.Count == 0)
        {
            var dumpPath = await TryDumpDebugStateAsync(page, "empty-following-list");
            if (dumpPath is not null)
            {
                log?.Report($"[Unfollow] 0 tài khoản — đã lưu ảnh chụp + HTML trang lúc đó để chẩn đoán: {dumpPath}");
            }
        }
        else if (totalFollowing is int total && accounts.Count < total)
        {
            // Loading stalled before reaching the real total even with the patient multi-tick
            // scroll above — could be genuinely done scrolling this session, or TikTok just
            // needing more patience than this run gave it. Either way, the safe workable path is
            // the same: unfollow what's in the current batch, then reload — since the unfollowed
            // accounts drop out of the list, the next load has fewer to get through and may well
            // surface further ones this run didn't reach.
            log?.Report(
                $"[Unfollow] Tải được {accounts.Count}/{total} kênh trong lần này — TikTok có thể còn nhiều hơn nhưng dừng tải. " +
                "Unfollow xong đợt này rồi bấm 'Tải lại' để thử lấy tiếp phần còn lại.");
        }

        return accounts;
    }

    /// <summary>Best-effort diagnostic dump (screenshot + full page HTML) used when something
    /// that should have found data found none — e.g. the following-list scrape coming back
    /// empty despite the account clearly following hundreds of people. Lets a human compare what
    /// the automated Chrome window actually saw against the real DOM, instead of guessing blind
    /// at which selector broke this time. Never throws — a failed dump must not mask the real
    /// error/result of the caller.</summary>
    private async Task<string?> TryDumpDebugStateAsync(IPage? page, string label)
    {
        if (page is null)
        {
            return null;
        }

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseName = Path.Combine(AppPaths.DebugDir, $"{stamp}_{label}");

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = baseName + ".png", FullPage = true });
            var html = await page.ContentAsync();
            await File.WriteAllTextAsync(baseName + ".html", html);

            return baseName + ".png / .html";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Detects TikTok's generic "Something went wrong / Please try again later"
    /// bot-check/error page, which looks nothing like a normal profile page (no follow stats,
    /// no video grid) and would otherwise be misread as "0 tài khoản" by the caller.</summary>
    private static async Task<bool> IsTikTokErrorPageAsync(IPage page)
    {
        try
        {
            var text = await page.EvaluateAsync<string>("document.body?.innerText ?? ''");
            return text.Contains("Something went wrong", StringComparison.OrdinalIgnoreCase)
                && text.Contains("try again", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<FollowingAccount?> ParseFollowingItemAsync(ILocator item)
    {
        // `item` is the row's `<a href="/@username...">` link (see SelectorsConfig.FollowingItem
        // for why href-parsing, not a data-e2e attribute, is the cross-variant-safe source here).
        var href = await item.GetAttributeAsync("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var atIdx = href.IndexOf("/@", StringComparison.Ordinal);
        if (atIdx < 0)
        {
            return null;
        }

        var username = href[(atIdx + 2)..].Split('?', '/').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        // In every observed variant this link wraps BOTH the nickname and the username as two
        // separate block-level children, so innerText renders them as two lines — the nickname
        // first. Falls back to the username itself if that assumption ever doesn't hold.
        var fullText = (await item.InnerTextAsync()).Trim();
        var nickname = fullText.Split('\n').FirstOrDefault()?.Trim();

        return new FollowingAccount { Username = username, Nickname = string.IsNullOrWhiteSpace(nickname) ? username : nickname };
    }

    /// <summary>Click-through fallback used when the REST unfollow call fails (in practice: the
    /// only path used at all now — see TikTokApiService): opens the profile page directly and
    /// clicks the follow/unfollow toggle, dismissing TikTok's "Unfollow?" confirmation if one
    /// appears. CONFIRMED LIVE (2026-09-13) this used to just click-and-declare-success with no
    /// check that anything actually happened — and a real account stayed followed afterward. So
    /// this now reads the button's aria-label BEFORE and AFTER clicking (TikTok renders it as
    /// "Following {name}" vs "Follow {name}") and only reports success if it actually flipped;
    /// on failure it saves a debug dump instead of silently lying about the result.</summary>
    public async Task<bool> ClickUnfollowAsync(string username, SelectorsConfig selectors, CancellationToken ct = default)
    {
        EnsureLaunched();
        var page = _page!;

        await page.GotoAsync($"https://www.tiktok.com/@{username}?lang=en", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Task.Delay(1000, ct);

        var button = page.Locator(selectors.UnfollowBtn).First;
        if (await button.CountAsync() == 0)
        {
            throw new InvalidOperationException($"Không tìm thấy nút Follow/Unfollow trên trang @{username} (selector '{selectors.UnfollowBtn}').");
        }

        var before = (await button.GetAttributeAsync("aria-label")) ?? (await button.InnerTextAsync());
        if (!before.Contains("Following", StringComparison.OrdinalIgnoreCase))
        {
            // Already not following (someone/something else unfollowed it already, or the
            // account never actually followed it) — nothing to do, and not a failure.
            return true;
        }

        await button.ClickAsync();
        await Task.Delay(800, ct);

        // TikTok's own "Unfollow?" confirmation isn't always a `<button>` — try a broad
        // visible-text match first (covers menu items/divs too), falling back to the narrower
        // button-only match for older/other DOM variants.
        var confirm = page.GetByText("Unfollow", new PageGetByTextOptions { Exact = false }).First;
        if (await confirm.CountAsync() == 0)
        {
            confirm = page.Locator("button:has-text('Unfollow')").First;
        }
        if (await confirm.CountAsync() > 0)
        {
            try
            {
                await confirm.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                await Task.Delay(800, ct);
            }
            catch
            {
                // No real confirmation dialog appeared (some accounts/variants unfollow on the
                // first click with no confirmation) — nothing to click, that's fine.
            }
        }

        // Re-locate rather than reuse `button` — TikTok may have replaced the element outright
        // rather than mutating it in place.
        var after = page.Locator(selectors.UnfollowBtn).First;
        var afterText = await after.CountAsync() > 0
            ? (await after.GetAttributeAsync("aria-label")) ?? (await after.InnerTextAsync())
            : "";

        if (afterText.Contains("Following", StringComparison.OrdinalIgnoreCase))
        {
            var dumpPath = await TryDumpDebugStateAsync(page, $"unfollow-failed-{username}");
            var dumpNote = dumpPath is not null ? $" Đã lưu ảnh chụp + HTML để chẩn đoán: {dumpPath}" : "";
            throw new InvalidOperationException($"Bấm nút Unfollow nhưng @{username} vẫn đang được theo dõi (không có tác dụng thật)." + dumpNote);
        }

        return true;
    }

    /// <summary>Reads an account's numeric user id, video count, and most-recent-post timestamp
    /// off its profile page. Deliberately NOT a direct call to TikTok's `api/user/detail/` or
    /// `api/post/item_list/` endpoints — live testing (2026-09-13, see TikTokApiService for
    /// detail) showed those return HTTP 200 with an EMPTY body without a valid X-Bogus/msToken
    /// signature that a manually-issued request (whether from C# or hand-typed in a live
    /// browser's own console) can't produce — confirmed the signature is tied to the page's own
    /// internal call site, not just "this browser is logged in".
    /// So instead: (1) userId/videoCount come from the server-rendered
    /// `__UNIVERSAL_DATA_FOR_REHYDRATION__` hydration script TikTok embeds in the page HTML
    /// itself (path: __DEFAULT_SCOPE__["webapp.user-detail"].userInfo.{user.id, stats.videoCount})
    /// — no signature needed, it's just page content, not an API call; (2) the last-post
    /// timestamp comes from PASSIVELY intercepting the `api/post/item_list/` response that the
    /// profile page's OWN client-side code fires to render its video grid — confirmed live that
    /// this genuine, page-triggered call *does* carry a working signature (unlike anything we
    /// issue ourselves), and `itemList[0].createTime` (unix seconds) is the newest video's post
    /// time under the default "Latest" sort. Returns null on any failure (private/deleted
    /// account, selector/schema drift, etc.); caller treats that as "unknown", never as "dead".
    /// LastPostUnixSeconds specifically stays null when the account has videos but the
    /// intercept simply didn't arrive in time (slow network) — never guessed from partial data.</summary>
    public async Task<(string UserId, int VideoCount, long? LastPostUnixSeconds)?> GetUserDetailAsync(string username, CancellationToken ct = default)
    {
        EnsureLaunched();

        try
        {
            var itemListTask = _page!.WaitForResponseAsync(
                r => r.Url.Contains("/api/post/item_list/") && r.Ok,
                new PageWaitForResponseOptions { Timeout = 15_000 });

            await _page.GotoAsync($"https://www.tiktok.com/@{username}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            var raw = await _page.EvaluateAsync<string?>(
                "document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__')?.textContent ?? null");
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(raw);
            var userInfo = doc.RootElement
                .GetProperty("__DEFAULT_SCOPE__")
                .GetProperty("webapp.user-detail")
                .GetProperty("userInfo");

            var userId = userInfo.GetProperty("user").GetProperty("id").GetString() ?? "";
            var videoCount = userInfo.GetProperty("stats").GetProperty("videoCount").GetInt32();

            long? lastPostUnixSeconds = null;
            if (videoCount > 0)
            {
                try
                {
                    var response = await itemListTask;
                    var body = await response.TextAsync();
                    using var itemDoc = JsonDocument.Parse(body);
                    if (itemDoc.RootElement.TryGetProperty("itemList", out var itemList) && itemList.GetArrayLength() > 0)
                    {
                        lastPostUnixSeconds = itemList[0].GetProperty("createTime").GetInt64();
                    }
                }
                catch
                {
                    // item_list didn't arrive in time, or its shape changed — leave last-post
                    // date unknown rather than guessing at it.
                }
            }

            return (userId, videoCount, lastPostUnixSeconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureLaunched()
    {
        if (_page is null)
        {
            throw new InvalidOperationException("Playwright chưa được khởi động — gọi LaunchAsync trước.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_chromeProcess is not null)
        {
            TryKillChrome(_chromeProcess);
            _chromeProcess.Dispose();
        }

        if (_profileDir is not null)
        {
            try { Directory.Delete(_profileDir, true); } catch { /* best effort cleanup */ }
        }

        _context = null;
        _browser = null;
        _page = null;
        _playwright = null;
        _chromeProcess = null;
        _profileDir = null;
    }
}
