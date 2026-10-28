using Guna.UI2.WinForms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TikTool.Browser;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

/// <summary>Where the user signs in, one view per platform (switch with the TikTok | YouTube buttons). The sign-in pages run in WebView2 controls inside
/// this page, created from the same <see cref="WebViewSession"/> as the window that later does the work. Both views keep running while the other
/// one is shown, so a half-finished sign-in is not lost.
/// TikTok: once "sessionid" appears the tool copies the TikTok cookies, verifies them, and stores them in its own DPAPI-protected cookie file (TikTok does not
/// keep the session across app restarts). YouTube: Google keeps the session in the profile folder; the tool only notices the sign-in.
/// Nothing is read from the user's own Chrome/Edge. Without the WebView2 runtime the old separate-Chrome sign-in is the fallback for TikTok.</summary>
internal sealed class AccountsPage : UserControl
{
    private const string TikTokLoginUrl = "https://www.tiktok.com/login?lang=en";
    private const string YouTubeSignInUrl = "https://accounts.google.com/ServiceLogin?service=youtube&hl=en&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private enum Platform { TikTok, YouTube }

    private readonly CookieService _cookieService;
    private readonly PlaywrightService _playwright;
    private readonly SettingsService _settings;

    // Both controls exist from the start (no browser process until they are initialised) so that Dock=Fill is added before the Top bars.
    private readonly WebView2 _webTikTok = new() { Dock = DockStyle.Fill };
    private readonly WebView2 _webYouTube = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _message = new();
    private readonly Label _status = new();
    private readonly Guna2Button _tabTikTok = new();
    private readonly Guna2Button _tabYouTube = new();
    private readonly Guna2Button _btnSignOut = new();
    private readonly Guna2Button _btnChrome = new();
    private readonly System.Windows.Forms.Timer _pollTikTok = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _pollYouTube = new() { Interval = 2000 };

    private Platform _current = Platform.TikTok;
    private bool _started;
    private bool _tikTokStarted, _youTubeStarted;
    private bool _tikTokBusy, _youTubeBusy;
    private bool _tikTokSignedIn, _youTubeSignedIn;
    private TikTokCookie? _tikTokCookie;
    private string _tikTokNote = "";

    public event Action? CookieStatusChanged;

    public AccountsPage(CookieService cookieService, PlaywrightService playwright, SettingsService settings)
    {
        _cookieService = cookieService;
        _playwright = playwright;
        _settings = settings;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();

        _pollTikTok.Tick += async (_, _) => await PollTikTokAsync();
        _pollYouTube.Tick += async (_, _) => await PollYouTubeAsync();
        VisibleChanged += async (_, _) =>
        {
            if (Visible && !_started)
            {
                _started = true;
                await StartCurrentAsync();
            }
        };
    }

    /// <summary>Opens the given platform's sign-in view ("tiktok" or "youtube"). Called by the feature pages when the user has to sign in first.</summary>
    public async void ShowPlatform(string platform)
    {
        _current = platform.Equals("youtube", StringComparison.OrdinalIgnoreCase) ? Platform.YouTube : Platform.TikTok;
        _started = true;
        await StartCurrentAsync();
    }

    // ------------------------------------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var title = new Label
        {
            Text = T("Tài khoản", "Accounts"),
            Font = Theme.FontTitle,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(24, 14),
        };
        var subtitle = new Label
        {
            Text = T("Đăng nhập một lần, tool nhớ cho các lần sau.", "Sign in once; the tool remembers it for next time."),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(25, 44),
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Background };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        StyleButton(_tabTikTok, "TikTok", 110);
        StyleButton(_tabYouTube, "YouTube", 110);
        _tabTikTok.Location = new Point(24, 11);
        _tabYouTube.Location = new Point(24 + 110 + 8, 11);
        _tabTikTok.Click += async (_, _) => await SwitchToAsync(Platform.TikTok);
        _tabYouTube.Click += async (_, _) => await SwitchToAsync(Platform.YouTube);

