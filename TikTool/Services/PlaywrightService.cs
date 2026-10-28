using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using TikTool.Browser;
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
    private IBrowserContext? _context;
    private IPage? _page;
    private IBrowserHost? _host;
    private IPage? _workerPage;
    private readonly SemaphoreSlim _workerGate = new(1, 1);
    private string? _profileDir;

    /// <summary>True while the browser is up. A closed window counts as not launched, so the next run starts a fresh one.</summary>
    public bool IsLaunched => _page is not null && _host is { IsRunning: true };

    /// <param name="embedded">true = the built-in WebView2 window (its profile folder is shared with the Accounts tab), false = the installed Chrome/Edge with a throwaway profile.</param>
    public async Task LaunchAsync(TikTokCookie cookie, bool embedded = false)
    {
        TryFlushDnsCache();

        string profile;
        if (embedded)
        {
            profile = AppPaths.TikTokWebViewDir;
        }
        else
        {
            if (FindRealChromeExecutable() is null)
            {
                throw new InvalidOperationException(
                    T("Không tìm thấy Google Chrome hoặc Microsoft Edge trên máy.\n", "Google Chrome or Microsoft Edge was not found on this computer.\n") +
                    T("Social Toolkit cần attach vào Chrome/Edge thật (TikTok chặn trình duyệt tự động).\n", "Social Toolkit needs to attach to a real Chrome/Edge (TikTok blocks automated browsers).\n") +
                    T("Hãy cài Google Chrome: https://www.google.com/chrome/", "Please install Google Chrome: https://www.google.com/chrome/"));
            }

            _profileDir = Path.Combine(Path.GetTempPath(), $"tiktool_chrome_session_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_profileDir);
            profile = _profileDir;
        }

        _playwright = await Playwright.CreateAsync();
        _host = BrowserHosts.Create(embedded);
        _context = await _host.StartAsync(_playwright, profile, T("Social Toolkit — Trình duyệt TikTok", "Social Toolkit — TikTok browser"), CancellationToken.None);
        await _context.AddCookiesAsync(BuildCookies(cookie));
        _page = await _host.GetMainPageAsync(CancellationToken.None);

        // Warm-up: this profile dir is brand new every launch (only cookies were injected, no
        // localStorage/device fingerprint), and navigating straight to a deep page like a profile
        // sometimes gets TikTok's "Something went wrong" bot-check page instead of real content.
        // Landing on the homepage first, like a real user would, measurably reduces that. Best
        // effort — a failure here shouldn't block the actual task.
        try
        {
            try
            {
                await _page.GotoAsync("https://www.tiktok.com/",
                    new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15_000 });
            }
            catch { /* NetworkIdle timeout on TikTok is expected — page content already loaded */ }
            await Task.Delay(2500);
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

    private const string ChromeOfflineInstallerUrl =
        "https://dl.google.com/chrome/install/ChromeStandaloneSetup64.exe";

    /// <summary>If Chrome/Edge is already installed this is a no-op and returns immediately.
    /// Otherwise shows a MessageBox asking the user for consent, downloads the Chrome offline
    /// installer (~80 MB), runs it silently (per-user, no admin needed), then verifies that
    /// Chrome is now findable. Progress bytes are reported via <paramref name="progress"/> so
    /// the caller can update a UI label/bar. Throws on download or install failure with a
    /// friendly Vietnamese message.</summary>
    public static async Task EnsureChromeInstalledAsync(
        IProgress<(long Downloaded, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        if (FindRealChromeExecutable() is not null) return;

        var answer = MessageBox.Show(
            T("Không tìm thấy Google Chrome hoặc Microsoft Edge trên máy.\n\n", "Google Chrome or Microsoft Edge was not found on this computer.\n\n") +
            T("Social Toolkit cần attach vào Chrome thật vì TikTok chặn trình duyệt tự động.\n\n", "Social Toolkit needs to attach to a real Chrome because TikTok blocks automated browsers.\n\n") +
            T("Cài Google Chrome tự động? (~80 MB)", "Install Google Chrome automatically? (~80 MB)"),
            T("Cần Chrome để dùng tính năng Unfollow", "Chrome is required for the Unfollow feature"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            throw new OperationCanceledException(T("Người dùng huỷ cài Chrome.", "The user cancelled the Chrome installation."));

        var setupPath = Path.Combine(Path.GetTempPath(), "tiktool_chrome_setup.exe");

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        using var response = await http.GetAsync(
            ChromeOfflineInstallerUrl,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(setupPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            progress?.Report((downloaded, total));
        }

        await dst.FlushAsync(ct);
        dst.Close();

        using var install = Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/silent /install",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException(T("Không khởi chạy được Chrome installer.", "Could not start the Chrome installer."));

        // Installer typically finishes in under 2 minutes on a normal machine.
        await Task.Run(() => install.WaitForExit(120_000), ct);

        try { File.Delete(setupPath); } catch { }

        if (FindRealChromeExecutable() is null)
            throw new InvalidOperationException(
                T("Cài Chrome xong nhưng vẫn không tìm thấy — thử khởi động lại máy hoặc cài thủ công: ", "Chrome was installed but still cannot be found — restart the computer or install it manually: ") +
                "https://www.google.com/chrome/");
    }

    private static readonly string[] RealBrowserCandidates =
    {
        // Chrome — per-machine, per-machine x86, per-user
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        // Edge — per-machine, per-machine x86, per-user
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe"),
    };

    /// <summary>Finds Chrome or Edge (Chrome first). Returns null if neither is installed.</summary>
    public static string? FindRealChromeExecutable() => RealBrowserCandidates.FirstOrDefault(File.Exists);

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
                T("Không tìm thấy Google Chrome hoặc Microsoft Edge trên máy. ", "Google Chrome or Microsoft Edge was not found on this computer. ") +
                T("Hãy cài Chrome/Edge hoặc cài WebView2 Runtime.", "Install Chrome/Edge or the WebView2 runtime."));

        var port = GetFreeTcpPort();
        var profileDir = Path.Combine(Path.GetTempPath(), $"tiktool_chrome_login_{Guid.NewGuid():N}");
        Directory.CreateDirectory(profileDir);

        using var chromeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check https://www.tiktok.com/login",
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException(T("Không khởi chạy được Chrome thật.", "Could not start the real Chrome."));

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

        throw new InvalidOperationException(T($"Không kết nối được vào Chrome thật qua CDP (cổng {port}).", $"Could not connect to the real Chrome over CDP (port {port})."), lastError);
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

    /// <summary>Loads the current user's full following list by clicking the "Following" count
    /// on their own profile page — this opens a modal with all following accounts in a properly
    /// scrollable container. The API response interceptor captures userId/videoCount from TikTok's
    /// own XHR calls passively. The old "following-see-all" sidebar panel was dropped (2026-10-26):
    /// it pre-loads exactly 5 accounts whose container height equals its content height, so no
    /// overflow ever exists and scrolling triggers no further API calls.</summary>
    public async Task<List<FollowingAccount>> LoadFollowingListAsync(
        SelectorsConfig selectors, string username, IProgress<string>? log = null, CancellationToken ct = default)
    {
        EnsureLaunched();

        var page = _page!;
        var accounts = new List<FollowingAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The real total (e.g. 547) comes from the list API's own 'total' field, captured below -- so no
        // separate visit to the profile page is needed (and that page is the one TikTok blocks first).
        int? totalFollowing = null;

        // Intercept TikTok's own API responses while scrolling the following panel — TikTok's own
        // XHR calls carry valid signatures and return richer data (userId, videoCount, secUid) that
        // we cannot easily obtain by issuing our own requests. Capturing these passively (no extra
        // network cost) populates UserId and VideoCount for each account without separate profile
        // visits, which both avoids rate-limiting and handles deleted accounts (Access Denied on
        // their profile page) since we have their userId from the list load instead.
        CapturedAvatarUrls.Clear();
        var capturedUsers = new ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)>(StringComparer.OrdinalIgnoreCase);

        // Diagnostics: last paged-list response seen per API path (hasMore/cursor/size), so a stall
        // can be attributed to TikTok ending pagination vs. the scraper not reaching the bottom.
        var batchInfo = new ConcurrentDictionary<string, string>();
        var batchCounts = new ConcurrentDictionary<string, int>();
        var lastScrollInfo = "?";

        async void OnApiResponse(object? _, IResponse resp)
        {
            if (!resp.Ok) return;
            var urlPath = resp.Url.Split('?')[0];
            if (!urlPath.Contains("/api/")) return;
            try
            {
                var body = await resp.TextAsync();
                ExtractUserDataFromApiResponse(body, capturedUsers);
                RecordListBatch(body, urlPath, batchInfo, batchCounts);

                if (totalFollowing is null && urlPath.EndsWith("/api/user/list/", StringComparison.Ordinal))
                {
                    using var totalDoc = JsonDocument.Parse(body);
                    if (totalDoc.RootElement.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number)
                    {
                        totalFollowing = totalEl.GetInt32();
                    }
                }
            }
            catch { /* response body may be unavailable on navigation — safe to ignore */ }
        }

        page.Response += OnApiResponse;

        // Open the full "Following accounts" panel: tiktok.com/following -> sidebar "View all". It is
        // reached directly, never via the profile page: the profile modal is capped by TikTok at ~60
        // rows (reproduced by hand), and the profile page is the first thing TikTok blocks/errors on
        // (its video grid shows "Something went wrong" even when the rest of the site works).
        var opened = false;
        for (var attempt = 0; attempt < 3 && !opened; attempt++)
        {
            try
            {
                await OpenFollowingPanelAsync(page, selectors.FollowingItem, username, ct);
                opened = true;
            }
            catch (Exception ex) when (ct.IsCancellationRequested == false)
            {
                AppLog.Write("WARN", $"Open Following panel attempt {attempt + 1} failed: {ex.GetType().Name}: {ex.Message}");
                if (attempt < 2)
                {
                    log?.Report(T($"[Unfollow] Chưa mở được danh sách Following ({ex.Message}), thử lại (lần {attempt + 2})...",
                                  $"[Unfollow] Following list not opened ({ex.Message}), retrying (attempt {attempt + 2})..."));
                    await Task.Delay(4000, ct);
                }
            }
        }
        if (!opened)
        {
            var dumpPath = await TryDumpDebugStateAsync(page, "following-modal-not-found");
            var dumpNote = dumpPath is not null ? T($" Đã lưu ảnh chụp + HTML để chẩn đoán: {dumpPath}", $" A screenshot + HTML were saved for diagnosis: {dumpPath}") : "";
            throw new InvalidOperationException(
                T("Không thể mở danh sách Following sau 3 lần thử — TikTok chặn hoặc đổi DOM. Thử lại sau vài phút hoặc dùng VPN." + dumpNote,
                  "Could not open the Following list after 3 attempts — TikTok blocked or changed DOM. Retry after a few minutes or use a VPN." + dumpNote));
        }

        var modalMissingTicks = 0;

        // Track unique accounts seen, not raw DOM count. TikTok's modal is a virtual list: it
        // removes items from the top of the DOM as new batches load at the bottom. Tracking DOM
        // count would cause stall false-positives (count stays ~20 while items shift). Instead we
        // loop from 0 every tick (seen HashSet deduplicates) and measure progress by seen.Count.
        var prevSeenCount = 0;
        var noChangeCount = 0;
        var innerRetryCount = 0;
        const int noChangeRetryThreshold = 20; // 20s stuck → try reopening the panel
        const int maxInnerRetries = 2;          // at most 2 panel-reopen attempts per load
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Confirmed live 2026-09-13: with the multi-tick wheel scroll below, this panel DOES keep
        // loading more (seen progressing 30 -> 55 in one run) — it's just inconsistent about how
        // much patience each batch needs, sometimes stalling harder than others. So this is
        // deliberately very patient (35 no-change strikes before giving up) and bounded by wall-clock
        // time (5 minutes). If scroll is stuck for 20s with no progress AND we've used fewer than
        // maxInnerRetries, we navigate back to home and reopen the panel — TikTok often serves a
        // fresh API batch on a second open, whereas the same panel just kept returning 5 accounts
        // even after 60+ seconds of scrolling in the original session.
        var overallBudget = TimeSpan.FromMinutes(5);
        while (noChangeCount < 35 && stopwatch.Elapsed < overallBudget)
        {
            ct.ThrowIfCancellationRequested();

            var scan = await ScanFollowingModalAsync(page, username, selectors.FollowingItem);
            var count = scan.Rows.Count;
            foreach (var (rowUser, rowNick) in scan.Rows)
            {
                if (seen.Add(rowUser))
                {
                    accounts.Add(new FollowingAccount { Username = rowUser, Nickname = rowNick });
                }
            }

            if (!scan.Found && ++modalMissingTicks == 3)
            {
                var missPath = await TryDumpDebugStateAsync(page, "following-modal-root-missing");
                log?.Report(T($"[Unfollow] Không xác định được modal Following (đã lưu chẩn đoán: {missPath}).", $"[Unfollow] Could not locate the Following list (diagnostics saved: {missPath})."));
            }

            if (seen.Count == prevSeenCount)
            {
                noChangeCount++;

                // Auto-retry: if stuck for noChangeRetryThreshold seconds, close and reopen the
                // panel so TikTok issues a fresh API call. The seen HashSet deduplicates any
                // overlap with accounts already collected.
                // Skip the retry when we're already at or very near the known total — being
                // "stuck" at 524/524 just means the panel ran out of items, not a mid-scroll stall.
                var nearTotal = totalFollowing is int tot && accounts.Count >= (int)(tot * 0.92);

                // Already at/near the real total and nothing new for 5 ticks: the list is exhausted (the
                // profile count also includes deleted/banned accounts that are never returned), so stop
                // instead of waiting out the full 35-tick stall budget.
                if (nearTotal && noChangeCount >= 5)
                {
                    log?.Report(T($"[Unfollow] Đã tải {accounts.Count}/{totalFollowing} kênh — danh sách đã hết, dừng.", $"[Unfollow] Loaded {accounts.Count}/{totalFollowing} accounts — the list is exhausted, stopping."));
                    break;
                }
                if (noChangeCount >= noChangeRetryThreshold && innerRetryCount < maxInnerRetries && !nearTotal)
                {
                    innerRetryCount++;
                    noChangeCount = 0;
                    log?.Report(
                        T($"[Unfollow] Kẹt {noChangeRetryThreshold}s tại {accounts.Count} tài khoản — ", $"[Unfollow] Stuck for {noChangeRetryThreshold}s at {accounts.Count} accounts — ") +
                        T($"đang mở lại panel (lần {innerRetryCount}/{maxInnerRetries})...", $"reopening the panel (attempt {innerRetryCount}/{maxInnerRetries})..."));

                    try
                    {
                        // Reopen the modal: navigate back to profile, then click the stat again.
                        await OpenFollowingPanelAsync(page, selectors.FollowingItem, username, ct);
                        log?.Report(T("[Unfollow] Modal Following đã mở lại thành công — tiếp tục tải...",
                                      "[Unfollow] Following modal reopened successfully — continuing..."));
                    }
                    catch (Exception retryEx) when (!ct.IsCancellationRequested)
                    {
                        log?.Report(T($"[Unfollow] Mở lại modal Following thất bại ({retryEx.Message}) — tiếp tục chờ...",
                                      $"[Unfollow] Reopen Following modal failed ({retryEx.Message}) — waiting..."));
                    }

                    await Task.Delay(1000, ct);
                    continue;
                }

                // Heartbeat so a long stall reads as "still working" rather than going silent.
                log?.Report(T($"[Unfollow] Đang chờ tải thêm... ({accounts.Count} đã thấy / {count} trong DOM, {stopwatch.Elapsed:mm\\:ss} trôi qua)", $"[Unfollow] Waiting for more... ({accounts.Count} seen / {count} in the page, {stopwatch.Elapsed:mm\\:ss} elapsed)"));
            }
            else
            {
                noChangeCount = 0;
                log?.Report(T($"[Unfollow] Đã tìm thấy {accounts.Count} tài khoản ({count} hiện trong DOM)...", $"[Unfollow] Found {accounts.Count} accounts ({count} shown in the page)..."));
            }

            prevSeenCount = seen.Count;

            if (scan.Found && count > 0)
            {
                try
                {
                    // Scroll the MODAL's own container (found relative to the modal root, never via the
                    // page-wide row selector — that one also matches the 5-row sidebar widget).
                    var scrollJson = await page.EvaluateAsync<string?>(ScrollModalJs, new { sel = selectors.FollowingItem, own = username });
                    if (scrollJson is not null)
                    {
                        using var sd = JsonDocument.Parse(scrollJson);
                        var r = sd.RootElement;
                        if (accounts.Count <= 30 || noChangeCount > 0)
                        {
                            lastScrollInfo = $"scrollH={r.GetProperty("sh").GetDouble():0} clientH={r.GetProperty("ch").GetDouble():0} scrollTop={r.GetProperty("st").GetDouble():0}";
                            log?.Report($"[Unfollow][DBG] modal rows={count} scrollH={r.GetProperty("sh").GetDouble():0} clientH={r.GetProperty("ch").GetDouble():0} scrollTop={r.GetProperty("st").GetDouble():0}");
                        }

                        // Trusted wheel events over the modal's scroll area, in case TikTok's
                        // load-more is driven by wheel handlers rather than the scroll position.
                        await page.Mouse.MoveAsync((float)r.GetProperty("x").GetDouble(), (float)r.GetProperty("y").GetDouble());
                        for (var tick = 0; tick < 4; tick++)
                        {
                            await page.Mouse.WheelAsync(0, 500);
                            await Task.Delay(200, ct);
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log?.Report($"[Unfollow][DBG] scroll error: {ex.Message}");
                }
            }

            // After scrolling, give TikTok's API time to respond. TikTok lazy-loads the next
            // batch only after its IntersectionObserver sentinel enters the viewport; the API call
            // itself takes 1–3 s on a normal connection. Polling every 1 s missed batches that
            // arrived in 2–3 s; 3 s catches them reliably without wasting much total time.
            await Task.Delay(3000, ct);
        }

        page.Response -= OnApiResponse;

        foreach (var kv in batchInfo)
        {
            log?.Report($"[Unfollow][DBG] API list: {kv.Value} (batches={batchCounts.GetValueOrDefault(kv.Key)})");
        }
        log?.Report($"[Unfollow][DBG] final: scraped={accounts.Count} total={totalFollowing?.ToString() ?? "?"} {lastScrollInfo}");

        // Apply userId, videoCount, and relation captured from TikTok's own API calls during list load.
        var interceptedCount = 0;
        foreach (var account in accounts)
        {
            if (capturedUsers.TryGetValue(account.Username, out var captured))
            {
                if (string.IsNullOrEmpty(account.UserId) && !string.IsNullOrEmpty(captured.UserId))
                    account.UserId = captured.UserId;
                if (account.VideoCount < 0 && captured.VideoCount >= 0)
                    account.VideoCount = captured.VideoCount;
                if (account.Relation < 0 && captured.Relation >= 0)
                    account.Relation = captured.Relation;
                interceptedCount++;
            }
        }
        var avatarCount = 0;
        foreach (var account in accounts)
        {
            if (CapturedAvatarUrls.TryGetValue(account.Username, out var avatarUrl))
            {
                account.AvatarUrl = avatarUrl;
                avatarCount++;
            }
        }

        var avatarHost = "-";
        var sampleUrl = accounts.Select(a => a.AvatarUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u));
        if (sampleUrl is not null && Uri.TryCreate(sampleUrl, UriKind.Absolute, out var sampleUri))
        {
            avatarHost = sampleUri.Host + " ..." + Path.GetExtension(sampleUri.AbsolutePath);
        }

        log?.Report($"[Unfollow][DBG] Avatar links captured: {avatarCount}/{accounts.Count} (sample host: {avatarHost})");

        if (interceptedCount > 0)
            log?.Report(T($"[Unfollow] API intercept: lấy được userId/videoCount cho {interceptedCount}/{accounts.Count} kênh (không cần visit profile).", $"[Unfollow] API intercept: got userId/videoCount for {interceptedCount}/{accounts.Count} accounts (no profile visits needed)."));

        if (accounts.Count == 0)
        {
            var dumpPath = await TryDumpDebugStateAsync(page, "empty-following-list");
            if (dumpPath is not null)
            {
                log?.Report(T($"[Unfollow] 0 tài khoản — đã lưu ảnh chụp + HTML trang lúc đó để chẩn đoán: {dumpPath}", $"[Unfollow] 0 accounts — a screenshot + HTML of the page were saved for diagnosis: {dumpPath}"));
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
                T($"[Unfollow] Tải được {accounts.Count}/{total} kênh trong lần này — TikTok có thể còn nhiều hơn nhưng dừng tải. ", $"[Unfollow] Loaded {accounts.Count}/{total} accounts this time — TikTok may have more but stopped loading. ") +
                T("Unfollow xong đợt này rồi bấm 'Tải lại' để thử lấy tiếp phần còn lại.", "Unfollow this batch, then click 'Reload' to try to get the rest."));
        }

        return accounts;
    }

    // Rows are matched by the page-wide row selector, but ONLY the visible ones count — the
    // collapsed sidebar widget (a fixed 5 rows that can never scroll) matches the same selector
    // and is what the scraper used to read instead of the real list.
    private const string RowsPreambleJs = """
        const vis = e => e.getClientRects().length > 0;
        const rowsOf = sel => [...document.querySelectorAll(sel)].filter(vis);
        const linkOf = r => r.matches('a[href*="/@"]') ? r : (r.querySelector('a[href*="/@"]') || r.closest('a[href*="/@"]'));
        """;

    private const string ScanModalJs = "(arg) => {" + RowsPreambleJs + """
        const rows = rowsOf(arg.sel);
        const seen = new Set();
        const out = [];
        for (const r of rows) {
          const a = linkOf(r);
          if (!a) continue;
          const m = (a.getAttribute('href') || '').match(/\/@([^\/?#]+)/);
          if (!m) continue;
          const u = decodeURIComponent(m[1]);
          const k = u.toLowerCase();
          if (seen.has(k) || k === (arg.own || '').toLowerCase()) continue;
          seen.add(k);
          const lines = (r.innerText || '').split('\n').map(s => s.trim())
            .filter(s => s && !/^(Following|Follow|Friends|Remove|Message|View|Xem)$/i.test(s));
          out.push({ u: u, n: (lines[0] && lines[0] !== u) ? lines[0] : u });
        }
        return JSON.stringify({ found: rows.length > 0, rows: out });
        }
        """;

    private const string ScrollModalJs = "(arg) => {" + RowsPreambleJs + """
        const rows = rowsOf(arg.sel);
        const last = rows[rows.length - 1];
        if (!last) return null;
        let el = last.parentElement, sc = null;
        while (el && el !== document.body && el !== document.documentElement) {
          const ov = getComputedStyle(el).overflowY;
          if (el.scrollHeight > el.clientHeight + 2 && ov !== 'visible') { sc = el; break; }
          el = el.parentElement;
        }
        last.scrollIntoView({ block: 'end' });
        if (sc) {
          sc.scrollTop = sc.scrollHeight;
          sc.dispatchEvent(new Event('scroll', { bubbles: true }));
        }
        const r = (sc || last).getBoundingClientRect();
        return JSON.stringify({ x: r.x + r.width / 2, y: r.y + r.height / 2,
          sh: sc ? sc.scrollHeight : 0, ch: sc ? sc.clientHeight : 0, st: sc ? sc.scrollTop : 0 });
        }
        """;

    private static async Task<(bool Found, List<(string User, string Nick)> Rows)> ScanFollowingModalAsync(
        IPage page, string ownUsername, string rowSelector)
    {
        var rows = new List<(string, string)>();
        try
        {
            var json = await page.EvaluateAsync<string>(ScanModalJs, new { sel = rowSelector, own = ownUsername });
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.GetProperty("found").GetBoolean())
            {
                return (false, rows);
            }

            foreach (var row in root.GetProperty("rows").EnumerateArray())
            {
                rows.Add((row.GetProperty("u").GetString() ?? "", row.GetProperty("n").GetString() ?? ""));
            }

            return (true, rows);
        }
        catch
        {
            return (false, rows);
        }
    }

    /// <summary>Opens the full "Following accounts" list: tiktok.com/following -> sidebar
    /// "View all". Confirmed by hand (2026-10-27) to keep loading well past the 60-row cap that
    /// the profile-page "Following N" modal hits (TikTok-side limit, reproduced scrolling manually).</summary>
    private static async Task OpenFollowingPanelAsync(IPage page, string rowSelector, string ownUsername, CancellationToken ct)
    {
        // TikTok collapses the left sidebar to icons (no "View all" link) on narrow windows, and the
        // real Chrome window may be snapped/half-screen — so force a wide emulated viewport.
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync("https://www.tiktok.com/following?lang=en", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        // No whole-page 'error page' check here: the video feed can show 'Something went wrong' while the
        // sidebar (with 'View all') still works. If the link truly is missing, the wait below times out.
        var viewAll = page.GetByText(new System.Text.RegularExpressions.Regex("^(View all|Xem tất cả)$")).First;
        await viewAll.WaitForAsync(new LocatorWaitForOptions { Timeout = 25_000 });
        await viewAll.ClickAsync();
        await Task.Delay(2000, ct);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var (found, rows) = await ScanFollowingModalAsync(page, ownUsername, rowSelector);
            if (found && rows.Count > 0)
            {
                return;
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException(T("Panel Following không xuất hiện.", "The Following panel did not appear."));
    }

    /// <summary>Diagnostic only: remembers the shape of the latest paged API response (hasMore,
    /// cursors, list size, top-level keys) per path so the final log can show whether TikTok itself
    /// stopped paginating. Never throws.</summary>
    private static void RecordListBatch(
        string body, string urlPath, ConcurrentDictionary<string, string> info, ConcurrentDictionary<string, int> counts)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{')) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            if (!r.TryGetProperty("hasMore", out var hasMore)) return;

            var size = -1;
            foreach (var prop in r.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array) { size = prop.Value.GetArrayLength(); break; }
            }

            string Get(string n) => r.TryGetProperty(n, out var v) ? v.ToString() : "-";
            var path = urlPath.Replace("https://www.tiktok.com", "");
            counts.AddOrUpdate(path, 1, (_, c) => c + 1);
            info[path] = $"{path} lastBatchSize={size} hasMore={hasMore} cursor={Get("cursor")} minCursor={Get("minCursor")} maxCursor={Get("maxCursor")} total={Get("total")} isTruncated={Get("isTruncated")} keys=[{string.Join(",", r.EnumerateObject().Select(x => x.Name))}]";
        }
        catch { /* diagnostics must never break the scrape */ }
    }
    /// <summary>Tries to extract user records (uniqueId → userId + videoCount) from any TikTok
    /// API JSON response. TikTok embeds user objects in several different response shapes — this
    /// walks the JSON recursively looking for objects that contain both "uniqueId" and "id" fields
    /// (the canonical user-record fingerprint) and records them into <paramref name="result"/>.
    /// Never throws — a parse failure for one response must not interrupt the list-loading loop.
    /// </summary>
    internal static void ExtractUserDataFromApiResponse(
        string json,
        ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)> result)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{')) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            WalkJsonForUserRecords(doc.RootElement, result, depth: 0);
        }
        catch { /* malformed JSON or unexpected schema — silently skip */ }
    }

    private static void WalkJsonForUserRecords(
        JsonElement el,
        ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)> result,
        int depth)
    {
        if (depth > 10) return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkJsonForUserRecords(item, result, depth + 1);
                break;

            case JsonValueKind.Object:
                // Pattern A — flat user object: { "uniqueId": "...", "id": "...", "videoCount": N, "relation": N }
                if (el.TryGetProperty("uniqueId", out var uqA) && el.TryGetProperty("id", out var idA))
                {
                    var vcA = el.TryGetProperty("videoCount", out var vcElA) && vcElA.ValueKind == JsonValueKind.Number
                        ? vcElA.GetInt32() : -1;
                    var relA = el.TryGetProperty("relation", out var relElA) && relElA.ValueKind == JsonValueKind.Number
                        ? relElA.GetInt32() : -1;
                    RecordUser(uqA, idA, vcA, relA, result);
                    RecordAvatar(uqA, el);
                }

                // Pattern B — nested: { "user": { "uniqueId": "...", "id": "...", "relation": N }, "stats": { "videoCount": N } }
                if (el.TryGetProperty("user", out var userEl)
                    && userEl.TryGetProperty("uniqueId", out var uqB)
                    && userEl.TryGetProperty("id", out var idB))
                {
                    var vcB = -1;
                    if (el.TryGetProperty("stats", out var stats) && stats.TryGetProperty("videoCount", out var svc) && svc.ValueKind == JsonValueKind.Number)
                        vcB = svc.GetInt32();
                    else if (userEl.TryGetProperty("videoCount", out var uvc) && uvc.ValueKind == JsonValueKind.Number)
                        vcB = uvc.GetInt32();
                    var relB = userEl.TryGetProperty("relation", out var relElB) && relElB.ValueKind == JsonValueKind.Number
                        ? relElB.GetInt32() : -1;
                    RecordUser(uqB, idB, vcB, relB, result);
                    RecordAvatar(uqB, userEl);
                }

                // Walk all properties regardless of whether we matched above — response may have
                // multiple levels of user lists (e.g. a "recommended" array inside a following list).
                foreach (var prop in el.EnumerateObject())
                    WalkJsonForUserRecords(prop.Value, result, depth + 1);
                break;
        }
    }

    private static readonly ConcurrentDictionary<string, string> CapturedAvatarUrls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers an account's avatar link from the same user object the userId/videoCount are read from.</summary>
    private static void RecordAvatar(JsonElement uniqueIdEl, JsonElement userObj)
    {
        var uniqueId = uniqueIdEl.GetString();
        if (string.IsNullOrEmpty(uniqueId)) return;
        foreach (var name in new[] { "avatarThumb", "avatarMedium", "avatarLarger" })
        {
            if (userObj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
            {
                CapturedAvatarUrls.TryAdd(uniqueId, v.GetString()!);
                return;
            }
        }
    }

    private static void RecordUser(
        JsonElement uniqueIdEl, JsonElement idEl, int videoCount, int relation,
        ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)> result)
    {
        var uniqueId = uniqueIdEl.GetString() ?? "";
        var userId   = idEl.GetString() ?? "";
        if (string.IsNullOrEmpty(uniqueId) || string.IsNullOrEmpty(userId)) return;
        result.TryAdd(uniqueId, (userId, videoCount, relation));
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
    public async Task<bool> ClickUnfollowAsync(
        string username, SelectorsConfig selectors, string userId = "", CancellationToken ct = default)
    {
        EnsureLaunched();
        var page = _page!;

        await page.GotoAsync($"https://www.tiktok.com/@{username}?lang=en", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Task.Delay(1000, ct);

        // Accounts deleted from TikTok's CDN still appear in the following list (their record
        // stays in TikTok's DB) but their profile page returns "Access Denied" from Akamai.
        // Direct HttpClient calls to TikTok's API fail silently (confirmed: TikTok returns 2xx
        // but doesn't perform the unfollow without a valid X-Bogus signature that only its own
        // page JS can produce). Workaround: navigate to a live TikTok page and call fetch()
        // from WITHIN that page context — TikTok's own fetch interceptor will sign the request.
        var pageTitle = await page.TitleAsync();
        var pageText = await page.EvaluateAsync<string>("document.body?.innerText ?? ''");
        if (pageTitle.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
            || pageText.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
            || pageText.Contains("You don't have permission", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(userId))
            {
                try
                {
                    // Navigate to a live TikTok page so TikTok's JS (and its fetch signing
                    // interceptor) is active before we issue the unfollow API call.
                    try
                    {
                        await page.GotoAsync("https://www.tiktok.com/",
                            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10_000 });
                    }
                    catch { /* best effort — proceed even on timeout */ }

                    var statusCode = await page.EvaluateAsync<int>(@"
                        async (toUserId) => {
                            try {
                                const resp = await fetch('/api/relation/user/unfollow/', {
                                    method: 'POST',
                                    headers: { 'content-type': 'application/x-www-form-urlencoded' },
                                    credentials: 'include',
                                    body: new URLSearchParams({ userId: toUserId, action_type: '1' })
                                });
                                const data = await resp.json().catch(() => ({}));
                                return data.status_code ?? data.statusCode ?? -1;
                            } catch (e) { return -999; }
                        }
                    ", userId);

                    if (statusCode == 0) return true;
                    // Non-zero status — API rejected (deleted account may have been purged from
                    // relation DB too). Fall through to return true anyway: nothing more we can do.
                }
                catch { /* best effort */ }
            }
            return true; // account deleted/inaccessible — mark done regardless
        }

        var button = page.Locator(selectors.UnfollowBtn).First;
        if (await button.CountAsync() == 0)
        {
            throw new InvalidOperationException(T($"Không tìm thấy nút Follow/Unfollow trên trang @{username} (selector '{selectors.UnfollowBtn}').", $"The Follow/Unfollow button was not found on @{username}'s page (selector '{selectors.UnfollowBtn}')."));
        }

        // Wait for the button to be visible and have its label rendered before reading — without
        // this, aria-label and InnerText can both come back empty on a slow-loading profile page,
        // causing the empty string to not match "Following" and silently returning true (i.e.
        // "already unfollowed") without actually doing anything.
        try
        {
            await button.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch
        {
            // If the wait itself times out the button was never visible — fall through to the
            // empty-string guard below which will throw a proper error.
        }

        var before = (await button.GetAttributeAsync("aria-label")) ?? (await button.InnerTextAsync());
        if (string.IsNullOrWhiteSpace(before))
        {
            throw new InvalidOperationException(T($"Nút Follow trên @{username} không load được text sau 5 giây — trang chưa render đủ. Thử lại sau.", $"The Follow button on @{username} had no text after 5 seconds — the page has not fully rendered. Try again later."));
        }

        if (!before.Contains("Following", StringComparison.OrdinalIgnoreCase))
        {
            // Already not following (someone/something else unfollowed it already, or the
            // account never actually followed it) — nothing to do, and not a failure.
            return true;
        }

        await button.ClickAsync();
        await Task.Delay(800, ct);

        // TikTok's own "Unfollow?" confirmation isn't always a `<button>` — try scoped to
        // [role='dialog'] first to avoid matching "Unfollow" text elsewhere on the page (nav
        // links, section headings, etc.), then fall back to broader matches for older DOM variants.
        var modal = page.Locator("[role='dialog']").First;
        var hasModal = await modal.CountAsync() > 0;
        var confirmScope = hasModal ? modal : (ILocator)page;
        var confirm = confirmScope.GetByText("Unfollow", new LocatorGetByTextOptions { Exact = false }).First;
        if (await confirm.CountAsync() == 0)
        {
            confirm = confirmScope.Locator("button:has-text('Unfollow')").First;
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
            var dumpNote = dumpPath is not null ? T($" Đã lưu ảnh chụp + HTML để chẩn đoán: {dumpPath}", $" A screenshot + HTML were saved for diagnosis: {dumpPath}") : "";
            throw new InvalidOperationException(T($"Bấm nút Unfollow nhưng @{username} vẫn đang được theo dõi (không có tác dụng thật).", $"Clicked Unfollow but @{username} is still followed (it had no real effect).") + dumpNote);
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
    /// issue ourselves), and the NEWEST `createTime` (unix seconds) in the returned page is the latest post
    /// time (NOT `itemList[0]`: pinned posts are listed first and are old on purpose). Returns null on any failure (private/deleted
    /// account, selector/schema drift, etc.); caller treats that as "unknown", never as "dead".
    /// LastPostUnixSeconds specifically stays null when the account has videos but the
    /// intercept simply didn't arrive in time (slow network) — never guessed from partial data.</summary>
    public async Task<(string UserId, int VideoCount, long? LastPostUnixSeconds, int Relation)?> GetUserDetailAsync(string username, CancellationToken ct = default)
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

            var userEl2  = userInfo.GetProperty("user");
            var userId    = userEl2.GetProperty("id").GetString() ?? "";
            var videoCount = userInfo.GetProperty("stats").GetProperty("videoCount").GetInt32();
            var relation  = userEl2.TryGetProperty("relation", out var relEl) && relEl.ValueKind == JsonValueKind.Number
                ? relEl.GetInt32() : -1;

            long? lastPostUnixSeconds = null;
            if (videoCount > 0)
            {
                try
                {
                    var response = await itemListTask;
                    var body = await response.TextAsync();
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        // HTTP 200 with no content: TikTok's usual way of refusing a request it considers automated /
                        // too frequent, so it is counted separately from a real parse problem.
                        BumpDetailStat("itemlist_empty_body (TikTok returned no content - rate limited?)");
                        return (userId, videoCount, null, relation);
                    }

                    using var itemDoc = JsonDocument.Parse(body);
                    if (itemDoc.RootElement.TryGetProperty("itemList", out var itemList) && itemList.GetArrayLength() > 0)
                    {
                        lastPostUnixSeconds = NewestCreateTime(itemList);
                    }
                }
                catch
                {
                    // item_list didn't arrive in time, or its shape changed — leave last-post
                    // date unknown rather than guessing at it.
                }
            }
            else
            {
                // videoCount == 0 means the account never posted — no reason to wait for the
                // item_list response. Discard the task so its 15-second timeout doesn't fire as
                // an unobserved exception later (with 500+ accounts in a scan, those pile up fast).
                _ = itemListTask.ContinueWith(_ => { }, TaskContinuationOptions.None);
            }

            return (userId, videoCount, lastPostUnixSeconds, relation);
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

    /// <summary>Same as <see cref="GetUserDetailAsync"/> but uses a CALLER-SUPPLIED page instead
    /// of the shared <c>_page</c>. This makes it safe to call from multiple concurrent tasks that
    /// each own their own page (opened via <see cref="NewPageAsync"/>). The caller is responsible
    /// for closing the page when done.</summary>
    private static async Task<(string UserId, int VideoCount, long? LastPostUnixSeconds, int Relation)?> GetUserDetailOnPageAsync(
        IPage page, string username, CancellationToken ct)
    {
        JsonDocument? hydrationDoc = null;
        JsonDocument? apiDoc = null;
        EventHandler<IResponse>? onItemResponse = null;
        try
        {
            // Read the item_list body the moment its response arrives. Read later (after the page data), the browser may
            // already have dropped it: Network.getResponseBody then fails with "No resource with given identifier found"
            // (seen in the built-in browser, 3 of 9 profiles). A fault is observed so it is not reported as unobserved.
            var itemBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = itemBody.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            onItemResponse = (_, response) =>
            {
                if (!response.Url.Contains("/api/post/item_list/") || !response.Ok)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        try
                        {
                            itemBody.TrySetResult(await response.TextAsync());
                            return;
                        }
                        catch (Exception) when (attempt == 0)
                        {
                            await Task.Delay(300);
                        }
                        catch (Exception ex)
                        {
                            itemBody.TrySetException(ex);
                            return;
                        }
                    }
                });
            };
            page.Response += onItemResponse;

            // The page's own user/detail request (it carries a valid signature) is a second source for the
            // same data, used when the embedded page data is missing.
            var detailApiTask = page.WaitForResponseAsync(
                r => r.Url.Contains("/api/user/detail/") && r.Ok,
                new PageWaitForResponseOptions { Timeout = 15_000 });
            _ = detailApiTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            await page.GotoAsync($"https://www.tiktok.com/@{username}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            ct.ThrowIfCancellationRequested();

            JsonElement userInfo = default;
            var haveUserInfo = false;

            var raw = await ReadHydrationAsync(page);
            if (string.IsNullOrEmpty(raw))
            {
                // Seen live: the data script is sometimes not in the first HTML (client-rendered shell), so
                // read-immediately returned nothing for some accounts. Give the page a few seconds to produce it.
                try
                {
                    await page.WaitForFunctionAsync(
                        "() => !!document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__')",
                        null,
                        new PageWaitForFunctionOptions { Timeout = 6_000 });
                }
                catch
                {
                    // still absent - fall through to the API response below
                }

                raw = await ReadHydrationAsync(page);
            }

            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    hydrationDoc = JsonDocument.Parse(raw);
                    userInfo = hydrationDoc.RootElement
                        .GetProperty("__DEFAULT_SCOPE__")
                        .GetProperty("webapp.user-detail")
                        .GetProperty("userInfo");
                    haveUserInfo = true;
                    BumpDetailStat("detail_from_page_data");
                }
                catch
                {
                    // page data present but not the expected shape - try the API response
                }
            }

            if (!haveUserInfo)
            {
                try
                {
                    var detailResponse = await detailApiTask;
                    apiDoc = JsonDocument.Parse(await detailResponse.TextAsync());
                    userInfo = apiDoc.RootElement.GetProperty("userInfo");
                    haveUserInfo = true;
                    BumpDetailStat("detail_from_api_fallback");
                }
                catch
                {
                    // neither source worked
                }
            }

            if (!haveUserInfo)
            {
                BumpDetailStat("no_profile_data (neither page data nor user/detail response)");
                return null;
            }

            var userEl2   = userInfo.GetProperty("user");
            var userId    = userEl2.GetProperty("id").GetString() ?? "";
            var videoCount = userInfo.GetProperty("stats").GetProperty("videoCount").GetInt32();
            var relation  = userEl2.TryGetProperty("relation", out var relEl) && relEl.ValueKind == JsonValueKind.Number
                ? relEl.GetInt32() : -1;

            long? lastPostUnixSeconds = null;
            if (videoCount > 0)
            {
                try
                {
                    var body = await itemBody.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        // HTTP 200 with no content: TikTok's usual way of refusing a request it considers automated /
                        // too frequent, so it is counted separately from a real parse problem.
                        BumpDetailStat("itemlist_empty_body (TikTok returned no content - rate limited?)");
                        return (userId, videoCount, null, relation);
                    }

                    using var itemDoc = JsonDocument.Parse(body);
                    if (itemDoc.RootElement.TryGetProperty("itemList", out var itemList) && itemList.GetArrayLength() > 0)
                    {
                        lastPostUnixSeconds = NewestCreateTime(itemList);
                        if (lastPostUnixSeconds is null)
                        {
                            // Items came back but none carried a usable createTime - record what they look like.
                            var first = itemList[0];
                            var keys = first.ValueKind == JsonValueKind.Object
                                ? string.Join(",", first.EnumerateObject().Select(x => x.Name).Take(14))
                                : first.ValueKind.ToString();
                            BumpDetailStat($"itemlist_without_createTime (items={itemList.GetArrayLength()}, first item keys=[{keys}])");
                        }
                        else
                        {
                            BumpDetailStat("date_ok");
                        }
                    }
                    else
                    {
                        BumpDetailStat("itemlist_empty_or_missing (videoCount>0 but no itemList)");
                    }
                }
                catch (Exception ex)
                {
                    BumpDetailStat("itemlist_failed: " + ex.GetType().Name + " - " + ex.Message.Split('\n')[0]);
                }
            }
            else
            {
                BumpDetailStat("no_videos");
            }

            return (userId, videoCount, lastPostUnixSeconds, relation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            BumpDetailStat("profile_failed: " + ex.GetType().Name + " - " + ex.Message.Split('\n')[0]);
            return null;
        }
        finally
        {
            if (onItemResponse is not null)
            {
                page.Response -= onItemResponse;
            }

            hydrationDoc?.Dispose();
            apiDoc?.Dispose();
        }
    }

    private static Task<string?> ReadHydrationAsync(IPage page) =>
        page.EvaluateAsync<string?>("document.getElementById('__UNIVERSAL_DATA_FOR_REHYDRATION__')?.textContent ?? null");

    /// <summary>Newest createTime across a page of posts. The first item can be a PINNED post, which is old by
    /// design, so the latest post is the maximum over the page, not the first element.</summary>
    internal static long? NewestCreateTime(JsonElement itemList)
    {
        long? newest = null;
        foreach (var item in itemList.EnumerateArray())
        {
            if (item.TryGetProperty("createTime", out var ct)
                && ct.ValueKind == JsonValueKind.Number
                && ct.TryGetInt64(out var value)
                && (newest is null || value > newest))
            {
                newest = value;
            }
        }

        return newest;
    }
    private static readonly ConcurrentDictionary<string, int> DetailStats = new();

    private static void BumpDetailStat(string key) => DetailStats.AddOrUpdate(key, 1, (_, c) => c + 1);

    public static void ResetDetailStats() => DetailStats.Clear();

    /// <summary>Why profile visits did or did not yield a last-post date during the current scan
    /// (reason -> count), most common first. For the log only.</summary>
    public static string DescribeDetailStats() =>
        string.Join(" | ", DetailStats.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}x {kv.Key}"));
    /// <summary>Opens a fresh page in the shared browser context, fetches the user detail for
    /// <paramref name="username"/> via <see cref="GetUserDetailOnPageAsync"/>, then closes the
    /// page. Safe to call concurrently from multiple tasks as long as the browser context is
    /// still open. Returns null on any failure so callers never need to catch.</summary>
    public async Task<(string UserId, int VideoCount, long? LastPostUnixSeconds, int Relation)?> GetUserDetailParallelAsync(
        string username, CancellationToken ct = default)
    {
        EnsureLaunched();
        if (_host is { WorkerRunsOneAtATime: true })
        {
            // The built-in browser has one hidden reading page: use it for one profile at a time.
            await _workerGate.WaitAsync(ct);
            try
            {
                _workerPage ??= await _host.NewWorkerPageAsync(ct);
                return await GetUserDetailOnPageAsync(_workerPage, username, ct);
            }
            finally
            {
                _workerGate.Release();
            }
        }

        var page = await _context!.NewPageAsync();
        try
        {
            return await GetUserDetailOnPageAsync(page, username, ct);
        }
        finally
        {
            try { await page.CloseAsync(); } catch { }
        }
    }

    /// <summary>Lightweight session liveness check — reads the sessionid cookie from the browser
    /// context (no network round-trip). Returns false if the cookie is missing or empty, which
    /// happens when TikTok has invalidated the session (redirect to login). Returns true on any
    /// error so an uncertain check never aborts a long-running scan.</summary>
    public async Task<bool> IsSessionActiveAsync(CancellationToken ct = default)
    {
        if (_context is null) return false;
        try
        {
            var cookies = await _context.CookiesAsync(["https://www.tiktok.com"]);
            return cookies.Any(c =>
                c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(c.Value));
        }
        catch
        {
            return true; // benefit of the doubt — don't abort scan on an uncertain check
        }
    }

    private void EnsureLaunched()
    {
        if (_page is null)
        {
            throw new InvalidOperationException(T("Playwright chưa được khởi động — gọi LaunchAsync trước.", "Playwright has not been started — call LaunchAsync first."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
        catch
        {
            // best effort
        }

        _playwright?.Dispose();

        if (_profileDir is not null)
        {
            try { Directory.Delete(_profileDir, true); } catch { /* best effort cleanup */ }
        }

        _context = null;
        _page = null;
        _workerPage = null;
        _playwright = null;
        _host = null;
        _profileDir = null;
    }}
