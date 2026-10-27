using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

internal sealed class UnfollowPage : UserControl
{
    private const int ContentWidth = 700;

    private readonly CookieService _cookieService;
    private readonly TikTokApiService _apiService;
    private readonly PlaywrightService _playwright;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly SessionStateService _sessionStateService;

    private readonly List<FollowingAccount> _accounts = new();
    private readonly Dictionary<FollowingAccount, FollowingItemControl> _rows = new();

    private CancellationTokenSource? _cts;
    private bool _isBusy;

    private static readonly int?[] CriteriaThresholdDays = { null, 30, 90, 180, 365 };

    private Label _headerBadge = null!;
    private ComboBox _criteriaCombo = null!;
    private ComboBox _modeCombo = null!;
    private Label _sectionLabel = null!;
    private FlowLayoutPanel _listFlow = null!;
    private LogPanel _log = null!;
    private Label _actionInfo = null!;
    private Guna2Button _btnSelectDead = null!;
    private Guna2Button _btnReload = null!;
    private Guna2Button _btnClearSelection = null!;
    private Guna2Button _btnStart = null!;
    private Label _emptyHint = null!;
    private ComboBox _viewCombo = null!;
    private ComboBox _sortCombo = null!;
    private TextBox _searchBox = null!;
    private System.Windows.Forms.Timer _searchTimer = null!;
    private Guna2Button _btnSelectShown = null!;
    private Guna2Button _btnCheckDates = null!;
    private readonly LastPostCache _dateCache = new();

    public UnfollowPage(
        PlaywrightService playwright,
        TikTokApiService apiService,
        CookieService cookieService,
        SettingsService settingsService,
        HistoryService historyService,
        SessionStateService sessionStateService)
    {
        _playwright = playwright;
        _apiService = apiService;
        _cookieService = cookieService;
        _settingsService = settingsService;
        _historyService = historyService;
        _sessionStateService = sessionStateService;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
    }

    private void BuildUi()
    {
        // Dock=Fill must be added before Top/Bottom siblings — see CookiePage for why.
        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
        Controls.Add(BuildActionBar());
        StyleDisabledButtons();
        UpdateButtonStates();
    }

    private void StyleDisabledButtons()
    {
        foreach (var b in new[] { _btnReload, _btnSelectDead, _btnSelectShown, _btnClearSelection, _btnStart, _btnCheckDates })
        {
            b.DisabledState.FillColor = ColorTranslator.FromHtml("#151515");
            b.DisabledState.ForeColor = ColorTranslator.FromHtml("#4a4a4a");
            b.DisabledState.BorderColor = ColorTranslator.FromHtml("#151515");
        }
    }