        _status.Font = Theme.FontMedium;
        _status.ForeColor = Theme.TextSecondary;
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Location = new Point(24 + 110 + 8 + 110 + 20, 15);
        _status.Height = 22;

        StyleButton(_btnSignOut, T("Đăng xuất / Đổi tài khoản", "Sign out / Switch account"), 210);
        _btnSignOut.Visible = false;
        _btnSignOut.Click += async (_, _) => await SignOutAsync();

        StyleButton(_btnChrome, T("Đăng nhập bằng Chrome", "Sign in with Chrome"), 210);
        _btnChrome.Visible = false;
        _btnChrome.Click += async (_, _) => await SignInTikTokWithChromeAsync();

        var bar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Background };
        bar.Controls.Add(_tabTikTok);
        bar.Controls.Add(_tabYouTube);
        bar.Controls.Add(_status);
        bar.Controls.Add(_btnSignOut);
        bar.Controls.Add(_btnChrome);
        bar.Resize += (_, _) =>
        {
            _btnSignOut.Location = new Point(bar.Width - _btnSignOut.Width - 24, 11);
            _btnChrome.Location = new Point(bar.Width - _btnChrome.Width - 24, 11);
            _status.Width = Math.Max(120, bar.Width - _status.Left - 230);
        };

        _message.Dock = DockStyle.Fill;
        _message.Font = new Font("Segoe UI", 11f);
        _message.ForeColor = Theme.TextSecondary;
        _message.TextAlign = ContentAlignment.MiddleCenter;
        _message.Visible = false;

        // Dock=Fill first, then the Top controls (the last one added sits at the very top).
        Controls.Add(_webTikTok);
        Controls.Add(_webYouTube);
        Controls.Add(_message);
        Controls.Add(bar);
        Controls.Add(header);
    }

    private static void StyleButton(Guna2Button b, string text, int width)
    {
        b.Text = text;
        b.Font = Theme.FontMedium;
        b.Size = new Size(width, 30);
        b.FillColor = ColorTranslator.FromHtml("#2a2a2a");
        b.ForeColor = Theme.TextSecondary;
        b.BorderRadius = 7;
        b.Cursor = Cursors.Hand;
    }

    private bool UseEmbedded => BrowserHosts.UseEmbedded(_settings.Current);

    /// <summary>Shows what the current platform needs: the sign-in browser, or a message once signed in, plus status and buttons.</summary>
    private void UpdateView()
    {
        _tabTikTok.FillColor = _current == Platform.TikTok ? Theme.Accent : ColorTranslator.FromHtml("#2a2a2a");
        _tabTikTok.ForeColor = _current == Platform.TikTok ? Color.White : Theme.TextSecondary;
        _tabYouTube.FillColor = _current == Platform.YouTube ? Theme.Accent : ColorTranslator.FromHtml("#2a2a2a");
        _tabYouTube.ForeColor = _current == Platform.YouTube ? Color.White : Theme.TextSecondary;

        _btnChrome.Visible = false;
        _message.Visible = false;
        _webTikTok.Visible = false;
        _webYouTube.Visible = false;

        if (_current == Platform.TikTok)
        {
            if (_tikTokSignedIn && _tikTokCookie is { } c)
            {
                var days = c.DaysLeft;
                var daysText = days is int n ? T($" · còn {n} ngày", $" · {n} days left") : "";
                _status.Text = $"TikTok: ✓ @{c.Username}{daysText}";
                ShowMessage(T($"Đã đăng nhập TikTok là @{c.Username}.\n\nBây giờ bạn dùng thẳng tab Bỏ theo dõi. Muốn dùng tài khoản khác thì bấm \"Đăng xuất / Đổi tài khoản\".",
                              $"Signed in to TikTok as @{c.Username}.\n\nYou can go straight to the Unfollow tab. To use another account, press \"Sign out / Switch account\"."));
                _btnSignOut.Visible = true;
            }
            else if (_tikTokSignedIn)
            {
                _status.Text = T("TikTok: chưa xác thực được", "TikTok: not verified") + (_tikTokNote.Length > 0 ? " — " + _tikTokNote : "");
                ShowMessage(T("Đã đăng nhập nhưng chưa xác thực được với TikTok.\nKiểm tra kết nối mạng rồi bấm \"Đăng xuất / Đổi tài khoản\" và đăng nhập lại.",
                              "Signed in, but TikTok did not confirm it.\nCheck your connection, then press \"Sign out / Switch account\" and sign in again."));
                _btnSignOut.Visible = true;
            }
            else if (!BrowserHosts.EmbeddedAvailable())
            {
                _status.Text = T("TikTok: chưa đăng nhập", "TikTok: not signed in");
                ShowMessage(T("Máy chưa có WebView2 Runtime, nên không đăng nhập ngay trong cửa sổ này được.\nBạn có thể đăng nhập bằng Chrome (mở cửa sổ riêng), hoặc cài WebView2 Runtime từ trang của Microsoft.",
                              "The WebView2 runtime is not installed, so the sign-in cannot run inside this window.\nYou can sign in with Chrome (opens a separate window) or install the WebView2 runtime from Microsoft."));
                _btnChrome.Visible = true;
                _btnSignOut.Visible = false;
            }
            else
            {
                _status.Text = T("TikTok: chưa đăng nhập — đăng nhập bên dưới", "TikTok: not signed in — sign in below");
                _webTikTok.Visible = true;
                _btnSignOut.Visible = false;
            }
        }
        else
        {
            if (!UseEmbedded)
            {
                _status.Text = T("YouTube: dùng Chrome riêng", "YouTube: using a separate Chrome");
                ShowMessage(T("Đang tắt \"Dùng trình duyệt tích hợp\" (Cài đặt), nên YouTube dùng cửa sổ Chrome riêng.\nĐăng nhập Google trong cửa sổ Chrome đó khi bấm Kết nối ở tab YouTube.",
                              "\"Use the built-in browser\" is off (Settings), so YouTube uses a separate Chrome window.\nSign in to Google in that Chrome window when you press Connect on the YouTube tab."));
                _btnSignOut.Visible = false;
            }
            else if (_youTubeSignedIn)
            {
                _status.Text = T("YouTube: ✓ đã đăng nhập Google", "YouTube: ✓ signed in to Google");
                ShowMessage(T("Đã đăng nhập Google/YouTube.\n\nBây giờ bạn dùng thẳng tab YouTube Unsubscribe. Muốn dùng tài khoản khác thì bấm \"Đăng xuất / Đổi tài khoản\".",
                              "Signed in to Google/YouTube.\n\nYou can go straight to the YouTube Unsubscribe tab. To use another account, press \"Sign out / Switch account\"."));
                _btnSignOut.Visible = true;
            }
            else
            {
                _status.Text = T("YouTube: chưa đăng nhập — đăng nhập bên dưới", "YouTube: not signed in — sign in below");
                _webYouTube.Visible = true;
                _btnSignOut.Visible = false;
            }
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visible = true;
    }

    private async Task SwitchToAsync(Platform platform)
    {
        _current = platform;
        await StartCurrentAsync();
    }

    private async Task StartCurrentAsync()
    {
        try
        {
            if (_current == Platform.TikTok)
            {
                await StartTikTokAsync();
            }
            else
            {
                await StartYouTubeAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "AccountsPage start: " + ex);
            _status.Text = T("Không mở được trang đăng nhập: ", "Could not open the sign-in page: ") + ex.Message;
        }

        UpdateView();
    }

    // ------------------------------------------------------------------------------------------------ TikTok

    private async Task EnsureTikTokWebViewAsync()
    {
        if (_webTikTok.CoreWebView2 is null)
        {
            _webTikTok.Visible = true;
            await _webTikTok.EnsureCoreWebView2Async(await WebViewSessions.For(AppPaths.TikTokWebViewDir).GetEnvironmentAsync());
        }
    }

    private async Task StartTikTokAsync()
    {
        if (_tikTokStarted)
        {
            return;
        }

        _tikTokStarted = true;
        var saved = await _cookieService.LoadCookieAsync();
        if (saved is not null && !string.IsNullOrEmpty(saved.Username) && !(saved.ExpiryDate is DateTime d && d < DateTime.Now))
        {
            _tikTokCookie = saved;
            _tikTokSignedIn = true;
            return;
        }

        await BeginTikTokSignInAsync();
    }

    private async Task BeginTikTokSignInAsync()
    {
        _tikTokSignedIn = false;
        _tikTokCookie = null;
        if (!BrowserHosts.EmbeddedAvailable())
        {
            return;
        }

        await EnsureTikTokWebViewAsync();
        _webTikTok.CoreWebView2.Navigate(TikTokLoginUrl);
        _pollTikTok.Start();
    }

    private async Task PollTikTokAsync()
    {
        if (_tikTokBusy || _tikTokSignedIn || _webTikTok.CoreWebView2 is null)
        {
            return;
        }

        _tikTokBusy = true;
        try
        {
            var cookies = await _webTikTok.CoreWebView2.CookieManager.GetCookiesAsync("https://www.tiktok.com/");
            var session = cookies.FirstOrDefault(c => c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
            if (session is null)
            {
                return;
            }

            _pollTikTok.Stop();
            _status.Text = T("Đang xác thực...", "Verifying...");
            var cookie = new TikTokCookie
            {
                SessionId = session.Value,
                MsToken = cookies.FirstOrDefault(c => c.Name == "msToken")?.Value ?? "",
                TtWebId = cookies.FirstOrDefault(c => c.Name is "tt_webid_v2" or "tt_webid")?.Value ?? "",
                RawCookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")),
                CapturedAt = DateTime.Now,
                Source = "login",
                ExpiryDate = session.IsSession || session.Expires.Year < 2000 ? null : session.Expires.ToLocalTime(),
            };
            await AdoptTikTokAsync(cookie);
        }
        catch (Exception ex)
        {
            AppLog.Write("WARN", "AccountsPage TikTok poll: " + ex.Message);
        }
        finally
        {
            _tikTokBusy = false;
        }
    }

    /// <summary>Verifies the cookie against TikTok (to learn the username), stores it, and switches to the signed-in view.</summary>
    private async Task AdoptTikTokAsync(TikTokCookie cookie)
    {
        CookieVerifyResult? result = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            result = await _cookieService.VerifyCookieAsync(cookie);
            if (result.IsValid)
            {
                break;
            }

            await Task.Delay(1500);
        }

        if (result is { IsValid: true })
        {
            cookie.Username = result.Username;
            _tikTokCookie = cookie;
        }
        else
        {
            _tikTokNote = result?.Error ?? "";
        }

        await _cookieService.SaveCookieAsync(cookie);
        _tikTokSignedIn = true;
        CookieStatusChanged?.Invoke();
        UpdateView();
    }

    private async Task SignInTikTokWithChromeAsync()
    {
        _btnChrome.Enabled = false;
        _status.Text = T("Đang mở Chrome — đăng nhập TikTok trong cửa sổ đó (tối đa 5 phút)...", "Opening Chrome — sign in to TikTok in that window (up to 5 minutes)...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var cookie = await _playwright.LoginInteractivelyAsync(TimeSpan.FromMinutes(5), cts.Token);
            if (cookie is null)
            {
                _status.Text = T("TikTok: chưa đăng nhập (cửa sổ đã đóng hoặc hết thời gian).", "TikTok: not signed in (the window was closed or time ran out).");
                return;
            }

            await AdoptTikTokAsync(cookie);
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "AccountsPage chrome sign-in: " + ex);
            _status.Text = ex.Message;
        }
        finally
        {
            _btnChrome.Enabled = true;
        }
    }

    // ------------------------------------------------------------------------------------------------ YouTube

    private async Task EnsureYouTubeWebViewAsync()
    {
        if (_webYouTube.CoreWebView2 is null)
        {
            _webYouTube.Visible = true;
            await _webYouTube.EnsureCoreWebView2Async(await WebViewSessions.For(AppPaths.YouTubeWebViewDir).GetEnvironmentAsync());
        }
    }

    private async Task<bool> HasYouTubeSessionAsync()
    {
        var cookies = await _webYouTube.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com/");
        return cookies.Any(c => c.Name.Equals("SAPISID", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
    }

    private async Task StartYouTubeAsync()
    {
        if (_youTubeStarted || !UseEmbedded)
        {
            return;
        }

        _youTubeStarted = true;
        await EnsureYouTubeWebViewAsync();
        if (await HasYouTubeSessionAsync())
        {
            _youTubeSignedIn = true;
            GoogleSession.MarkSignedIn();
            return;
        }

        _youTubeSignedIn = false;
        _webYouTube.CoreWebView2.Navigate(YouTubeSignInUrl);
        _pollYouTube.Start();
    }

    private async Task PollYouTubeAsync()
    {
        if (_youTubeBusy || _youTubeSignedIn || _webYouTube.CoreWebView2 is null)
        {
            return;
        }

        _youTubeBusy = true;
        try
        {
            if (await HasYouTubeSessionAsync())
            {
                _pollYouTube.Stop();
                _youTubeSignedIn = true;
                GoogleSession.MarkSignedIn();
                UpdateView();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("WARN", "AccountsPage YouTube poll: " + ex.Message);
        }
        finally
        {
            _youTubeBusy = false;
        }
    }

    // ------------------------------------------------------------------------------------------------ sign out

    private async Task SignOutAsync()
    {
        var tikTok = _current == Platform.TikTok;
        var ask = MessageBox.Show(this,
            tikTok
                ? T("Đăng xuất TikTok khỏi tool? Tool sẽ quên phiên đã lưu. Tài khoản TikTok của bạn trên Chrome hay điện thoại không bị ảnh hưởng.",
                    "Sign out of TikTok in this tool? The tool forgets the saved session. Your TikTok account on Chrome or your phone is not affected.")
                : T("Đăng xuất Google/YouTube khỏi tool? Tool xóa phiên đăng nhập và bộ nhớ đệm ngày đăng. File sao lưu CSV và danh sách kênh được bảo vệ được giữ lại. Tài khoản Google của bạn ở nơi khác không bị ảnh hưởng.",
                    "Sign out of Google/YouTube in this tool? The tool removes the session and the upload-date cache. CSV backups and the protected-channel list are kept. Your Google account elsewhere is not affected."),
            tikTok ? T("Social Toolkit — Đăng xuất TikTok", "Social Toolkit — Sign out of TikTok") : T("Social Toolkit — Đăng xuất YouTube", "Social Toolkit — Sign out of YouTube"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (tikTok)
            {
                _pollTikTok.Stop();
                _cookieService.DeleteCookie();
                if (BrowserHosts.EmbeddedAvailable())
                {
                    await EnsureTikTokWebViewAsync();
                    _webTikTok.CoreWebView2.CookieManager.DeleteAllCookies();
                }

                _tikTokStarted = true;
                await BeginTikTokSignInAsync();
                CookieStatusChanged?.Invoke();
            }
            else
            {
                _pollYouTube.Stop();
                await EnsureYouTubeWebViewAsync();
                _webYouTube.CoreWebView2.CookieManager.DeleteAllCookies();
                GoogleSession.MarkSignedOut();
                try { if (File.Exists(AppPaths.YouTubeAgesFile)) File.Delete(AppPaths.YouTubeAgesFile); } catch (IOException) { }
                try { if (Directory.Exists(AppPaths.YouTubeProfileDir)) Directory.Delete(AppPaths.YouTubeProfileDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }

                _youTubeSignedIn = false;
                _youTubeStarted = true;
                _webYouTube.CoreWebView2.Navigate(YouTubeSignInUrl);
                _pollYouTube.Start();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "AccountsPage sign out: " + ex);
            _status.Text = T("Không đăng xuất được: ", "Could not sign out: ") + ex.Message;
            return;
        }

        UpdateView();
    }
}