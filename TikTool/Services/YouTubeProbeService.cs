using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;

namespace TikTool.Services;

/// <summary>THROWAWAY PROTOTYPE (read-only, never unsubscribes). Answers the open questions before a real YouTube
/// feature is built: (1) does Google let you sign in inside a Chrome window this tool launches (persistent profile,
/// real Chrome over CDP, no cookie reading), (2) can the subscription list be read from /feed/channels, (3) can a
/// channel's UC... id be resolved from its @handle page, and (4) does the public RSS feed give the latest upload date.
/// Everything is reported through <paramref name="log"/> so the numbers end up in the session log.</summary>
internal sealed class YouTubeProbeService
{
    private const string FeedUrl = "https://www.youtube.com/feed/channels?hl=en";
    private const string SignInUrl = "https://accounts.google.com/ServiceLogin?service=youtube&hl=en&continue=https%3A%2F%2Fwww.youtube.com%2Ffeed%2Fchannels%3Fhl%3Den";
    private const int SampleSize = 10;

    private static readonly string[] SampleHandles = { "YouTube", "mkbhd", "veritasium", "3blue1brown", "Fireship", "LinusTechTips", "kurzgesagt", "TED", "NASA", "Computerphile" };

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    public async Task RunAsync(IProgress<string> log, CancellationToken ct)
    {
        var chromePath = PlaywrightService.FindRealChromeExecutable()
            ?? throw new InvalidOperationException(T("Không tìm thấy Google Chrome/Edge trên máy.", "Google Chrome/Edge was not found on this computer."));

        var profileDir = AppPaths.YouTubeProfileDir;
        var port = GetFreePort();
        Process? chrome = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IPage? page = null;
        var watch = Stopwatch.StartNew();

        try
        {
            log.Report($"[YouTube] Chrome: {chromePath}");
            log.Report($"[YouTube] Profile (persistent): {profileDir}");

            chrome = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check --window-size=1280,900 about:blank",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Could not start Chrome.");

            playwright = await Playwright.CreateAsync();
            browser = await ConnectAsync(playwright, port, ct);
            var context = browser.Contexts.FirstOrDefault() ?? await browser.NewContextAsync();
            page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

            // 1) sign-in ---------------------------------------------------------------------------------------
            await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            var signedIn = await IsSignedInAsync(page);
            log.Report(signedIn
                ? "✓ [1/4] Already signed in to YouTube (the profile remembered the login)."
                : "[1/4] Not signed in yet - opening the Google sign-in page. Please sign in in the Chrome window (up to 5 minutes)...");

            if (!signedIn)
            {
                await page.GotoAsync(SignInUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                var warnedBlocked = false;
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(2000, ct);
                    try
                    {
                        var url = page.Url;
                        if (url.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!warnedBlocked)
                            {
                                var text = await page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''");
                                if (text.Contains("may not be secure", StringComparison.OrdinalIgnoreCase)
                                    || text.Contains("Couldn't sign you in", StringComparison.OrdinalIgnoreCase)
                                    || text.Contains("Couldn\u2019t sign you in", StringComparison.OrdinalIgnoreCase))
                                {
                                    warnedBlocked = true;
                                    log.Report("⚠ [1/4] Google shows a 'browser may not be secure / couldn't sign you in' message - sign-in inside a tool-launched Chrome may be BLOCKED.");
                                }
                            }
                        }
                        else if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) && await IsSignedInAsync(page))
                        {
                            signedIn = true;
                            break;
                        }
                    }
                    catch (PlaywrightException)
                    {
                        // mid-navigation: try again on the next tick
                    }
                }

                if (!signedIn)
                {
                    log.Report("⚠ [1/4] RESULT: could not sign in within 5 minutes" + (warnedBlocked ? " (Google showed a blocking message)." : "."));
                    return;
                }

                log.Report("✓ [1/4] Signed in.");
            }

            // 2) subscription list -------------------------------------------------------------------------------
            if (!page.Url.Contains("/feed/channels", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            }

            var channels = new List<(string Name, string Href, string Meta, bool Subscribed)>();
            var listFound = true;
            try
            {
                await page.WaitForSelectorAsync("ytd-channel-renderer", new PageWaitForSelectorOptions { Timeout = 20_000 });
            }
            catch (TimeoutException)
            {
                listFound = false;
                log.Report("⚠ [2/4] RESULT: no 'ytd-channel-renderer' rows on /feed/channels - this account may have NO subscriptions, or YouTube changed the page. Reading the list is therefore NOT tested.");
                await DumpPageAsync(page, log);
            }

            if (listFound)
            {
                var listWatch = Stopwatch.StartNew();
                var previous = -1;
                var stable = 0;
                var count = 0;
                for (var i = 0; i < 120 && stable < 3; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    count = await page.EvaluateAsync<int>("() => document.querySelectorAll('ytd-channel-renderer').length");
                    stable = count == previous ? stable + 1 : 0;
                    previous = count;
                    await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
                    await Task.Delay(800, ct);
                    if (i % 8 == 7)
                    {
                        log.Report($"[2/4] loading the list... {count} rows so far");
                    }
                }

                log.Report($"✓ [2/4] RESULT: {count} subscribed channels listed in {listWatch.Elapsed.TotalSeconds:0.0}s.");

                var json = await page.EvaluateAsync<string>(@"() => JSON.stringify([...document.querySelectorAll('ytd-channel-renderer')].map(r => {
                    const a = r.querySelector('a#main-link') || r.querySelector('a[href^=""/""]') || r.querySelector('a');
                    const name = (r.querySelector('ytd-channel-name') || {}).textContent || '';
                    const meta = [...r.querySelectorAll('#metadata yt-formatted-string, #subscribers, #video-count')]
                        .map(e => (e.textContent || '').trim()).filter(Boolean).join(' | ');
                    return { name: name.trim(), href: a ? a.href : '', meta: meta, subscribed: !!r.querySelector('ytd-subscribe-button-renderer[subscribed]') };
                }))");

                using (var doc = JsonDocument.Parse(json))
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        channels.Add((
                            el.GetProperty("name").GetString() ?? "",
                            el.GetProperty("href").GetString() ?? "",
                            el.GetProperty("meta").GetString() ?? "",
                            el.GetProperty("subscribed").GetBoolean()));
                    }
                }

                var withLink = channels.Count(c => !string.IsNullOrEmpty(c.Href));
                var subscribedBtn = channels.Count(c => c.Subscribed);
                log.Report($"[2/4] rows with a link: {withLink}/{channels.Count}; rows with a 'subscribed' button: {subscribedBtn}/{channels.Count}");
                foreach (var (c, i) in channels.Take(15).Select((c, i) => (c, i)))
                {
                    log.Report($"   {i + 1}. {c.Name} | {c.Href} | {c.Meta}");
                }
            }
            else
            {
                foreach (var handle in SampleHandles)
                {
                    channels.Add((handle, $"https://www.youtube.com/@{handle}", "(built-in public sample)", false));
                }

                log.Report($"[2/4] continuing with {channels.Count} built-in PUBLIC sample channels, only to test steps 3-4 (channel id + RSS).");
            }
            // 3) handle -> channel id, 4) RSS ----------------------------------------------------------------------
            var resolved = 0;
            var rssOk = 0;
            var fallbackTried = 0;
            var fallbackOk = 0;
            var idMs = new List<long>();
            var rssMs = new List<long>();
            log.Report($"[3/4] resolving channel ids and reading RSS for the first {Math.Min(SampleSize, channels.Count)} channels (slow on purpose)...");

            foreach (var c in channels.Take(SampleSize))
            {
                ct.ThrowIfCancellationRequested();
                var step = Stopwatch.StartNew();
                string? channelId = ExtractChannelIdFromUrl(c.Href);
                var how = channelId is not null ? "from the link" : "";

                if (channelId is null && !string.IsNullOrEmpty(c.Href))
                {
                    try
                    {
                        var html = await Http.GetStringAsync(c.Href, ct);
                        channelId = ExtractChannelIdFromHtml(html);
                        how = channelId is not null ? "from the channel page" : $"NOT FOUND in the page ({html.Length} chars)";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        how = $"page request failed: {ex.Message}";
                    }
                }

                idMs.Add(step.ElapsedMilliseconds);
                if (channelId is null)
                {
                    log.Report($"   ✗ {c.Name}: channel id {how}");
                    await Task.Delay(500, ct);
                    continue;
                }

                resolved++;
                var rssFailed = false;
                step.Restart();
                try
                {
                    using var response = await Http.GetAsync($"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}", ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        log.Report($"   ✗ {c.Name} ({channelId}, {how}): RSS HTTP {(int)response.StatusCode}");
                        rssFailed = true;
                    }
                    else
                    {
                        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        XNamespace atom = "http://www.w3.org/2005/Atom";
                        var dates = xml.Descendants(atom + "entry")
                            .Select(e => DateTimeOffset.TryParse((string?)e.Element(atom + "published"), out var d) ? d : (DateTimeOffset?)null)
                            .Where(d => d is not null)
                            .Select(d => d!.Value)
                            .ToList();
                        if (dates.Count == 0)
                        {
                            log.Report($"   ~ {c.Name} ({channelId}, {how}): RSS ok but no videos listed");
                        }
                        else
                        {
                            rssOk++;
                            var newest = dates.Max();
                            log.Report($"   ✓ {c.Name} ({channelId}, {how}): newest upload {(DateTimeOffset.UtcNow - newest).TotalDays:0} days ago ({newest:yyyy-MM-dd}), {dates.Count} videos in feed");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Report($"   ✗ {c.Name} ({channelId}): RSS failed: {ex.Message}");
                    rssFailed = true;
                }

                rssMs.Add(step.ElapsedMilliseconds);

                // YouTube's RSS server is known to answer 404 for every feed for hours at a time, so it cannot be the only
                // source. Fallback: the channel's own Videos tab (Latest) in the signed-in browser, read as "N days ago".
                if (rssFailed && page is not null && !string.IsNullOrEmpty(c.Href))
                {
                    fallbackTried++;
                    var fb = Stopwatch.StartNew();
                    try
                    {
                        var age = await ReadLatestFromChannelPageAsync(page, c.Href, ct);
                        if (age.Contains('≈', StringComparison.Ordinal))
                        {
                            fallbackOk++;
                        }

                        log.Report($"      ↳ fallback via the channel's Videos page: newest upload \"{age}\" ({fb.Elapsed.TotalSeconds:0.0}s)");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log.Report($"      ↳ fallback failed: {ex.Message}");
                    }
                }                await Task.Delay(500, ct);
            }

            var sampled = Math.Min(SampleSize, channels.Count);
            log.Report($"✓ [3/4] RESULT: channel id resolved for {resolved}/{sampled}" + (idMs.Count > 0 ? $" (avg {idMs.Average():0} ms)" : ""));
            log.Report($"✓ [4/4] RESULT: RSS gave a latest-upload date for {rssOk}/{sampled}" + (rssMs.Count > 0 ? $" (avg {rssMs.Average():0} ms)" : ""));
            if (fallbackTried > 0)
            {
                log.Report($"✓ [4/4] FALLBACK RESULT: the channel Videos page gave a relative age for {fallbackOk}/{fallbackTried} channels where RSS failed");
            }

            log.Report($"=== Prototype finished in {watch.Elapsed.TotalSeconds:0}s. Nothing was unsubscribed. ===");
        }
        finally
        {
            await CloseChromeAsync(page, chrome);
            try { browser = null; playwright?.Dispose(); } catch { /* best effort */ }
        }
    }

    /// <summary>Why did the list not show up? Saves a screenshot + the HTML and logs which YouTube components the page
    /// really contains (counts per element name, a few candidate selectors, the visible text).</summary>
    private static async Task DumpPageAsync(IPage page, IProgress<string> log)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseName = Path.Combine(AppPaths.DebugDir, $"{stamp}_youtube-feed");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = baseName + ".png" });
            await File.WriteAllTextAsync(baseName + ".html", await page.ContentAsync());
            log.Report($"[2/4] saved a screenshot + HTML for diagnosis: {baseName}.png / .html");

            var info = await page.EvaluateAsync<string>(@"() => {
                const counts = {};
                for (const e of document.querySelectorAll('*')) {
                    const t = e.tagName.toLowerCase();
                    if (t.startsWith('ytd-') || t.startsWith('yt-')) counts[t] = (counts[t] || 0) + 1;
                }
                const top = Object.entries(counts).sort((a, b) => b[1] - a[1]).slice(0, 45).map(([k, v]) => k + '=' + v).join(', ');
                const probes = ['ytd-channel-renderer', 'ytd-subscribe-button-renderer', 'yt-subscribe-button-view-model',
                                'ytd-grid-channel-renderer', 'yt-lockup-view-model', 'a[href^=""/@""]', 'a[href^=""/channel/""]']
                    .map(s => s + '=' + document.querySelectorAll(s).length).join(', ');
                const text = (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').slice(0, 500);
                return 'url=' + location.href + '\nprobes: ' + probes + '\ntags: ' + top + '\ntext: ' + text;
            }");
            foreach (var line in info.Split('\n'))
            {
                log.Report("   " + line);
            }
        }
        catch (Exception ex)
        {
            log.Report($"   (diagnostic dump failed: {ex.Message})");
        }
    }
    /// <summary>Reads how long ago the newest video was posted from the channel's Videos tab (default sort: Latest), e.g.
    /// "3 days ago". Approximate by design (YouTube only shows a coarse age) - fine for "inactive for 180+ days".</summary>
    private static async Task<string> ReadLatestFromChannelPageAsync(IPage page, string href, CancellationToken ct)
    {
        var uri = new Uri(href);
        var url = $"https://www.youtube.com{uri.AbsolutePath.TrimEnd('/')}/videos?hl=en";
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        // The grid is filled in after the page loads (it was empty when read after a fixed delay), so wait until a
        // relative-age line like "3 days ago" actually exists in the page text. Polled with a function-style evaluate:
        // YouTube's Trusted Types policy rejects the string-eval that WaitForFunction uses ("EvalError ... Trusted Type").
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

            await Task.Delay(500, ct);
        }
        var text = await page.EvaluateAsync<string>(
            "() => { const g = document.querySelector('ytd-rich-grid-renderer'); return (g && g.innerText.trim()) ? g.innerText : (document.body ? document.body.innerText : ''); }");
        var m = Regex.Match(text, @"(\d+)\s*(second|minute|hour|day|week|month|year|sec|min|mo|[smhdwy])s?\s+ago");
        if (m.Success)
        {
            return $"{m.Value} (≈{ApproxDays(int.Parse(m.Groups[1].Value), m.Groups[2].Value)} days)";
        }

        if (text.Contains("doesn't have any content", StringComparison.OrdinalIgnoreCase))
        {
            return "no videos";
        }

        var snippet = Regex.Replace(text, @"\s+", " ").Trim();
        if (snippet.Length > 220)
        {
            snippet = snippet[..220];
        }

        var counts = await page.EvaluateAsync<string>(
            "() => ['ytd-rich-grid-renderer','ytd-rich-item-renderer','yt-lockup-view-model','ytd-grid-video-renderer','ytd-two-column-browse-results-renderer'].map(s => s + '=' + document.querySelectorAll(s).length).join(', ')");
        return $"age not found | url={page.Url} | {counts} | text: {snippet}";
    }
    /// <summary>"13h ago" / "5d ago" / "9mo ago" / "3y ago" (and the long forms) to an approximate number of days.</summary>
    private static int ApproxDays(int amount, string unit) => unit.ToLowerInvariant() switch
    {
        "y" or "year" => amount * 365,
        "mo" or "month" => amount * 30,
        "w" or "week" => amount * 7,
        "d" or "day" => amount,
        _ => 0, // seconds, minutes, hours
    };
    private sealed class Session
    {
        public Process? Chrome;
        public IPlaywright? Playwright;
        public IBrowser? Browser;
        public IPage? Page;
    }

    /// <summary>Launches the tool's own Chrome (persistent profile), attaches and makes sure YouTube is signed in.</summary>
    private async Task<Session> OpenSignedInAsync(IProgress<string> log, CancellationToken ct)
    {
        var chromePath = PlaywrightService.FindRealChromeExecutable()
            ?? throw new InvalidOperationException(T("Không tìm thấy Google Chrome/Edge trên máy.", "Google Chrome/Edge was not found on this computer."));
        var port = GetFreePort();
        var s = new Session();
        try
        {
            s.Chrome = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{AppPaths.YouTubeProfileDir}\" --no-first-run --no-default-browser-check --window-size=1280,900 about:blank",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Could not start Chrome.");

            s.Playwright = await Playwright.CreateAsync();
            s.Browser = await ConnectAsync(s.Playwright, port, ct);
            var context = s.Browser.Contexts.FirstOrDefault() ?? await s.Browser.NewContextAsync();
            s.Page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

            await s.Page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            if (await IsSignedInAsync(s.Page))
            {
                log.Report("✓ Already signed in to YouTube.");
                return s;
            }

            log.Report("Not signed in - opening the Google sign-in page. Please sign in in the Chrome window (up to 5 minutes)...");
            await s.Page.GotoAsync(SignInUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(2000, ct);
                try
                {
                    if (s.Page.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) && await IsSignedInAsync(s.Page))
                    {
                        log.Report("✓ Signed in.");
                        return s;
                    }
                }
                catch (PlaywrightException)
                {
                    // mid-navigation
                }
            }

            throw new InvalidOperationException("Not signed in within 5 minutes.");
        }
        catch
        {
            await CloseChromeAsync(s.Page, s.Chrome);
            try { s.Playwright?.Dispose(); } catch { /* best effort */ }
            throw;
        }
    }

    private static async Task<List<(string Name, string Href)>> ReadListAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => JSON.stringify([...document.querySelectorAll('ytd-channel-renderer')].map(r => {
            const a = r.querySelector('a#main-link') || r.querySelector('a[href^=""/""]') || r.querySelector('a');
            return { name: ((r.querySelector('ytd-channel-name') || {}).textContent || '').trim(), href: a ? a.href : '' };
        }))");
        var list = new List<(string, string)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add((Regex.Replace(el.GetProperty("name").GetString() ?? "", @"\s+", " ").Trim(), el.GetProperty("href").GetString() ?? ""));
        }

        return list;
    }

    /// <summary>THE ONLY WRITE ACTION OF THE PROTOTYPE. Unsubscribes from exactly ONE channel after the caller's
    /// <paramref name="confirm"/> says yes, then verifies it twice (the row's button changed; the channel is gone from the
    /// list after a reload). The channel is chosen by <paramref name="nameFilter"/> (substring of name or link), or the first
    /// row when empty. Re-subscribing is possible from the channel's own page.</summary>
    public async Task UnsubscribeOneAsync(string nameFilter, Func<string, bool> confirm, IProgress<string> log, CancellationToken ct)
    {
        var s = await OpenSignedInAsync(log, ct);
        try
        {
            var page = s.Page!;
            if (!page.Url.Contains("/feed/channels", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            }

            await page.WaitForSelectorAsync("ytd-channel-renderer", new PageWaitForSelectorOptions { Timeout = 20_000 });
            var list = await ReadListAsync(page);
            log.Report($"[unsubscribe-test] {list.Count} channels in the list.");

            var idx = string.IsNullOrWhiteSpace(nameFilter)
                ? 0
                : list.FindIndex(c => c.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) || c.Href.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx >= list.Count)
            {
                log.Report($"⚠ [unsubscribe-test] no channel matches '{nameFilter}'. Nothing was changed.");
                return;
            }

            var target = list[idx];
            log.Report($"[unsubscribe-test] target: #{idx + 1} {target.Name} | {target.Href}");
            if (!confirm($"{target.Name}\n{target.Href}"))
            {
                log.Report("[unsubscribe-test] cancelled by the user. Nothing was changed.");
                return;
            }

            var row = page.Locator("ytd-channel-renderer").Nth(idx);

            // Watch for the request that really performs the unsubscribe. The row's button flips instantly in the UI
            // (optimistic update), so only the server's answer proves it worked - and navigating away too early can cancel it.
            var unsubscribeResponse = page.WaitForResponseAsync(
                r => r.Url.Contains("/subscription/unsubscribe", StringComparison.OrdinalIgnoreCase),
                new PageWaitForResponseOptions { Timeout = 12_000 });
            _ = unsubscribeResponse.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var button = row.Locator("ytd-subscribe-button-renderer button").First;
            try
            {
                await button.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 3_000 });
            }
            catch (TimeoutException)
            {
                // not needed for a DOM click
            }

            // Playwright refuses to click this button ("element is not visible": it sits in a container YouTube keeps
            // hidden), so click it the way the page's own script does - a DOM click does not require visibility.
            await button.EvaluateAsync("b => b.click()");
            log.Report("[unsubscribe-test] clicked the row's 'subscribed' button, waiting for YouTube's confirmation dialog...");

            string[] selectors =
            {
                "yt-confirm-dialog-renderer #confirm-button button",
                "button[aria-label=\"Unsubscribe\"]",
                "#confirm-button button",
            };
            string? used = null;
            var until = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < until && used is null)
            {
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

                        used = selector;
                        break;
                    }
                }

                if (used is null)
                {
                    await Task.Delay(300, ct);
                }
            }

            if (used is null)
            {
                log.Report("⚠ [unsubscribe-test] no confirmation dialog appeared (YouTube may have changed the flow). Saving a diagnosis; nothing else was clicked.");
                await DumpPageAsync(page, log);
                return;
            }

            log.Report($"[unsubscribe-test] clicked the confirm button via: {used}");

            try
            {
                var response = await unsubscribeResponse;
                var body = (await response.TextAsync()).Replace("\n", " ");
                log.Report($"{(response.Ok ? "✓" : "⚠")} [server] unsubscribe request answered HTTP {response.Status} ({body[..Math.Min(160, body.Length)]})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Report($"⚠ [server] no unsubscribe request was seen within 12 s ({ex.Message.Split('\n')[0]}) - the click may not have reached YouTube's server.");
            }

            await Task.Delay(2000, ct);
            var buttonChanged = false;
            var verifyUntil = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < verifyUntil)
            {
                if (await row.Locator("ytd-subscribe-button-renderer[subscribed]").CountAsync() == 0)
                {
                    buttonChanged = true;
                    break;
                }

                await Task.Delay(500, ct);
            }

            log.Report(buttonChanged
                ? "✓ [check 1/2] the row no longer shows the channel as subscribed."
                : "⚠ [check 1/2] the row still shows the channel as subscribed.");

            await page.GotoAsync(FeedUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            try
            {
                await page.WaitForSelectorAsync("ytd-channel-renderer", new PageWaitForSelectorOptions { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                // it may have been the last subscription: an empty list is a valid outcome
            }

            var after = await ReadListAsync(page);
            var stillThere = after.Any(c => string.Equals(c.Href, target.Href, StringComparison.OrdinalIgnoreCase));
            log.Report(stillThere
                ? $"⚠ [check 2/2] after reloading, '{target.Name}' is STILL in the list ({after.Count} channels)."
                : $"✓ [check 2/2] after reloading, '{target.Name}' is gone from the list ({list.Count} -> {after.Count} channels).");
            log.Report($"[unsubscribe-test] to subscribe again: open {target.Href}");
            log.Report("=== Unsubscribe test finished. ===");
        }
        finally
        {
            await CloseChromeAsync(s.Page, s.Chrome);
            try { s.Playwright?.Dispose(); } catch { /* best effort */ }
        }
    }
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

    private static string? ExtractChannelIdFromUrl(string href)
    {
        var m = Regex.Match(href ?? "", @"/channel/(UC[\w-]{22})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractChannelIdFromHtml(string html)
    {
        foreach (var pattern in new[]
                 {
                     @"<link rel=""canonical"" href=""https://www\.youtube\.com/channel/(UC[\w-]{22})""",
                     @"""externalId"":""(UC[\w-]{22})""",
                     @"""channelId"":""(UC[\w-]{22})""",
                     @"channel_id=(UC[\w-]{22})",
                 })
        {
            var m = Regex.Match(html, pattern);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return null;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<IBrowser> ConnectAsync(IPlaywright playwright, int port, CancellationToken ct)
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

    /// <summary>Quit Chrome through its own CDP "Browser.close" so the profile is flushed cleanly (cookies of the sign-in
    /// survive); a hard kill can lose the newest cookies. Falls back to killing the process.</summary>
    private static async Task CloseChromeAsync(IPage? page, Process? chrome)
    {
        try
        {
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
            if (chrome is not null && !chrome.WaitForExit(6000))
            {
                chrome.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}