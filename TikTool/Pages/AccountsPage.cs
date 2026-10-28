using Guna.UI2.WinForms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TikTool.Browser;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

/// <summary>Where the user signs in. TikTok: the sign-in page runs in a WebView2 inside this page; once TikTok sets the "sessionid" cookie the tool
/// copies the TikTok cookies, verifies them, and stores them in its own DPAPI-protected cookie file, so later runs need no sign-in until the user
/// switches account (Sign out). Nothing is read from the user's own Chrome/Edge. Without the WebView2 runtime the old separate-Chrome sign-in is the fallback.</summary>
internal sealed class AccountsPage : UserControl
{
    private const string TikTokLoginUrl = "https://www.tiktok.com/login?lang=en";

    private readonly CookieService _cookieService;
    private readonly PlaywrightService _playwright;

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _message = new();
    private readonly Label _status = new();
    private readonly Guna2Button _btnSignOut = new();
    private readonly Guna2Button _btnChrome = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };

    private CoreWebView2Environment? _env;
    private bool _started;
    private bool _busy;
    private bool _signedIn;

    public event Action? CookieStatusChanged;

    public AccountsPage(CookieService cookieService, PlaywrightService playwright)
    {
        _cookieService = cookieService;
        _playwright = playwright;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();

        _poll.Tick += async (_, _) => await PollAsync();
        VisibleChanged += async (_, _) =>
        {
            if (Visible && !_started)
            {
                _started = true;
                await StartAsync();
            }
        };
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
            Text = T("Đăng nhập TikTok một lần, tool nhớ cho các lần sau.", "Sign in to TikTok once; the tool remembers it for next time."),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(25, 44),
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Background };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        _status.Font = Theme.FontMedium;
        _status.ForeColor = Theme.TextSecondary;
        _status.AutoSize = false;
        _status.Location = new Point(24, 15);
        _status.Height = 22;

        StyleButton(_btnSignOut, T("Đăng xuất / Đổi tài khoản", "Sign out / Switch account"), 210);
        _btnSignOut.Visible = false;
        _btnSignOut.Click += async (_, _) => await SignOutAsync();

        StyleButton(_btnChrome, T("Đăng nhập bằng Chrome", "Sign in with Chrome"), 210);
        _btnChrome.Visible = false;
        _btnChrome.Click += async (_, _) => await SignInWithChromeAsync();

        var bar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Background };
        bar.Controls.Add(_status);
        bar.Controls.Add(_btnSignOut);
        bar.Controls.Add(_btnChrome);
        bar.Resize += (_, _) =>
        {
            _btnSignOut.Location = new Point(bar.Width - _btnSignOut.Width - 24, 11);
            _btnChrome.Location = new Point(bar.Width - _btnChrome.Width - 24, 11);
            _status.Width = Math.Max(120, bar.Width - 24 - 230);
        };

        _message.Dock = DockStyle.Fill;
        _message.Font = new Font("Segoe UI", 11f);
        _message.ForeColor = Theme.TextSecondary;
        _message.TextAlign = ContentAlignment.MiddleCenter;
        _message.Visible = false;

        // Dock=Fill first, then the Top controls (the last one added sits at the very top).
        Controls.Add(_web);
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

    // ------------------------------------------------------------------------------------------------ flow

    private async Task StartAsync()
    {
        try
        {
            var saved = await _cookieService.LoadCookieAsync();
            if (saved is not null && !string.IsNullOrEmpty(saved.Username) && !(saved.ExpiryDate is DateTime d && d < DateTime.Now))
            {
                ShowSignedIn(saved);
                return;
            }

            await StartSignInAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "AccountsPage start: " + ex);
            _status.Text = T("Không mở được trang đăng nhập: ", "Could not open the sign-in page: ") + ex.Message;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        _env ??= await CoreWebView2Environment.CreateAsync(null, AppPaths.TikTokWebViewDir);
        if (_web.CoreWebView2 is null)
        {
            await _web.EnsureCoreWebView2Async(_env);
        }
    }

    private async Task StartSignInAsync()
    {
        _signedIn = false;
        _message.Visible = false;
        _btnSignOut.Visible = false;

        if (!BrowserHosts.EmbeddedAvailable())
        {
            // No WebView2 runtime: offer the separate-Chrome sign-in instead.
            _web.Visible = false;
            _message.Text = T("Máy chưa có WebView2 Runtime, nên không đăng nhập ngay trong cửa sổ này được.\nBạn có thể đăng nhập bằng Chrome (mở cửa sổ riêng), hoặc cài WebView2 Runtime từ trang của Microsoft.",
                              "The WebView2 runtime is not installed, so the sign-in cannot run inside this window.\nYou can sign in with Chrome (opens a separate window) or install the WebView2 runtime from Microsoft.");
            _message.Visible = true;
            _btnChrome.Visible = true;
            _status.Text = T("TikTok: chưa đăng nhập", "TikTok: not signed in");
            return;
        }

        _btnChrome.Visible = false;
        _web.Visible = true;
        await EnsureWebViewAsync();
        _status.Text = T("TikTok: chưa đăng nhập — đăng nhập bên dưới, tool tự nhận khi xong.", "TikTok: not signed in — sign in below, the tool notices when you are done.");
        _web.CoreWebView2.Navigate(TikTokLoginUrl);
        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (_busy || _signedIn || _web.CoreWebView2 is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.tiktok.com/");
            var session = cookies.FirstOrDefault(c => c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
            if (session is null)
            {
                return;
            }

            _poll.Stop();
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
            await AdoptAsync(cookie);
        }
        catch (Exception ex)
        {
            AppLog.Write("WARN", "AccountsPage poll: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Verifies the cookie against TikTok (to learn the username), stores it, and switches to the signed-in view.</summary>
    private async Task AdoptAsync(TikTokCookie cookie)
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
        }

        await _cookieService.SaveCookieAsync(cookie);
        CookieStatusChanged?.Invoke();

        if (result is { IsValid: true })
        {
            ShowSignedIn(cookie);
        }
        else
        {
            // Signed in but TikTok did not confirm it (offline, rate limit...): keep the page on the sign-out button so the user can retry.
            _signedIn = true;
            _web.Visible = false;
            _message.Text = T("Đã đăng nhập nhưng chưa xác thực được với TikTok.\nKiểm tra kết nối mạng rồi bấm \"Đăng xuất / Đổi tài khoản\" và đăng nhập lại.",
                              "Signed in, but TikTok did not confirm it.\nCheck your connection, then press \"Sign out / Switch account\" and sign in again.");
            _message.Visible = true;
            _status.Text = T("TikTok: chưa xác thực — ", "TikTok: not verified — ") + (result?.Error ?? "");
            _btnSignOut.Visible = true;
        }
    }

    private void ShowSignedIn(TikTokCookie cookie)
    {
        _signedIn = true;
        _poll.Stop();
        _web.Visible = false;
        _btnChrome.Visible = false;
        _btnSignOut.Visible = true;

        var days = cookie.DaysLeft;
        var daysText = days is int n ? T($" · còn {n} ngày", $" · {n} days left") : "";
        _status.Text = $"TikTok: ✓ @{cookie.Username}{daysText}";
        _message.Text = T($"Đã đăng nhập TikTok là @{cookie.Username}.\n\nBây giờ bạn dùng thẳng tab Bỏ theo dõi. Muốn dùng tài khoản khác thì bấm \"Đăng xuất / Đổi tài khoản\".",
                          $"Signed in to TikTok as @{cookie.Username}.\n\nYou can go straight to the Unfollow tab. To use another account, press \"Sign out / Switch account\".");
        _message.Visible = true;
    }

    private async Task SignOutAsync()
    {
        var ask = MessageBox.Show(this,
            T("Đăng xuất TikTok khỏi tool? Tool sẽ quên phiên đã lưu. Tài khoản TikTok của bạn trên Chrome hay điện thoại không bị ảnh hưởng.",
              "Sign out of TikTok in this tool? The tool forgets the saved session. Your TikTok account on Chrome or your phone is not affected."),
            T("Social Toolkit — Đăng xuất TikTok", "Social Toolkit — Sign out of TikTok"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (ask != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _poll.Stop();
            _cookieService.DeleteCookie();
            if (BrowserHosts.EmbeddedAvailable())
            {
                await EnsureWebViewAsync();
                _web.CoreWebView2.CookieManager.DeleteAllCookies();
            }

            CookieStatusChanged?.Invoke();
            await StartSignInAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "AccountsPage sign out: " + ex);
            _status.Text = T("Không đăng xuất được: ", "Could not sign out: ") + ex.Message;
        }
    }

    private async Task SignInWithChromeAsync()
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

            await AdoptAsync(cookie);
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
}