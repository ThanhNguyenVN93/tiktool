using Microsoft.Playwright;
using TikTool.Browser;

namespace TikTool.Services;

/// <summary>Recognises a page that is asking the user to prove they are human. The markup of TikTok's real challenge has not been seen yet, so the
/// probe is deliberately loose: a VISIBLE element whose id/class/iframe mentions a captcha, or a well-known challenge sentence. Every episode
/// is written to challenges.log with a screenshot and the HTML in the debug folder, so the real markup can be learned from the first real one.</summary>
internal static class ChallengeDetector
{
    private const string ProbeJs = """
        () => {
          const visible = e => {
            const r = e.getBoundingClientRect();
            const s = getComputedStyle(e);
            return r.width >= 40 && r.height >= 40 && s.visibility !== 'hidden' && s.display !== 'none' && s.opacity !== '0';
          };
          if (location.hostname.endsWith('google.com') && location.pathname.startsWith('/sorry')) return 'google-unusual-traffic';
          const selector = 'iframe[src*="captcha" i], iframe[src*="recaptcha" i], iframe[src*="hcaptcha" i], iframe[title*="captcha" i], [id*="captcha" i], [class*="captcha" i], [class*="secsdk" i]';
          for (const e of document.querySelectorAll(selector)) {
            if (visible(e)) return 'captcha:' + String(e.id || e.className || e.tagName).slice(0, 60);
          }
          const text = ((document.body && document.body.innerText) || '').slice(0, 5000).toLowerCase();
          for (const p of ['drag the slider', 'verify to continue', 'select 2 objects', 'select two objects', 'security verification', 'verify you are human', 'verify that you are human']) {
            if (text.includes(p)) return 'captcha-text:' + p;
          }
          return '';
        }
        """;

    /// <summary>Empty when the page shows no challenge (or cannot be asked, for example in the middle of a navigation).</summary>
    public static async Task<string> ProbeAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string>(ProbeJs) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}

/// <summary>Lets the work stop and wait when a page shows a challenge, instead of carrying on and hammering the site. At the moment a challenge is seen
/// the browser window is shown on top (also when it runs in the background), the user is alerted, and the work waits, re-reminding at
/// 1, 2, 5, 10 minutes and then every 30 minutes, with no upper limit (the user may be away). The tool never touches the challenge or reloads the page
/// (the site re-issues the challenge by itself when it expires). When the challenge is gone it waits a cooldown and the interrupted step is run again.</summary>
internal sealed class ChallengeGate
{
    private readonly string _platform;
    private readonly IBrowserHost _host;
    private readonly Func<IReadOnlyList<IPage>> _pages;
    private readonly SemaphoreSlim _one = new(1, 1);

    public ChallengeGate(string platform, IBrowserHost host, Func<IReadOnlyList<IPage>> pages)
    {
        _platform = platform;
        _host = host;
        _pages = pages;
    }

    /// <summary>Seconds to wait after a challenge is solved before the work goes on.</summary>
    public Func<int> CooldownSeconds { get; set; } = () => 90;

    /// <summary>How long after the start of an episode the n-th reminder is due (0 based).</summary>
    public Func<int, TimeSpan> ReminderAfter { get; set; } = DefaultReminderAfter;

    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>User-facing lines (the pages show them in their log).</summary>
    public event Action<string>? Notice;

    /// <summary>How many challenges have been handled so far.</summary>
    public int Episodes { get; private set; }

    internal static TimeSpan DefaultReminderAfter(int n) => n switch
    {
        0 => TimeSpan.FromMinutes(1),
        1 => TimeSpan.FromMinutes(2),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(10 + 30 * (n - 3)),
    };

    /// <summary>Runs one unit of work. A challenge that is showing before it starts is waited out first; a challenge that appears during it, or after it
    /// (or that makes it fail), is waited out and the unit is run again. With <paramref name="redoWhenChallengedAfterSuccess"/> false a step that
    /// finished normally is kept even if a challenge shows up right after it (use that for steps that must not be repeated).</summary>
    public async Task<T> RunStepAsync<T>(Func<Task<T>> step, string what, CancellationToken ct, bool redoWhenChallengedAfterSuccess = true)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await CheckpointAsync(what, ct);
            T result;
            try
            {
                result = await step();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure can be caused by a challenge covering the page.
                if (await CheckpointAsync(what, ct))
                {
                    continue;
                }

                throw;
            }

            if (await CheckpointAsync(what, ct) && redoWhenChallengedAfterSuccess)
            {
                continue;
            }