    /// <summary>Single source of truth for which buttons are usable: nothing to act on before a list is
    /// loaded; only Stop while busy; Start needs at least one selected account (and shows the count).</summary>
    private void UpdateButtonStates()
    {
        void Apply()
        {
            var selectedCount = _accounts.Count(a => a.IsSelected && !a.IsWhitelisted && a.Status != UnfollowStatus.Done);
            var anySelected = _accounts.Any(a => a.IsSelected);
            var anyDead = _accounts.Any(a => a.IsDead && !a.IsWhitelisted);

            _btnReload.Enabled = !_isBusy;
            _btnSelectDead.Enabled = !_isBusy && anyDead;
            _btnSelectShown.Enabled = !_isBusy && _accounts.Count > 0;

            var needDates = _accounts.Count(a => a.VideoCount > 0 && a.LastPostUnixSeconds == null);
            var datesMatter = CriteriaThresholdDays[_criteriaCombo.SelectedIndex] is not null;
            _btnCheckDates.Enabled = !_isBusy && needDates > 0 && datesMatter;
            _btnCheckDates.Text = needDates > 0
                ? T($"📅  Kiểm tra ngày ({needDates})", $"📅  Check dates ({needDates})")
                : T("📅  Đã đủ ngày đăng", "📅  All dates known");
            _btnClearSelection.Enabled = !_isBusy && anySelected;
            _btnStart.Enabled = _isBusy || selectedCount > 0;
            _btnStart.Text = _isBusy
                ? T("⏹  Dừng", "⏹  Stop")
                : selectedCount > 0
                    ? T($"🚫  Bắt đầu Unfollow ({selectedCount})", $"🚫  Start Unfollow ({selectedCount})")
                    : T("🚫  Bắt đầu Unfollow", "🚫  Start Unfollow");

            _emptyHint.Visible = _accounts.Count == 0;
            _emptyHint.Text = _isBusy
                ? T("Đang tải danh sách theo dõi…", "Loading your following list…")
                : T("Bấm \"Tải lại\" để tải danh sách theo dõi.", "Press Reload to load your following list.");
        }

        if (InvokeRequired) BeginInvoke(Apply); else Apply();
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
            Text = T("Bỏ Theo Dõi Kênh Chết", "Unfollow Dead Channels"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        };
        var subtitle = new Label
        {
            Text = T("Dọn dẹp danh sách theo dõi TikTok — bỏ theo dõi tài khoản không hoạt động lâu ngày",
                     "Clean up your TikTok following list — unfollow accounts that have been inactive for a long time"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        };

        _headerBadge = new Label
        {
            Text = T("Cần cookie", "Cookie needed"),
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

        _log = new LogPanel { Dock = DockStyle.Fill };
        var logHost = new Panel { Dock = DockStyle.Bottom, Height = 116, Padding = new Padding(20, 0, 20, 16), BackColor = Theme.Background };
        logHost.Controls.Add(_log);

        var scroll = BuildScrollArea();

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

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            BackColor = Theme.Background,
            Location = new Point(20, 16),
        };

        flow.Controls.Add(BuildFilterRow());
        flow.Controls.Add(BuildListSection());

        scroll.Controls.Add(flow);
        return scroll;
    }

    private Control BuildFilterRow()
    {
        var row = new Panel { Width = ContentWidth, Height = 118, Margin = new Padding(0, 0, 0, 16) };

        var criteriaLabel = new Label
        {
            Text = T("Tiêu chí kênh chết", "Dead channel criteria"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _criteriaCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(0, 20),
            Size = new Size(ContentWidth / 2 - 8, 30),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        // Chỉ mục phải khớp thứ tự với CriteriaThresholdDays (null = chỉ tính 0 video).
        _criteriaCombo.Items.Add(T("Chưa từng đăng (0 video)", "Never posted (0 videos)"));
        _criteriaCombo.Items.Add(T("Không đăng > 30 ngày", "No posts > 30 days"));
        _criteriaCombo.Items.Add(T("Không đăng > 90 ngày", "No posts > 90 days"));
        _criteriaCombo.Items.Add(T("Không đăng > 180 ngày (mặc định)", "No posts > 180 days (default)"));
        _criteriaCombo.Items.Add(T("Không đăng > 365 ngày", "No posts > 365 days"));
        _criteriaCombo.SelectedIndex = 3;
        _criteriaCombo.SelectedIndexChanged += (_, _) =>
        {
            RecomputeDeadStatus();
            RebuildList();
            UpdateHeaderBadge();
        };

        var modeLabel = new Label
        {
            Text = T("Chế độ bỏ theo dõi", "Unfollow mode"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(ContentWidth / 2 + 8, 0),
        };
        _modeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(ContentWidth / 2 + 8, 20),
            Size = new Size(ContentWidth / 2 - 8, 30),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        _modeCombo.Items.Add(T("API trước, dự phòng trình duyệt", "API first, browser fallback"));
        _modeCombo.Items.Add(T("Chỉ qua trình duyệt (an toàn hơn, chậm hơn)", "Browser only (safer, slower)"));
        _modeCombo.SelectedIndex = _settingsService.Current.PreferApiOverPlaywright ? 0 : 1;

        row.Controls.Add(criteriaLabel);
        row.Controls.Add(_criteriaCombo);
        row.Controls.Add(modeLabel);
        row.Controls.Add(_modeCombo);

        // Second row: what to show, how to order it, and a name search.
        var third = ContentWidth / 3;
        Label MakeLabel(string text, int x) => new()
        {
            Text = text,
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(x, 62),
        };
        ComboBox MakeCombo(int x, params string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, 82),
                Size = new Size(third - 8, 30),
                BackColor = ColorTranslator.FromHtml("#161616"),
                ForeColor = Theme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
            };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        // Index order must match GetVisibleAccounts.
        _viewCombo = MakeCombo(0,
            T("Tất cả kênh", "All channels"),
            T("Chỉ kênh chết", "Dead only"),
            T("Chỉ kênh còn hoạt động", "Active only"),
            T("Chỉ kênh đã chọn", "Selected only"));
        _sortCombo = MakeCombo(third,
            T("Thứ tự mặc định", "Default order"),
            T("Lâu không đăng nhất trước", "Longest inactive first"),
            T("Ít video nhất trước", "Fewest videos first"),
            T("Tên A → Z", "Name A → Z"));
        _viewCombo.SelectedIndexChanged += (_, _) => RebuildList();
        _sortCombo.SelectedIndexChanged += (_, _) => RebuildList();

        _searchBox = new TextBox
        {
            Location = new Point(third * 2, 83),
            Size = new Size(third - 4, 26),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Tìm theo tên / @username", "Search name / @username"),
        };
        // Rebuilding ~500 rows per keystroke would stutter, so wait for a short pause in typing.
        _searchTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RebuildList(); };
        _searchBox.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };

        row.Controls.Add(MakeLabel(T("Hiển thị", "Show"), 0));
        row.Controls.Add(_viewCombo);
        row.Controls.Add(MakeLabel(T("Sắp xếp", "Sort"), third));
        row.Controls.Add(_sortCombo);
        row.Controls.Add(MakeLabel(T("Tìm kiếm", "Search"), third * 2));
        row.Controls.Add(_searchBox);
        return row;
    }

    private Control BuildListSection()
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true };

