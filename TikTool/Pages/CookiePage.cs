using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

internal sealed class CookiePage : UserControl
{
    private const int ContentWidth = 700;

    private readonly CookieService _cookieService;
    private readonly PlaywrightService _playwright;
    private TikTokCookie? _currentCookie;
    private bool _loginInProgress;

    private Label _headerBadge = null!;
    private FlowLayoutPanel _flow = null!;
    private readonly List<Control> _staticFlowControls = new();
    private Panel? _statusPanel;
    private LogPanel _log = null!;
    private Label _actionInfo = null!;
    private Guna2Button _btnReverify = null!;
    private Guna2Button _btnSave = null!;

    private readonly List<CookieMethodCard> _methodCards = new();

    private CookieBrowser _selectedBrowser = CookieBrowser.Chrome;
    private Guna2Panel _chromeCard = null!;
    private Guna2Panel _edgeCard = null!;
    private Guna2Button _btnReadBrowser = null!;
    private TextBox _pasteBox = null!;

    public event Action? CookieStatusChanged;

    public CookiePage(CookieService cookieService, PlaywrightService playwright)
    {
        _cookieService = cookieService;
        _playwright = playwright;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();

        // CookiePage.Load fires while MainForm is still constructing (all 5 pages are
        // pre-created up front, before the form has a real size/position) — deferring via
        // BeginInvoke runs the initial cookie load after the form's first real layout pass.
        Load += (_, _) => BeginInvoke(new Action(async () =>
        {
            try
            {
                await LoadSavedCookieAsync();
            }
            catch (Exception ex)
            {
                _log.Log(T($"[Cookie] Lỗi khi nạp cookie đã lưu: {ex.Message}", $"[Cookie] Error loading saved cookie: {ex.Message}"), LogLevel.Error);
            }
        }));
    }

    private void BuildUi()
    {
        // WinForms resolves Dock in reverse Z-order (last-added edge-docked control claims
        // space first); Dock=Fill must be added BEFORE the Top/Bottom edges or it gets
        // resolved before their space is subtracted and ends up overlapping them.
        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
        Controls.Add(BuildActionBar());
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Theme.Background,
            Padding = new Padding(20, 14, 20, 0),
        };

        var title = new Label
        {
            Text = T("Quản lý Cookie", "Cookie Manager"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        };
        var subtitle = new Label
        {
            Text = T("Xác thực tài khoản TikTok để dùng chức năng Bỏ theo dõi",
                     "Authenticate your TikTok account to use the Unfollow feature"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        };

        _headerBadge = new Label
        {
            Text = T("Chưa có cookie", "No cookie"),
            Font = Theme.FontLabel,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
        };
        var badgeHost = new Guna2Panel
        {
            AutoSize = true,
            BorderRadius = 20,
            FillColor = Color.FromArgb(40, 40, 40),
            Location = new Point(560, 16),
        };
        badgeHost.Controls.Add(_headerBadge);

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(badgeHost);

        var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };
        header.Controls.Add(sep);
        return header;
    }

    private Control BuildBody()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

        _log = new LogPanel { Dock = DockStyle.Bottom, Height = 100 };
        var logHost = new Panel { Dock = DockStyle.Bottom, Height = 116, Padding = new Padding(20, 0, 20, 16), BackColor = Theme.Background };
        logHost.Controls.Add(_log);

        var scroll = BuildScrollArea();