            return result;
        }

        return await step();
    }

    /// <summary>Looks at the pages now; when one shows a challenge, handles it (blocks until it is gone). True when a challenge had to be handled.</summary>
    public async Task<bool> CheckpointAsync(string what, CancellationToken ct)
    {
        var handled = false;
        while (true)
        {
            var (page, reason) = await ProbeAllAsync();
            if (page is null)
            {
                return handled;
            }

            await _one.WaitAsync(ct);
            try
            {
                var again = await ChallengeDetector.ProbeAsync(page);
                if (again.Length > 0)
                {
                    await HandleAsync(page, again, what, ct);
                }

                handled = true;
            }
            finally
            {
                _one.Release();
            }
        }
    }

    private async Task<(IPage? Page, string Reason)> ProbeAllAsync()
    {
        foreach (var page in _pages())
        {
            if (page.IsClosed)
            {
                continue;
            }

            var reason = await ChallengeDetector.ProbeAsync(page);
            if (reason.Length > 0)
            {
                return (page, reason);
            }
        }

        return (null, "");
    }

    private async Task HandleAsync(IPage page, string reason, string what, CancellationToken ct)
    {
        var started = DateTime.Now;
        Episodes++;
        var wasHidden = _host.CanHide && !_host.IsVisible;
        var captchaRequests = 0;
        EventHandler<IRequest> onRequest = (_, request) =>
        {
            if (request.Url.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref captchaRequests);
            }
        };
        page.Request += onRequest;

        try
        {
            Log("WARN", $"START what=\"{what}\" reason={reason} url={page.Url} windowWasHidden={wasHidden}");
            await SnapshotAsync(page);
            await _host.ShowForChallengeAsync(page);

            Notice?.Invoke(T($"⚠ {_platform} yêu cầu xác minh (captcha) khi đang {what}. Cửa sổ trình duyệt đã hiện lên: hãy giải xác minh, tool sẽ tự chạy tiếp.",
                             $"⚠ {_platform} is asking for verification (captcha) while {what}. The browser window is showing: solve it and the tool carries on by itself."));
            UiThread.Alert("Social Toolkit", T($"{_platform} cần bạn giải xác minh (captcha).", $"{_platform} needs you to solve a verification (captcha)."));

            var reminders = 0;
            var clearProbes = 0;
            while (true)
            {
                await Task.Delay(ProbeInterval, ct);
                if ((await ChallengeDetector.ProbeAsync(page)).Length == 0)
                {
                    if (++clearProbes >= 2)
                    {
                        break;
                    }
                }
                else
                {
                    clearProbes = 0;
                }

                var elapsed = DateTime.Now - started;
                if (elapsed >= ReminderAfter(reminders))
                {
                    reminders++;
                    var minutes = (int)Math.Round(elapsed.TotalMinutes);
                    Log("INFO", $"WAITING {minutes} min, reminder {reminders}, captcha requests so far {captchaRequests}");
                    Notice?.Invoke(T($"⚠ Vẫn đang chờ bạn giải xác minh {_platform} (đã chờ {minutes} phút).", $"⚠ Still waiting for you to solve the {_platform} verification ({minutes} min so far)."));
                    UiThread.Alert("Social Toolkit", T($"Vẫn đang chờ bạn giải xác minh {_platform}.", $"Still waiting for you to solve the {_platform} verification."));
                }
            }

            var total = DateTime.Now - started;
            Log("INFO", $"CLEARED after {total.TotalMinutes:0.0} min, reminders={reminders}, captcha requests={captchaRequests}");
        }
        finally
        {
            page.Request -= onRequest;
            try { await _host.RestoreAfterChallengeAsync(page, wasHidden); } catch (Exception) { /* best effort */ }
        }

        var cooldown = Math.Max(0, CooldownSeconds());
        if (cooldown > 0)
        {
            Notice?.Invoke(T($"✓ Đã giải xong xác minh. Nghỉ {cooldown} giây rồi chạy tiếp...", $"✓ Verification solved. Pausing {cooldown} seconds, then carrying on..."));
            await Task.Delay(TimeSpan.FromSeconds(cooldown), ct);
        }
    }

    private async Task SnapshotAsync(IPage page)
    {
        try
        {
            var dir = AppPaths.DebugDir;
            var baseName = Path.Combine(dir, $"challenge_{_platform.ToLowerInvariant()}_{DateTime.Now:yyyyMMdd_HHmmss}");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = baseName + ".png", Timeout = 5000 });
            await File.WriteAllTextAsync(baseName + ".html", await page.ContentAsync());

            foreach (var old in new DirectoryInfo(dir).GetFiles("challenge_*").OrderByDescending(f => f.Name).Skip(40))
            {
                try { old.Delete(); } catch (IOException) { }
            }

            Log("INFO", $"snapshot saved: {Path.GetFileName(baseName)}.png / .html (debug folder)");
        }
        catch (Exception ex)
        {
            Log("WARN", "snapshot failed: " + ex.Message);
        }
    }

    private void Log(string level, string text) => AppLog.Write(level, $"[{_platform}] {text}", LogCategory.Challenges);
}