        _sectionLabel = new Label
        {
            Text = T("Chưa tải danh sách theo dõi", "Following list not loaded"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(0, 6),
        };

        _btnReload = new Guna2Button
        {
            Text = T("🔄  Tải lại", "🔄  Reload"),
            Font = Theme.FontMedium,
            Size = new Size(100, 28),
            Location = new Point(ContentWidth - 100, 0),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnReload.Click += async (_, _) => await ReloadFollowingListAsync();

        _btnSelectDead = new Guna2Button
        {
            Text = T("☠  Chọn kênh chết", "☠  Select dead"),
            Font = Theme.FontMedium,
            Size = new Size(150, 28),
            Location = new Point(ContentWidth - 258, 0),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnSelectDead.Click += (_, _) => SelectAllDead();

        _btnSelectShown = new Guna2Button
        {
            Text = T("☑  Chọn tất cả đang hiện", "☑  Select all shown"),
            Font = Theme.FontMedium,
            Size = new Size(170, 28),
            Location = new Point(ContentWidth - 258 - 8 - 170, 0),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnSelectShown.Click += (_, _) => SelectAllShown();

        _listFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(0, 36),
            Width = ContentWidth,
            BackColor = Color.Transparent,
        };

        _emptyHint = new Label
        {
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(0, 70),
        };

        wrap.Controls.Add(_emptyHint);
        wrap.Controls.Add(_sectionLabel);
        wrap.Controls.Add(_btnSelectDead);
        wrap.Controls.Add(_btnSelectShown);
        wrap.Controls.Add(_btnReload);
        wrap.Controls.Add(_listFlow);
        wrap.Height = 250;
        return wrap;
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
            Text = T("0 đã chọn", "0 selected"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 21),
        };

        _btnStart = new Guna2Button
        {
            Text = T("Bắt đầu bỏ theo dõi", "Start unfollowing"),
            Font = Theme.FontMedium,
            Size = new Size(210, 32),
            Location = new Point(ContentWidth - 210, 12),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnStart.Click += async (_, _) => await StartOrStopUnfollowAsync();

        _btnClearSelection = new Guna2Button
        {
            Text = T("Bỏ chọn", "Clear"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(ContentWidth - 210 - 8 - 110, 12),
            FillColor = ColorTranslator.FromHtml("#2a2a2a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnClearSelection.Click += (_, _) => ClearSelection();

        _btnCheckDates = new Guna2Button
        {
            Text = T("📅  Kiểm tra ngày", "📅  Check dates"),
            Font = Theme.FontMedium,
            Size = new Size(215, 32),
            Location = new Point(150, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnCheckDates.Click += async (_, _) => await CheckLastPostDatesAsync();

        bar.Controls.Add(_actionInfo);
        bar.Controls.Add(_btnClearSelection);
        bar.Controls.Add(_btnCheckDates);
        bar.Controls.Add(_btnStart);
        bar.Controls.Add(sep);
        return bar;
    }

    // ══════════════════════════════════════════════════════════
    // ACTIONS
    // ══════════════════════════════════════════════════════════

    private async Task ReloadFollowingListAsync()
    {
        if (_isBusy) return;

        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null || string.IsNullOrEmpty(cookie.Username))
        {
            _log.Log(T("[Unfollow] Chưa có cookie đã xác thực — sang tab Cookie để đăng nhập trước.",
                        "[Unfollow] No authenticated cookie — go to Cookie tab to sign in first."), LogLevel.Warn);
            return;
        }

        // ── Session resume: hỏi user có muốn tiếp tục phiên cũ không ──────────────
        if (_sessionStateService.HasResumableSession())
        {
            var saved = _sessionStateService.GetResumableSession()!;
            var pending = saved.Accounts.Count(a => a.IsSelected && a.Status == Models.UnfollowStatus.Pending);
            var done = saved.Accounts.Count(a => a.Status == Models.UnfollowStatus.Done);
            var choice = MessageBox.Show(
                T($"Còn phiên chưa hoàn tất từ {saved.SavedAt:HH:mm dd/MM}.\n" +
                  $"Đã unfollow {done}/{saved.Accounts.Count(a => a.IsSelected)} kênh, còn {pending} kênh chờ.\n\n" +
                  $"Tiếp tục phiên cũ?",
                  $"Unfinished session from {saved.SavedAt:HH:mm dd/MM}.\n" +
                  $"Unfollowed {done}/{saved.Accounts.Count(a => a.IsSelected)}, {pending} pending.\n\n" +
                  $"Resume this session?"),
                T("Social Toolkit — Tiếp tục phiên cũ?", "Social Toolkit — Resume session?"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (choice == DialogResult.Yes)
            {
                _accounts.Clear();
                _accounts.AddRange(saved.Accounts);
                ApplyWhitelistFlags();
                RecomputeDeadStatus();
                RebuildList();
                StartAvatarDownloads();
                _log.Log(T($"[Unfollow] Đã khôi phục {_accounts.Count} tài khoản từ phiên trước.",
                           $"[Unfollow] Restored {_accounts.Count} accounts from previous session."), LogLevel.Ok);
                UpdateHeaderBadge();
                return;
            }

            // User chọn "No" = xóa phiên cũ, bắt đầu lại
            _sessionStateService.ClearSession();
        }

        _isBusy = true;
        _btnReload.Enabled = false;
        UpdateButtonStates();
        _cts = new CancellationTokenSource();
        var reloadCt = _cts.Token;
        _log.Log(T("[Playwright] Đang mở Chrome thật (sẽ hiện cửa sổ) và inject cookie...",
                    "[Playwright] Opening real Chrome (a window will appear) and injecting cookie..."), LogLevel.Info);

        try
        {
            await RelaunchPlaywrightAsync(cookie);

            _log.Log(T("[Unfollow] Đang tải danh sách đang theo dõi (cuộn tự động)...",
                        "[Unfollow] Loading following list (auto-scroll)..."), LogLevel.Info);
            var progress = new Progress<string>(m => _log.Log(m, LogLevel.Info));
            var selectors = SelectorsService.Load();
            var accounts = await _playwright.LoadFollowingListAsync(selectors, cookie.Username!, progress, reloadCt);

            _accounts.Clear();
            _accounts.AddRange(accounts);
            ApplyDateCache();
            ApplyWhitelistFlags();
            _log.Log(T($"[Unfollow] Đã tải {accounts.Count} tài khoản. Đang kiểm tra ngày đăng gần nhất...",
                        $"[Unfollow] Loaded {accounts.Count} accounts. Checking last post dates..."), LogLevel.Ok);

            await CheckDeadChannelsAsync(reloadCt);
            RecomputeDeadStatus();

            // Warn when the selected criteria uses a day threshold but some accounts couldn't be
            // checked (VideoCount > 0, last-post date unknown — either GetUserDetail returned null
            // or the item_list intercept missed). These accounts won't be auto-marked dead even if
            // they haven't posted in years, so the user should know.
            var thresholdIdx = 0;
            _criteriaCombo.Invoke(() => thresholdIdx = _criteriaCombo.SelectedIndex);
            if (thresholdIdx > 0) // 0 = "0 video" criteria, no date check
            {
                var unknownDate = _accounts.Count(a => a.VideoCount > 0 && a.LastPostUnixSeconds == null);
                if (unknownDate > 0)
                    _log.Log(T($"[Unfollow] ⚠ {unknownDate} kênh có video nhưng không lấy được ngày đăng gần nhất — sẽ không bị tính là kênh chết theo tiêu chí ngày. Bấm nút 'Kiểm tra ngày' ở thanh dưới để lấy dần (từng đợt nhỏ, an toàn).",
                               $"[Unfollow] ⚠ {unknownDate} accounts have videos but last-post date is unknown — they won't be auto-marked dead by the day threshold. Use the 'Check dates' button below to read them in small, safe batches."),
                        LogLevel.Warn);
            }

            // Lưu session sau khi CheckDead xong — đây là điểm khởi đầu đáng tin để resume
            _sessionStateService.SaveSession(_accounts, accounts.Count);

            RebuildList();
            StartAvatarDownloads();
            _log.Log(T($"[Unfollow] Hoàn tất — {_accounts.Count(a => a.IsDead)} kênh chết / {_accounts.Count} tổng.",
                        $"[Unfollow] Done — {_accounts.Count(a => a.IsDead)} dead / {_accounts.Count} total."), LogLevel.Ok);
        }
        catch (Exception ex)
        {
            _log.Log(T($"[Unfollow] Lỗi khi tải danh sách: {ex.Message}", $"[Unfollow] Failed to load list: {ex.Message}"), LogLevel.Error);
        }
        finally
        {
            _isBusy = false;
            _btnReload.Enabled = true;
            UpdateButtonStates();
            _cts?.Dispose();
            _cts = null;
            UpdateHeaderBadge();
        }
    }

    /// <summary>Đánh dấu các account có trong whitelist — gọi ngay sau khi load accounts.</summary>
    private void ApplyWhitelistFlags()
    {
        foreach (var account in _accounts)
        {
            account.IsWhitelisted = _sessionStateService.IsWhitelisted(account.Username);
        }
    }

    /// <summary>For the following-list reload, always start a fresh browser session — the sidebar
    /// "Following accounts" panel only makes its full API call on a fresh Chrome load; reusing a
    /// session from a previous unfollow pass leaves the panel serving from an in-memory cache
    /// that only contains the initial 5-6 preview accounts rather than the full list.</summary>
    private async Task RelaunchPlaywrightAsync(TikTokCookie cookie)
    {
        await _playwright.DisposeAsync();
        await EnsureChromeAsync();
        await _playwright.LaunchAsync(cookie);
    }

    private async Task EnsurePlaywrightAsync(TikTokCookie cookie)
    {
        if (_playwright.IsLaunched) return;
        await EnsureChromeAsync();
        await _playwright.LaunchAsync(cookie);
    }

    /// <summary>Ensures Chrome/Edge is installed — downloads and installs silently if not found.
    /// Shows download progress in the log panel. Throws if user cancels or install fails.</summary>
    private async Task EnsureChromeAsync()
    {
        if (PlaywrightService.FindRealChromeExecutable() is not null) return;

        _log.Log(T("Đang tải Chrome offline installer (~80 MB)...",
                   "Downloading Chrome offline installer (~80 MB)..."), LogLevel.Info);

        var progress = new Progress<(long Downloaded, long Total)>(p =>
        {
            var pct = p.Total > 0 ? (int)(p.Downloaded * 100 / p.Total) : 0;
            var mb = p.Downloaded / 1_048_576.0;
            _log.Log(T($"  Đang tải... {mb:F1} MB ({pct}%)",
                       $"  Downloading... {mb:F1} MB ({pct}%)"), LogLevel.Info);
        });

        await PlaywrightService.EnsureChromeInstalledAsync(progress, _cts?.Token ?? default);

        _log.Log(T("Chrome đã được cài thành công.", "Chrome installed successfully."), LogLevel.Ok);
    }

    /// <summary>Checks all accounts in parallel (3 concurrent pages) to get video counts and
    /// last-post dates. Three concurrent workers is a balance between speed (3x faster than
    /// sequential) and bot-detection risk (more than 3–4 simultaneous profile navigations from
    /// one browser can trigger TikTok's rate limiter mid-scan). Uses
    /// <see cref="PlaywrightService.GetUserDetailParallelAsync"/> which opens and closes its own
    /// page for each call, leaving the shared _page free for the unfollow pass.</summary>
    private async Task CheckDeadChannelsAsync(CancellationToken ct = default)
    {
        // Accounts whose VideoCount was already populated by the API interceptor during list load
        // don't need a separate profile visit — skip them entirely. This is especially important
        // for deleted accounts (profile returns "Access Denied") whose videoCount and userId we
        // captured from TikTok's own following-panel API call.
        var accountsToCheck = _accounts.Where(a => a.VideoCount < 0).ToList();
        var skippedByIntercept = _accounts.Count - accountsToCheck.Count;
        if (skippedByIntercept > 0)
            _log.Log(T($"[Unfollow] Bỏ qua {skippedByIntercept} kênh đã có dữ liệu từ API intercept — chỉ cần kiểm tra {accountsToCheck.Count} kênh.",
                       $"[Unfollow] Skipping {skippedByIntercept} accounts already populated by API intercept — checking {accountsToCheck.Count} remaining."), LogLevel.Info);

        if (accountsToCheck.Count == 0) return;

        PlaywrightService.ResetDetailStats();
        const int concurrency = 3;
        var sem = new SemaphoreSlim(concurrency, concurrency);
        var nullStreak = 0;
        var nullStreakLock = new object();
        var done = 0;
        var total = accountsToCheck.Count;

        var tasks = accountsToCheck.Select(async account =>
        {
            await sem.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var detail = await _playwright.GetUserDetailParallelAsync(account.Username, ct);

                int current;
                lock (nullStreakLock)
                {
                    if (detail is { } d)
                    {
                        account.UserId = d.UserId;
                        account.VideoCount = d.VideoCount;
                        account.LastPostUnixSeconds = d.LastPostUnixSeconds;
                        if (account.Relation < 0 && d.Relation >= 0)
                            account.Relation = d.Relation;
                        nullStreak = 0;
                    }
                    else
                    {
                        nullStreak++;
                        _log.Log($"[Unfollow] Không đọc được @{account.Username} (thử mở: https://www.tiktok.com/@{account.Username})", LogLevel.Warn);
                    }

                    current = ++done;
                }

                if (nullStreak >= 5)
                {
                    // Reset streak and warn once (multiple workers can trigger this, but the
                    // lock above means the count is authoritative).
                    lock (nullStreakLock) { nullStreak = 0; }
                    _log.Log(T("[Unfollow] Cảnh báo: nhiều tài khoản liên tiếp không đọc được — có thể cookie hết hạn hoặc bị rate limit.",
                               "[Unfollow] Warning: several accounts unreadable — cookie may have expired or rate limited."), LogLevel.Warn);
                }

                if (current % 10 == 0 || current == total)
                {
                    _log.Log(T($"[Unfollow] Đã kiểm tra {current}/{total} tài khoản...",
                               $"[Unfollow] Checked {current}/{total} accounts..."), LogLevel.Info);
                }

                // Periodic session check every 50 accounts completed.
                if (current % 50 == 0)
                {
                    var alive = await _playwright.IsSessionActiveAsync(ct);
                    if (!alive)
                    {
                        throw new InvalidOperationException(T(
                            "[Unfollow] Cookie hết hạn trong khi đang scan — vào tab Cookie để làm mới rồi thử lại.",
                            "[Unfollow] Cookie expired during scan — go to Cookie tab to refresh and try again."));
                    }
                }
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        _log.Log($"[Unfollow][DBG] Profile visit results: {PlaywrightService.DescribeDetailStats()}", LogLevel.Info);
    }

    // Reading dates means opening each profile, which is what got the session blocked when it ran
    // unattended for ~500 accounts. So it is a separate, opt-in button that works in small, slow
    // batches (results cached on disk) and stops the moment TikTok starts refusing pages.
    private const int MaxDateChecksPerRun = 120;
    private const int DateCheckConcurrency = 2;
    private const int DateCheckBlockStreak = 5;
    private readonly SemaphoreSlim _dateCheckGate = new(DateCheckConcurrency, DateCheckConcurrency);

    /// <summary>Fills in last-post dates from the on-disk cache wherever it is still trustworthy.</summary>
    private void ApplyDateCache()
    {
        foreach (var account in _accounts.Where(a => a.VideoCount > 0 && a.LastPostUnixSeconds == null))
        {
            if (_dateCache.TryGetFresh(account.Username, account.VideoCount) is long cached)
            {
                account.LastPostUnixSeconds = cached;
            }
        }
    }

    private async Task CheckLastPostDatesAsync()
    {
        if (_isBusy) return;

        var targets = _accounts
            .Where(a => a.VideoCount > 0 && a.LastPostUnixSeconds == null)
            .Take(MaxDateChecksPerRun)
            .ToList();
        if (targets.Count == 0) return;

        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null)
        {
            _log.Log(T("[Unfollow] Không tìm thấy cookie — sang tab Cookie trước.", "[Unfollow] Cookie not found — go to Cookie tab first."), LogLevel.Error);
            return;
        }

        _isBusy = true;
        UpdateButtonStates();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var gate = new object();
        var done = 0;
        var failStreak = 0;
        var blocked = false;
        var gotDate = 0;

        try
        {
            await EnsurePlaywrightAsync(cookie);
            PlaywrightService.ResetDetailStats();
            _log.Log(T($"[Unfollow] Đang lấy ngày đăng cuối của {targets.Count} kênh (chạy chậm để tránh bị chặn — bấm Stop để dừng)...",
                       $"[Unfollow] Reading last-post dates for {targets.Count} accounts (slow on purpose to avoid blocks — press Stop to cancel)..."), LogLevel.Info);

            var tasks = targets.Select(async account =>
            {
                await _dateCheckGate.WaitAsync(ct);
                try
                {
                    if (blocked) return;
                    ct.ThrowIfCancellationRequested();

                    var detail = await _playwright.GetUserDetailParallelAsync(account.Username, ct);

                    int current;
                    lock (gate)
                    {
                        if (detail is { } d)
                        {
                            account.UserId = d.UserId;
                            account.VideoCount = d.VideoCount;
                            account.LastPostUnixSeconds = d.LastPostUnixSeconds;
                            if (account.Relation < 0 && d.Relation >= 0) account.Relation = d.Relation;
                            if (d.LastPostUnixSeconds is long ts)
                            {
                                _dateCache.Set(account.Username, d.VideoCount, ts);
                                gotDate++;
                            }

                            failStreak = 0;
                        }
                        else if (++failStreak >= DateCheckBlockStreak)
                        {
                            blocked = true;
                        }

                        current = ++done;
                    }

                    if (current % 10 == 0)
                    {
                        _log.Log(T($"[Unfollow] Đã kiểm tra {current}/{targets.Count} kênh ({gotDate} có ngày)...",
                                   $"[Unfollow] Checked {current}/{targets.Count} accounts ({gotDate} with a date)..."), LogLevel.Info);
                    }

                    await Task.Delay(Random.Shared.Next(1500, 3000), ct);
                }
                finally
                {
                    _dateCheckGate.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            if (blocked)
            {
                _log.Log(T("[Unfollow] ⚠ TikTok có vẻ đang chặn (nhiều kênh liên tiếp không đọc được) — đã dừng. Phần đã lấy được đã lưu; đợi một lúc rồi bấm lại.",
                           "[Unfollow] ⚠ TikTok seems to be blocking (several accounts in a row unreadable) — stopped. What was read is saved; wait a while then try again."), LogLevel.Warn);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Log(T("[Unfollow] Đã dừng theo yêu cầu.", "[Unfollow] Stopped by user."), LogLevel.Warn);
        }
        catch (Exception ex)
        {
            _log.Log(T($"[Unfollow] Lỗi khi lấy ngày đăng: {ex.Message}", $"[Unfollow] Failed to read dates: {ex.Message}"), LogLevel.Error);
        }
        finally
        {
            _dateCache.Save();
            _log.Log($"[Unfollow][DBG] Profile visit results: {PlaywrightService.DescribeDetailStats()}", LogLevel.Info);
            _isBusy = false;
            _cts?.Dispose();
            _cts = null;
            RecomputeDeadStatus();
            _sessionStateService.SaveSession(_accounts, _accounts.Count);
            RebuildList();
            UpdateHeaderBadge();
            var left = _accounts.Count(a => a.VideoCount > 0 && a.LastPostUnixSeconds == null);
            _log.Log(T($"[Unfollow] Xong đợt này: {gotDate} kênh có ngày mới, còn {left} kênh chưa biết ngày.",
                       $"[Unfollow] Batch done: {gotDate} new dates, {left} accounts still unknown."), LogLevel.Ok);
        }
    }

    /// <summary>Shows channel avatars: reuses files already cached on disk, and downloads the rest in the
    /// background (4 at a time). TikTok's avatar links expire, so this runs right after the list loads.</summary>
    private void StartAvatarDownloads()
    {
        var toDownload = new List<FollowingAccount>();
        foreach (var account in _accounts)
        {
            if (account.AvatarPath is not null && File.Exists(account.AvatarPath))
            {
                continue;
            }

            account.AvatarPath = AvatarCache.CachedPath(account.Username);
            if (account.AvatarPath is not null)
            {
                RefreshRow(account);
            }
            else if (!string.IsNullOrEmpty(account.AvatarUrl))
            {
                toDownload.Add(account);
            }
        }

        if (toDownload.Count == 0)
        {
            return;
        }

        _log.Log(T($"[Unfollow] Đang tải logo {toDownload.Count} kênh ở nền...", $"[Unfollow] Downloading {toDownload.Count} channel logos in the background..."), LogLevel.Info);

        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(4);
            var ok = 0;
            await Task.WhenAll(toDownload.Select(async account =>
            {
                await gate.WaitAsync();
                try
                {
                    var path = await AvatarCache.DownloadAsync(account.Username, account.AvatarUrl!, CancellationToken.None);
                    if (path is not null)
                    {
                        account.AvatarPath = path;
                        Interlocked.Increment(ref ok);
                        RefreshRow(account);
                    }
                }
                catch
                {
                    // one failed logo must never affect the others
                }
                finally
                {
                    gate.Release();
                }
            }));

            _log.Log(T($"[Unfollow] Logo: tải được {ok}/{toDownload.Count} kênh.", $"[Unfollow] Logos: {ok}/{toDownload.Count} downloaded."),
                ok > 0 ? LogLevel.Ok : LogLevel.Warn);
        });
    }

    /// <summary>Recomputes FollowingAccount.IsDead for every loaded account according to the
    /// currently selected "Tiêu chí kênh chết" combo — a channel that never posted (0 video) is
    /// always dead regardless of the selected day threshold; otherwise dead means the most
    /// recent post is older than that threshold (unknown last-post date never counts as dead).</summary>
    private void RecomputeDeadStatus()
    {
        var thresholdDays = CriteriaThresholdDays[_criteriaCombo.SelectedIndex];
        foreach (var account in _accounts)
        {
            account.IsDead = account.VideoCount == 0
                || (thresholdDays is int days && account.DaysSinceLastPost is int since && since >= days);
        }
    }

    private void RebuildList()
    {
        _listFlow.SuspendLayout();
        _listFlow.Controls.Clear();
        _rows.Clear();

        var visible = GetVisibleAccounts();
        foreach (var account in visible)
        {
            var row = new FollowingItemControl(account, ContentWidth);
            row.SelectionChanged += UpdateActionInfo;
            row.WhitelistToggled += (acc, add) =>
            {
                if (add) _sessionStateService.AddToWhitelist(acc.Username);
                else _sessionStateService.RemoveFromWhitelist(acc.Username);
                _log.Log(add
                    ? T($"[Unfollow] 🔒 Đã thêm @{acc.Username} vào whitelist.", $"[Unfollow] 🔒 Added @{acc.Username} to whitelist.")
                    : T($"[Unfollow] 🔓 Đã xóa @{acc.Username} khỏi whitelist.", $"[Unfollow] 🔓 Removed @{acc.Username} from whitelist."),
                    LogLevel.Info);
                UpdateActionInfo();
            };
            _rows[account] = row;
            _listFlow.Controls.Add(row);
        }

        _listFlow.ResumeLayout(true);
        UpdateSectionLabel(visible.Count);
        UpdateActionInfo();
    }

    /// <summary>The accounts currently shown: view filter, then name search, then sort.</summary>
    private List<FollowingAccount> GetVisibleAccounts()
    {
        IEnumerable<FollowingAccount> q = _accounts;

        switch (_viewCombo.SelectedIndex)
        {
            case 1: q = q.Where(a => a.IsDead); break;
            case 2: q = q.Where(a => !a.IsDead); break;
            case 3: q = q.Where(a => a.IsSelected); break;
        }

        var term = _searchBox.Text.Trim().TrimStart('@');
        if (term.Length > 0)
        {
            q = q.Where(a => a.Username.Contains(term, StringComparison.OrdinalIgnoreCase)
                          || a.Nickname.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        switch (_sortCombo.SelectedIndex)
        {
            case 1: // never posted first, then oldest last post, unknown dates last
                q = q.OrderBy(a => a.VideoCount == 0 ? 0 : a.DaysSinceLastPost is not null ? 1 : 2)
                     .ThenByDescending(a => a.DaysSinceLastPost ?? -1);
                break;
            case 2:
                q = q.OrderBy(a => a.VideoCount < 0 ? int.MaxValue : a.VideoCount);
                break;
            case 3:
                q = q.OrderBy(a => string.IsNullOrEmpty(a.Nickname) ? a.Username : a.Nickname, StringComparer.OrdinalIgnoreCase);
                break;
        }

        return q.ToList();
    }

    private void SelectAllShown()
    {
        var skipFriends = _settingsService.Current.SkipFriends;
        var count = 0;
        foreach (var account in GetVisibleAccounts().Where(a => !a.IsWhitelisted && a.Status != UnfollowStatus.Done && !(skipFriends && a.IsFriend)))
        {
            account.IsSelected = true;
            count++;
        }

        RebuildList();
        _log.Log(T($"[Unfollow] Đã chọn {count} kênh đang hiển thị.", $"[Unfollow] Selected {count} shown channels."), LogLevel.Info);
    }

    private void SelectAllDead()
    {
        var skipFriends = _settingsService.Current.SkipFriends;
        foreach (var account in _accounts.Where(a => a.IsDead && !a.IsWhitelisted && !(skipFriends && a.IsFriend)))
        {
            account.IsSelected = true;
            if (_rows.TryGetValue(account, out var row))
            {
                row.RefreshStatus();
            }
        }

        RebuildList();
        var whitelistCount = _accounts.Count(a => a.IsDead && a.IsWhitelisted);
        var friendCount    = skipFriends ? _accounts.Count(a => a.IsDead && !a.IsWhitelisted && a.IsFriend) : 0;
        var selectedCount  = _accounts.Count(a => a.IsDead && !a.IsWhitelisted && !(skipFriends && a.IsFriend));
        var extras = new System.Text.StringBuilder();
        if (whitelistCount > 0) extras.Append(T($", bỏ qua {whitelistCount} whitelist", $", {whitelistCount} whitelisted skipped"));
        if (friendCount > 0)    extras.Append(T($", bỏ qua {friendCount} follow lại", $", {friendCount} mutual skipped"));
        _log.Log(T($"[Unfollow] Đã chọn {selectedCount} kênh chết{extras}.",
                   $"[Unfollow] Selected {selectedCount} dead channels{extras}."), LogLevel.Info);
    }

    private void ClearSelection()
    {
        foreach (var account in _accounts)
        {
            account.IsSelected = false;
        }

        RebuildList();
    }

    private async Task StartOrStopUnfollowAsync()
    {
        if (_isBusy)
        {
            _cts?.Cancel();
            return;
        }

        var settings = _settingsService.Current;

        // ── Daily quota check ─────────────────────────────────────────────
        var unfollowedToday = _sessionStateService.GetUnfollowedToday();
        if (unfollowedToday >= settings.MaxUnfollowsPerDay)
        {
            _log.Log(T($"[Unfollow] Đã đạt giới hạn {settings.MaxUnfollowsPerDay} lần unfollow hôm nay — thử lại ngày mai.",
                        $"[Unfollow] Reached daily limit of {settings.MaxUnfollowsPerDay} unfollows today — try again tomorrow."), LogLevel.Warn);
            return;
        }
        var remainingQuota = settings.MaxUnfollowsPerDay - unfollowedToday;

        var sessionLimit = Math.Min(settings.UnfollowSessionLimit, remainingQuota);
        // Bỏ qua whitelisted và mutual-follow (friends) kể cả khi đã tick
        var selected = _accounts
            .Where(a => a.IsSelected && !a.IsWhitelisted && a.Status != UnfollowStatus.Done
                     && !(settings.SkipFriends && a.IsFriend))
            .Take(sessionLimit)
            .ToList();

        if (selected.Count == 0)
        {
            var whitelistBlocked = _accounts.Count(a => a.IsSelected && a.IsWhitelisted);
            var friendBlocked    = settings.SkipFriends ? _accounts.Count(a => a.IsSelected && a.IsFriend && !a.IsWhitelisted) : 0;
            if (whitelistBlocked > 0)
                _log.Log(T($"[Unfollow] {whitelistBlocked} kênh đã tick nhưng đang trong whitelist — không unfollow.",
                           $"[Unfollow] {whitelistBlocked} selected accounts are whitelisted — skipping."), LogLevel.Warn);
            else if (friendBlocked > 0)
                _log.Log(T($"[Unfollow] {friendBlocked} kênh đã tick nhưng là người follow lại (SkipFriends đang bật) — không unfollow.",
                           $"[Unfollow] {friendBlocked} selected accounts follow you back and SkipFriends is on — skipping."), LogLevel.Warn);
            else
                _log.Log(T("[Unfollow] Chưa chọn tài khoản nào để unfollow.", "[Unfollow] No accounts selected to unfollow."), LogLevel.Warn);
            return;
        }

        var skippedByLimit = _accounts.Count(a => a.IsSelected && !a.IsWhitelisted && a.Status != UnfollowStatus.Done) - selected.Count;
        if (skippedByLimit > 0)
        {
            _log.Log(T($"[Unfollow] Giới hạn phiên/ngày: {sessionLimit} tài khoản — {skippedByLimit} tài khoản còn lại sẽ chờ lần chạy sau.",
                        $"[Unfollow] Session/day limit: {sessionLimit} accounts — {skippedByLimit} remaining will wait for next run."), LogLevel.Warn);
        }

        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null)
        {
            _log.Log(T("[Unfollow] Không tìm thấy cookie — sang tab Cookie trước.", "[Unfollow] Cookie not found — go to Cookie tab first."), LogLevel.Error);
            return;
        }

        _isBusy = true;
        UpdateButtonStates();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var apiFirst = _modeCombo.SelectedIndex == 0;

        _log.Log(T($"[Unfollow] Bắt đầu unfollow {selected.Count} tài khoản ({(apiFirst ? "API trước" : "chỉ trình duyệt")})...",
                    $"[Unfollow] Starting unfollow for {selected.Count} accounts ({(apiFirst ? "API first" : "browser only")})..."), LogLevel.Info);

        try
        {
            if (!apiFirst)
            {
                await EnsurePlaywrightAsync(cookie);
            }

            foreach (var account in selected)
            {
                ct.ThrowIfCancellationRequested();

                account.Status = UnfollowStatus.Unfollowing;
                RefreshRow(account);

                try
                {
                    if (apiFirst && !string.IsNullOrEmpty(account.UserId))
                    {
                        try
                        {
                            await _apiService.UnfollowAsync(account.UserId, cookie, ct);
                        }
                        catch (RateLimitException rex)
                        {
                            _log.Log($"[Unfollow] {rex.Message}", LogLevel.Warn);

                            if (settings.AutoWaitOnRateLimit)
                            {
                                _log.Log(T($"[Unfollow] Tự động đợi {settings.RateLimitWaitSeconds}s rồi tiếp tục...",
                                           $"[Unfollow] Auto-waiting {settings.RateLimitWaitSeconds}s before retrying..."), LogLevel.Info);
                                await Task.Delay(settings.RateLimitWaitSeconds * 1_000, ct);
                            }
                            else if (settings.NotifyOnRateLimit)
                            {
                                var choice = MessageBox.Show(
                                    T($"{rex.Message}\n\nTiếp tục unfollow các tài khoản còn lại?",
                                      $"{rex.Message}\n\nContinue unfollowing the remaining accounts?"),
                                    "Social Toolkit — Rate limit",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                                if (choice == DialogResult.No) throw new OperationCanceledException();
                                await Task.Delay(10_000, ct);
                            }

                            await EnsurePlaywrightAsync(cookie);
                            await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), account.UserId, ct);
                        }
                        catch (Exception)
                        {
                            await EnsurePlaywrightAsync(cookie);
                            await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), account.UserId, ct);
                        }
                    }
                    else
                    {
                        await EnsurePlaywrightAsync(cookie);
                        await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), account.UserId, ct);
                    }

                    account.Status = UnfollowStatus.Done;
                    _log.Log(T($"[Unfollow] ✓ Đã unfollow @{account.Username}", $"[Unfollow] ✓ Unfollowed @{account.Username}"), LogLevel.Ok);
                    _sessionStateService.IncrementUnfollowedToday();
                    _sessionStateService.SaveSession(_accounts, _accounts.Count);

                    await _historyService.AddAsync(new HistoryItem
                    {
                        Type = "Unfollow",
                        Title = $"@{account.Username}",
                        Detail = account.VideoCount == 0 ? T("Kênh chết (0 video)", "Dead channel (0 videos)") : $"{account.VideoCount} video",
                        Success = true,
                    });

                    // Daily quota check after each successful unfollow
                    var todayCount = _sessionStateService.GetUnfollowedToday();
                    if (todayCount >= settings.MaxUnfollowsPerDay)
                    {
                        _log.Log(T($"[Unfollow] Đã đạt giới hạn {settings.MaxUnfollowsPerDay} lần unfollow hôm nay — dừng để bảo vệ tài khoản.",
                                   $"[Unfollow] Reached daily limit of {settings.MaxUnfollowsPerDay} unfollows — stopping to protect account."), LogLevel.Warn);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    account.Status = UnfollowStatus.Pending;
                    RefreshRow(account);
                    throw;
                }
                catch (Exception ex)
                {
                    account.Status = UnfollowStatus.Failed;
                    account.ErrorMessage = ex.Message;
                    _log.Log(T($"[Unfollow] ✗ Lỗi @{account.Username}: {ex.Message}", $"[Unfollow] ✗ Error @{account.Username}: {ex.Message}"), LogLevel.Error);
                    await _historyService.AddAsync(new HistoryItem
                    {
                        Type = "Unfollow",
                        Title = $"@{account.Username}",
                        Detail = ex.Message,
                        Success = false,
                    });
                }

                RefreshRow(account);
                UpdateActionInfo();

                var delay = Random.Shared.Next(settings.UnfollowDelayMinMs, Math.Max(settings.UnfollowDelayMinMs + 1, settings.UnfollowDelayMaxMs));
                await Task.Delay(delay, ct);
            }

            // Hoàn tất toàn bộ queue → xóa session resume
            if (selected.All(a => a.Status == UnfollowStatus.Done || a.Status == UnfollowStatus.Failed))
            {
                _sessionStateService.ClearSession();
            }

            _log.Log(T("[Unfollow] Hoàn tất hàng đợi unfollow.", "[Unfollow] Unfollow queue complete."), LogLevel.Ok);
        }
        catch (OperationCanceledException)
        {
            _log.Log(T("[Unfollow] Đã dừng theo yêu cầu.", "[Unfollow] Stopped by user."), LogLevel.Warn);
        }
        finally
        {
            _isBusy = false;
            UpdateButtonStates();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RefreshRow(FollowingAccount account)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshRowCore(account));
        }
        else
        {
            RefreshRowCore(account);
        }
    }

    private void RefreshRowCore(FollowingAccount account)
    {
        if (_rows.TryGetValue(account, out var row))
        {
            row.RefreshStatus();
        }
    }

    private void UpdateSectionLabel(int shown = -1)
    {
        var dead = _accounts.Count(a => a.IsDead);
        var showing = shown >= 0 && shown != _accounts.Count;
        _sectionLabel.Text = T(
            $"{_accounts.Count} kênh theo dõi · {dead} kênh chết" + (showing ? $" · hiện {shown}" : ""),
            $"{_accounts.Count} following · {dead} dead" + (showing ? $" · showing {shown}" : ""));
    }

    private void UpdateActionInfo()
    {
        void Update()
        {
            var selected = _accounts.Count(a => a.IsSelected);
            _actionInfo.Text = T($"{selected} đã chọn", $"{selected} selected");
            UpdateButtonStates();
        }

        if (InvokeRequired)
        {
            BeginInvoke(Update);
        }
        else
        {
            Update();
        }
    }

    private void UpdateHeaderBadge()
    {
        var dead = _accounts.Count(a => a.IsDead);
        _headerBadge.Text = _accounts.Count == 0 ? T("Cần cookie", "Cookie needed") : T($"{dead} kênh chết / {_accounts.Count}", $"{dead} dead / {_accounts.Count}");
        _headerBadge.ForeColor = dead > 0 ? Theme.Orange : Theme.TextMuted;
    }
}