        // Same Fill-before-edges rule applies one level down: scroll (Fill) must be added
        // before logHost (Bottom).
        body.Controls.Add(scroll);
        body.Controls.Add(logHost);
        return body;
    }

    private Control BuildScrollArea()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(20, 16, 20, 0),
        };

        _flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            BackColor = Theme.Background,
            Location = new Point(20, 16),
        };

        var sectionLabel = new Label
        {
            Text = T("CHỌN PHƯƠNG THỨC LẤY COOKIE", "CHOOSE A COOKIE METHOD"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _staticFlowControls.Add(sectionLabel);
        _staticFlowControls.Add(BuildMethodCardChrome());
        _staticFlowControls.Add(BuildMethodCardPaste());
        _staticFlowControls.Add(BuildMethodCardLogin());

        RelayoutFlow();

        scroll.Controls.Add(_flow);
        return scroll;
    }

    /// <summary>Rebuilds the flow's child order from scratch (status card, if any, followed by
    /// the static controls). Proven reliable pattern — see project history for why a targeted
    /// insert/remove was not.</summary>
    private void RelayoutFlow()
    {
        _flow.SuspendLayout();
        _flow.Controls.Clear();
        if (_statusPanel is not null)
        {
            _flow.Controls.Add(_statusPanel);
        }

        foreach (var control in _staticFlowControls)
        {
            _flow.Controls.Add(control);
        }

        _flow.ResumeLayout(true);

        // Re-apply card border states after FlowLayoutPanel re-parent clears visual state
        foreach (var card in _methodCards)
        {
            card.BorderColor = card.IsExpanded ? Theme.Accent : Theme.Border;
        }
    }

    private Panel BuildStatusPanel()
    {
        var panel = new Guna2Panel
        {
            Width = ContentWidth,
            Height = 64,
            BorderRadius = 8,
            FillColor = Theme.Panel,
            BorderColor = Theme.Border,
            BorderThickness = 1,
            Margin = new Padding(0, 0, 0, 10),
        };
        return panel;
    }

    private void RenderStatusPanel()
    {
        _statusPanel?.Dispose();
        _statusPanel = null;

        if (_currentCookie is null)
        {
            RelayoutFlow();
            return;
        }

        var cookie = _currentCookie;
        var statusPanel = BuildStatusPanel();
        _statusPanel = statusPanel;

        var avatar = new Guna2Panel
        {
            Size = new Size(40, 40),
            Location = new Point(12, 12),
            BorderRadius = 20,
            FillColor = Theme.Accent,
        };
        avatar.Controls.Add(new Label { Text = "😊", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, BackColor = Theme.Accent });

        var nameLabel = new Label
        {
            Text = cookie.Username is null ? T("(chưa xác thực)", "(not verified)") : $"@{cookie.Username}",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Size = new Size(260, 20),
            Location = new Point(64, 12),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.Panel,
        };
        var metaLabel = new Label
        {
            Text = T($"Nguồn: {cookie.Source} · Lấy lúc {cookie.CapturedAt:HH:mm dd/MM}", $"Source: {cookie.Source} · Captured {cookie.CapturedAt:HH:mm dd/MM}"),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(300, 18),
            Location = new Point(64, 34),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.Panel,
        };

        var validLabel = new Label
        {
            Text = cookie.Username is not null ? T("✓ Cookie hợp lệ", "✓ Cookie valid") : T("○ Chưa kiểm tra", "○ Not verified"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = cookie.Username is not null ? Theme.Green : Theme.TextMuted,
            AutoSize = false,
            Size = new Size(224, 18),
            Location = new Point(ContentWidth - 304, 10),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };
        var daysLabel = new Label
        {
            Text = cookie.DaysLeft is int d ? T($"Hết hạn: {d} ngày", $"Expires in {d} days") : "",
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(224, 16),
            Location = new Point(ContentWidth - 304, 30),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };

        var btnDelete = new Guna2Button
        {
            Text = T("Xóa", "Delete"),
            Font = Theme.FontMedium,
            Size = new Size(60, 28),
            Location = new Point(ContentWidth - 76, 18),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 6,
            Cursor = Cursors.Hand,
        };
        btnDelete.Click += (_, _) => DeleteCookie();

        statusPanel.Controls.Add(avatar);
        statusPanel.Controls.Add(nameLabel);
        statusPanel.Controls.Add(metaLabel);
        statusPanel.Controls.Add(validLabel);
        statusPanel.Controls.Add(daysLabel);
        statusPanel.Controls.Add(btnDelete);

        RelayoutFlow();
    }

    private void SelectCard(CookieMethodCard selected)
    {
        foreach (var card in _methodCards.Where(c => c != selected))
        {
            card.Collapse();
        }
    }

    private CookieMethodCard BuildMethodCardChrome()
    {
        var card = new CookieMethodCard(ContentWidth, "🌐", T("Tự đọc từ trình duyệt", "Auto-read from browser"), T("Đọc cookie TikTok từ Chrome/Edge đang login sẵn", "Read TikTok cookie from logged-in Chrome/Edge"), T("⭐ Khuyên dùng", "⭐ Recommended"), Theme.Green);
        card.OnExpand += () => SelectCard(card);
        _methodCards.Add(card);

        var browserRow = new Panel { Width = ContentWidth - 32, Height = 46, Margin = new Padding(0, 0, 0, 10) };
        _chromeCard = BuildBrowserChoice("🔵", "Chrome", 0, CookieBrowser.Chrome);
        _edgeCard = BuildBrowserChoice("🔷", "Edge", 158, CookieBrowser.Edge);
        browserRow.Controls.Add(_chromeCard);
        browserRow.Controls.Add(_edgeCard);

        _btnReadBrowser = new Guna2Button
        {
            Text = T("🔍  Đọc Cookie từ Chrome", "🔍  Read Cookie from Chrome"),
            Font = Theme.FontMedium,
            Width = ContentWidth - 32,
            Height = 34,
            Margin = new Padding(0),
            FillColor = Color.FromArgb(38, Theme.Green.R, Theme.Green.G, Theme.Green.B),
            ForeColor = Theme.Green,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnReadBrowser.Click += async (_, _) => await ReadFromBrowserAsync();

        card.SetContent(browserRow, _btnReadBrowser);
        card.Expand();
        return card;
    }

    private Guna2Panel BuildBrowserChoice(string icon, string name, int x, CookieBrowser browser)
    {
        var installed = CookieService.IsBrowserInstalled(browser);
        var boxBg = ColorTranslator.FromHtml("#141414");
        var box = new Guna2Panel
        {
            Size = new Size(150, 46),
            Location = new Point(x, 0),
            BorderRadius = 8,
            FillColor = boxBg,
            BorderColor = browser == _selectedBrowser ? Theme.Accent : Theme.Border,
            BorderThickness = 1,
            Cursor = Cursors.Hand,
        };
        var lblIcon = new Label { Text = icon, Font = new Font("Segoe UI", 13f), Location = new Point(10, 10), Size = new Size(26, 26), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = boxBg, Cursor = Cursors.Hand };
        var lblName = new Label { Text = name, Font = Theme.FontMedium, ForeColor = Theme.TextSecondary, Location = new Point(40, 6), Size = new Size(100, 18), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = boxBg, Cursor = Cursors.Hand };
        var lblStatus = new Label
        {
            Text = installed ? T("Tìm thấy", "Found") : T("Không tìm thấy", "Not found"),
            Font = new Font("Segoe UI", 7f),
            ForeColor = installed ? Theme.TextMuted : ColorTranslator.FromHtml("#663333"),
            Location = new Point(40, 24),
            Size = new Size(105, 16),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = boxBg,
            Cursor = Cursors.Hand,
        };
        box.Controls.Add(lblIcon);
        box.Controls.Add(lblName);
        box.Controls.Add(lblStatus);

        // The 3 labels above visually cover almost the whole box, so a click there never
        // reaches box's own Click event (WinForms routes the click to whichever control is on
        // top) — every label needs the same handler, not just the panel background.
        EventHandler select = (_, _) =>
        {
            _selectedBrowser = browser;
            _chromeCard.BorderColor = browser == CookieBrowser.Chrome ? Theme.Accent : Theme.Border;
            _edgeCard.BorderColor = browser == CookieBrowser.Edge ? Theme.Accent : Theme.Border;
            _btnReadBrowser.Text = T($"🔍  Đọc Cookie từ {name}", $"🔍  Read Cookie from {name}");
        };
        box.Click += select;
        lblIcon.Click += select;
        lblName.Click += select;
        lblStatus.Click += select;
        return box;
    }

    private CookieMethodCard BuildMethodCardPaste()
    {
        var card = new CookieMethodCard(ContentWidth, "📋", T("Dán cookie thủ công", "Paste cookie manually"), T("Copy từ DevTools → paste vào đây", "Copy from DevTools → paste here"), T("Thủ công", "Manual"), Theme.Blue);
        card.OnExpand += () => SelectCard(card);
        _methodCards.Add(card);

        _pasteBox = new TextBox
        {
            Multiline = true,
            Width = ContentWidth - 32,
            Height = 70,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextSecondary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8f),
            PlaceholderText = "sessionid=abc123; tt_webid=789...; msToken=xyz...",
        };

        var btnImport = new Guna2Button
        {
            Text = "📥  Import Cookie",
            Font = Theme.FontMedium,
            Width = ContentWidth - 32,
            Height = 34,
            Margin = new Padding(0),
            FillColor = Color.FromArgb(38, Theme.Blue.R, Theme.Blue.G, Theme.Blue.B),
            ForeColor = Theme.Blue,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnImport.Click += async (_, _) => await ImportPastedCookieAsync();

        card.SetContent(_pasteBox, btnImport);
        return card;
    }

    private CookieMethodCard BuildMethodCardLogin()
    {
        var card = new CookieMethodCard(ContentWidth, "🔑", T("Login trong tool", "Login in-tool"), T("Mở TikTok web, đăng nhập, tool tự lưu cookie", "Opens TikTok web, log in, tool saves cookie automatically"), "Fallback", Theme.Orange);
        card.OnExpand += () => SelectCard(card);
        _methodCards.Add(card);

        var btnLogin = new Guna2Button
        {
            Text = T("🚀  Mở TikTok để Login", "🚀  Open TikTok to Login"),
            Font = Theme.FontMedium,
            Width = ContentWidth - 32,
            Height = 34,
            Margin = new Padding(0),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnLogin.Click += async (_, _) => await DoLoginInToolAsync(btnLogin);

        card.SetContent(btnLogin);
        return card;
    }

    private Control BuildActionBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = ColorTranslator.FromHtml("#0d0d0d"),
        };
        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        _actionInfo = new Label
        {
            Text = T("Chưa có cookie nào được lưu", "No cookie saved yet"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 18),
        };

        _btnSave = new Guna2Button
        {
            Text = T("Lưu Cookie", "Save Cookie"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(560, 12),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnSave.Click += async (_, _) => await SaveCurrentCookieAsync();

        _btnReverify = new Guna2Button
        {
            Text = T("Kiểm tra lại", "Verify again"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(430, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnReverify.Click += async (_, _) => await ReverifyAsync();

        bar.Controls.Add(_actionInfo);
        bar.Controls.Add(_btnReverify);
        bar.Controls.Add(_btnSave);
        bar.Controls.Add(sep);
        return bar;
    }

    private async Task LoadSavedCookieAsync()
    {
        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null)
        {
            _log.Log(T("[Cookie] Chưa có cookie nào được lưu trên máy", "[Cookie] No cookie saved on this machine"), LogLevel.Info);
            return;
        }

        _currentCookie = cookie;
        _log.Log(T($"[Cookie] Đã nạp cookie đã lưu (nguồn: {cookie.Source}, lúc {cookie.CapturedAt:HH:mm dd/MM})",
                   $"[Cookie] Loaded saved cookie (source: {cookie.Source}, at {cookie.CapturedAt:HH:mm dd/MM})"), LogLevel.Info);
        RenderStatusPanel();
        UpdateHeaderAndActionBar();
    }

    private async Task ReadFromBrowserAsync()
    {
        var name = _selectedBrowser == CookieBrowser.Chrome ? "Chrome" : "Edge";
        _btnReadBrowser.Enabled = false;
        try
        {
            var cookie = await ReadFromBrowserWithRetryAsync(name, allowKillRetry: true);
            if (cookie is null)
            {
                return;
            }

            _log.Log(T("[Cookie] Tìm thấy session TikTok hợp lệ", "[Cookie] Found valid TikTok session"), LogLevel.Ok);
            await AdoptCookieAsync(cookie);
        }
        finally
        {
            _btnReadBrowser.Enabled = true;
        }
    }

    /// <summary>Reads the cookie, and if Chrome/Edge has the cookie DB locked, offers to
    /// force-close the browser and retry once — instead of just telling the user to close it
    /// themselves and click the button again.</summary>
    private async Task<TikTokCookie?> ReadFromBrowserWithRetryAsync(string name, bool allowKillRetry)
    {
        _log.Log(T($"[Cookie] Đang đọc từ {name} SQLite...", $"[Cookie] Reading from {name} SQLite..."), LogLevel.Info);
        try
        {
            var cookie = await _cookieService.ReadFromChromiumBrowser(_selectedBrowser);
            if (cookie is null)
            {
                _log.Log(
                    T($"[Cookie] Không tìm thấy session TikTok đã login trong {name} (đã quét tất cả profile: Default, Profile 1, 2...). " +
                      "Kiểm tra: bạn có đang login TikTok đúng bằng trình duyệt này không, hay dùng trình duyệt/app khác? Thử đăng xuất rồi đăng nhập lại TikTok trên đúng profile Chrome này.",
                      $"[Cookie] No logged-in TikTok session found in {name} (scanned all profiles: Default, Profile 1, 2...). " +
                      "Check: are you logged into TikTok in this browser, or using a different browser/app? Try signing out and back in on this Chrome profile."),
                    LogLevel.Error);
            }

            return cookie;
        }
        catch (InvalidOperationException ex) when (allowKillRetry && ex.Message.Contains("đang khóa file cookie"))
        {
            _log.Log($"[Cookie] {ex.Message}", LogLevel.Warn);

            var choice = MessageBox.Show(
                T($"{name} đang mở nên khóa file cookie.\n\nCho phép TikTool tự đóng {name} để đọc cookie? " +
                  $"Tất cả tab đang mở trong {name} sẽ bị đóng ngay lập tức (không lưu).",
                  $"{name} is open and has the cookie file locked.\n\nAllow TikTool to close {name} to read the cookie? " +
                  $"All open tabs in {name} will be closed immediately (unsaved)."),
                T("TikTool — Đóng trình duyệt?", "TikTool — Close browser?"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (choice != DialogResult.Yes)
            {
                _log.Log(T($"[Cookie] Đã huỷ — hãy tự đóng hoàn toàn {name} (kể cả icon khay hệ thống) rồi bấm lại.",
                           $"[Cookie] Cancelled — please close {name} completely (including system tray icon) then click again."), LogLevel.Warn);
                return null;
            }

            _log.Log(T($"[Cookie] Đang đóng {name}...", $"[Cookie] Closing {name}..."), LogLevel.Info);
            await Task.Run(() => CookieService.KillBrowserProcesses(_selectedBrowser));
            await Task.Delay(500);

            return await ReadFromBrowserWithRetryAsync(name, allowKillRetry: false);
        }
        catch (Exception ex)
        {
            _log.Log(T($"[Cookie] Lỗi khi đọc cookie: {ex.Message}", $"[Cookie] Error reading cookie: {ex.Message}"), LogLevel.Error);
            return null;
        }
    }

    private async Task ImportPastedCookieAsync()
    {
        var raw = _pasteBox.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.Log(T("[Cookie] Chưa nhập chuỗi cookie để import", "[Cookie] No cookie string entered to import"), LogLevel.Warn);
            return;
        }

        try
        {
            var cookie = CookieService.ParseCookieString(raw);
            _log.Log(T("[Cookie] Đã parse cookie thủ công thành công", "[Cookie] Manually pasted cookie parsed successfully"), LogLevel.Ok);
            await AdoptCookieAsync(cookie);
        }
        catch (Exception ex)
        {
            _log.Log($"[Cookie] {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>Opens a real, visible window of the user's actual installed Chrome (via
    /// PlaywrightService.LoginInteractivelyAsync, connected over CDP — separate instance from
    /// the headless Playwright-bundled Chromium UnfollowPage drives) on tiktok.com/login, and
    /// polls for a session cookie to appear — the user logs in by hand (password, QR, SMS,
    /// whatever TikTok asks for), and as soon as a session shows up this saves it exactly like
    /// the other two methods. Uses real Chrome specifically because TikTok's QR login does not
    /// render at all in Playwright's own bundled Chromium (confirmed 2026-09).</summary>
    private async Task DoLoginInToolAsync(Guna2Button trigger)
    {
        if (_loginInProgress)
        {
            return;
        }

        _loginInProgress = true;
        trigger.Enabled = false;
        var originalText = trigger.Text;
        trigger.Text = T("⏳  Đang chờ đăng nhập...", "⏳  Waiting for login...");

        _log.Log(T("[Login] Đang mở Chrome thật cho bạn đăng nhập TikTok...", "[Login] Opening real Chrome for you to log in to TikTok..."), LogLevel.Info);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var cookie = await _playwright.LoginInteractivelyAsync(TimeSpan.FromMinutes(5), cts.Token);

            if (cookie is null)
            {
                _log.Log(T("[Login] Đã đóng cửa sổ hoặc hết thời gian chờ (5 phút) mà chưa đăng nhập xong.",
                           "[Login] Window closed or timed out (5 min) before login completed."), LogLevel.Warn);
                return;
            }

            _log.Log(T("[Login] Đăng nhập thành công — đã bắt được session.", "[Login] Login successful — session captured."), LogLevel.Ok);
            await AdoptCookieAsync(cookie);
        }
        catch (OperationCanceledException)
        {
            _log.Log(T("[Login] Đã hết thời gian chờ đăng nhập (5 phút).", "[Login] Login timed out (5 minutes)."), LogLevel.Warn);
        }
        catch (Exception ex)
        {
            _log.Log($"[Login] Lỗi: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _loginInProgress = false;
            trigger.Enabled = true;
            trigger.Text = originalText;
        }
    }

    private async Task AdoptCookieAsync(TikTokCookie cookie)
    {
        _currentCookie = cookie;
        RenderStatusPanel();
        UpdateHeaderAndActionBar();
        await SaveCurrentCookieAsync(silent: true);
        await ReverifyAsync();
    }

    private async Task ReverifyAsync()
    {
        if (_currentCookie is null)
        {
            _log.Log(T("[Verify] Chưa có cookie để kiểm tra", "[Verify] No cookie to verify"), LogLevel.Warn);
            return;
        }

        _btnReverify.Enabled = false;
        _log.Log(T("[Verify] Đang xác thực với TikTok...", "[Verify] Verifying with TikTok..."), LogLevel.Info);
        try
        {
            var result = await _cookieService.VerifyCookieAsync(_currentCookie);
            if (result.IsValid)
            {
                _currentCookie.Username = result.Username;
                var days = _currentCookie.DaysLeft;
                _log.Log(T($"[Verify] Đã xác thực: @{result.Username}" + (days is int d ? $" · Cookie còn hạn {d} ngày" : ""),
                           $"[Verify] Verified: @{result.Username}" + (days is int d2 ? $" · Cookie expires in {d2} days" : "")), LogLevel.Ok);
                await SaveCurrentCookieAsync(silent: true);
            }
            else
            {
                _log.Log($"[Verify] {result.Error}", LogLevel.Error);
            }
        }
        finally
        {
            RenderStatusPanel();
            UpdateHeaderAndActionBar();
            _btnReverify.Enabled = true;
        }
    }

    private async Task SaveCurrentCookieAsync(bool silent = false)
    {
        if (_currentCookie is null)
        {
            if (!silent)
            {
                _log.Log(T("[Cookie] Không có cookie để lưu", "[Cookie] No cookie to save"), LogLevel.Warn);
            }
            return;
        }

        await _cookieService.SaveCookieAsync(_currentCookie);
        if (!silent)
        {
            _log.Log(T("[Cookie] Đã lưu cookie (mã hoá DPAPI) vào %APPDATA%\\TikTool\\cookie.json",
                       "[Cookie] Cookie saved (DPAPI-encrypted) to %APPDATA%\\TikTool\\cookie.json"), LogLevel.Ok);
        }
        UpdateHeaderAndActionBar();
    }

    private void DeleteCookie()
    {
        _cookieService.DeleteCookie();
        _currentCookie = null;
        RenderStatusPanel();
        UpdateHeaderAndActionBar();
        _log.Log(T("[Cookie] Đã xoá cookie đã lưu", "[Cookie] Saved cookie deleted"), LogLevel.Warn);
    }

    private void UpdateHeaderAndActionBar()
    {
        if (_currentCookie is null)
        {
            _headerBadge.Text = T("Chưa có cookie", "No cookie");
            _headerBadge.ForeColor = Theme.TextMuted;
            _actionInfo.Text = T("Chưa có cookie nào được lưu", "No cookie saved yet");
            return;
        }

        var loggedIn = _currentCookie.Username is not null;
        _headerBadge.Text = loggedIn ? T("✓ Đã login", "✓ Logged in") : T("Cookie chưa xác thực", "Cookie not verified");
        _headerBadge.ForeColor = loggedIn ? Theme.Green : Theme.Orange;

        _actionInfo.Text = loggedIn
            ? T($"Session: @{_currentCookie.Username} · Còn hạn {_currentCookie.DaysLeft?.ToString() ?? "?"} ngày",
                $"Session: @{_currentCookie.Username} · Expires in {_currentCookie.DaysLeft?.ToString() ?? "?"} days")
            : T("Cookie đã lưu nhưng chưa xác thực — bấm Kiểm tra lại", "Cookie saved but not verified — click Verify again");

        CookieStatusChanged?.Invoke();
    }
}
