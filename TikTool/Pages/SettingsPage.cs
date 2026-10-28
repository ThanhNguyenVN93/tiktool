using Guna.UI2.WinForms;
using Microsoft.Win32;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

internal sealed class SettingsPage : UserControl
{
    private const int ContentWidth = 700;
    private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly SettingsService _settingsService;

    private TextBox _outputDirBox = null!;
    private ComboBox _formatCombo = null!;
    private NumericUpDown _concurrentNumeric = null!;
    private Guna2ToggleSwitch _autoUpdateToggle = null!;
    private ComboBox _cookieBrowserCombo = null!;
    private ComboBox _languageCombo = null!;

    private NumericUpDown _delayMinNumeric = null!;
    private NumericUpDown _delayMaxNumeric = null!;
    private NumericUpDown _sessionLimitNumeric = null!;
    private Guna2ToggleSwitch _preferApiToggle = null!;
    private Guna2ToggleSwitch _skipFriendsToggle = null!;
    private Guna2ToggleSwitch _notifyRateLimitToggle = null!;

    private Guna2ToggleSwitch _autoWaitRateLimitToggle = null!;
    private NumericUpDown _rateLimitWaitNumeric = null!;
    private NumericUpDown _maxUnfollowsPerDayNumeric = null!;

    private Guna2ToggleSwitch _startWithWindowsToggle = null!;
    private Guna2ToggleSwitch _minimizeToTrayToggle = null!;
    private Guna2ToggleSwitch _embeddedBrowserToggle = null!;
    private Guna2ToggleSwitch _runHiddenToggle = null!;
    private Guna2ToggleSwitch _checkUpdatesToggle = null!;
    private Guna2ToggleSwitch _autoInstallToggle = null!;

    private Label _savedInfo = null!;

    public SettingsPage(SettingsService settingsService)
    {
        _settingsService = settingsService;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
        LoadFromSettings();
    }

    private void BuildUi()
    {
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

        header.Controls.Add(new Label
        {
            Text = T("Cài đặt", "Settings"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        });
        header.Controls.Add(new Label
        {
            Text = T("Tuỳ chỉnh hành vi tải video, bỏ theo dõi và khởi động ứng dụng",
                     "Customize download, unfollow and startup behavior"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        });

        var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };
        header.Controls.Add(sep);
        return header;
    }

    private Control BuildBody()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(20, 16, 20, 16),
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

        flow.Controls.Add(BuildGroup(T("TẢI VIDEO", "DOWNLOAD"), BuildDownloadRows()));
        flow.Controls.Add(BuildGroup(T("BỎ THEO DÕI", "UNFOLLOW"), BuildUnfollowRows()));
        flow.Controls.Add(BuildGroup(T("TRÌNH DUYỆT", "BROWSER"), BuildBrowserRows()));
        flow.Controls.Add(BuildGroup(T("CẬP NHẬT", "UPDATES"), BuildUpdateRows()));
        flow.Controls.Add(BuildGroup(T("NHẬT KÝ", "LOGS"), BuildLogRows()));
        flow.Controls.Add(BuildGroup(T("GIAO DIỆN && HỆ THỐNG", "INTERFACE && SYSTEM"), BuildAppRows()));
        flow.Controls.Add(BuildGroup(T("NGÔN NGỮ", "LANGUAGE"), BuildLanguageRows()));

        scroll.Controls.Add(flow);
        return scroll;
    }

    private Control BuildGroup(string title, IReadOnlyList<Control> rows)
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true, Margin = new Padding(0, 0, 0, 20) };

        var label = new Label
        {
            Text = title,
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var card = new Guna2Panel
        {
            Width = ContentWidth,
            AutoSize = true,
            BorderRadius = 8,
            FillColor = Theme.Panel,
            BorderColor = Theme.Border,
            BorderThickness = 1,
            Padding = new Padding(4, 0, 4, 0),
        };

        var rowsFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = ContentWidth - 8,
        };
        foreach (var row in rows)
        {
            rowsFlow.Controls.Add(row);
        }

        card.Controls.Add(rowsFlow);

        var host = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = ContentWidth,
        };
        host.Controls.Add(label);
        host.Controls.Add(card);

        wrap.Controls.Add(host);
        return wrap;
    }

    private List<Control> BuildDownloadRows()
    {
        _outputDirBox = new TextBox
        {
            Width = 300,
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontRegular,
        };
        var btnChoose = new Guna2Button
        {
            Text = T("Chọn...", "Browse..."),
            Font = Theme.FontMedium,
            Size = new Size(80, 28),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 6,
            Cursor = Cursors.Hand,
        };
        btnChoose.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_outputDirBox.Text) ? _outputDirBox.Text : "" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _outputDirBox.Text = dialog.SelectedPath;
            }
        };
        var outputDirControl = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        outputDirControl.Controls.Add(_outputDirBox);
        outputDirControl.Controls.Add(btnChoose);

        _formatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, FlatStyle = FlatStyle.Flat, BackColor = ColorTranslator.FromHtml("#161616"), ForeColor = Theme.TextPrimary };
        _formatCombo.Items.AddRange(new object[] { "mp4", "mp3", "mkv" });

        _concurrentNumeric = BuildNumeric(1, 10);
        _autoUpdateToggle = BuildToggle();

        _cookieBrowserCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, FlatStyle = FlatStyle.Flat, BackColor = ColorTranslator.FromHtml("#161616"), ForeColor = Theme.TextPrimary };
        _cookieBrowserCombo.Items.AddRange(new object[] { "chrome", "edge", "firefox", "none" });

        return new List<Control>
        {
            BuildRow("📁", T("Thư mục lưu mặc định","Default output folder"), T("Nơi lưu video khi tải xong","Where downloaded videos are saved"), outputDirControl),
            BuildRow("🎬", T("Định dạng mặc định","Default format"), T("Format được chọn sẵn khi thêm URL mới","Pre-selected format when adding a new URL"), _formatCombo),
            BuildRow("⚡", T("Số lượt tải song song","Concurrent downloads"), T("Số video tải cùng lúc (2–3 là tối ưu trên mạng chậm/4G)","Videos downloading at once (2–3 optimal on slow/4G)"), _concurrentNumeric),
            BuildRow("🔄", T("Tự động cập nhật yt-dlp","Auto-update yt-dlp"), T("Kiểm tra bản mới mỗi khi mở app","Check for new version on startup"), _autoUpdateToggle),
            BuildRow("🍪", T("Trình duyệt lấy cookie","Cookie browser"), T("yt-dlp đọc cookie từ trình duyệt để tải Instagram/Facebook","yt-dlp reads cookies from browser for Instagram/Facebook"), _cookieBrowserCombo),
        };
    }

    private List<Control> BuildUnfollowRows()
    {
        _delayMinNumeric = BuildNumeric(500, 60_000, 500);
        _delayMaxNumeric = BuildNumeric(500, 60_000, 500);
        var delayControl = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        delayControl.Controls.Add(_delayMinNumeric);
        delayControl.Controls.Add(new Label { Text = "–", ForeColor = Theme.TextMuted, AutoSize = true, Margin = new Padding(6, 6, 6, 0) });
        delayControl.Controls.Add(_delayMaxNumeric);
        delayControl.Controls.Add(new Label { Text = "ms", ForeColor = Theme.TextMuted, AutoSize = true, Margin = new Padding(4, 6, 0, 0) });

        _sessionLimitNumeric = BuildNumeric(1, 500);
        _preferApiToggle = BuildToggle();
        _skipFriendsToggle = BuildToggle();
        _notifyRateLimitToggle = BuildToggle();
        _autoWaitRateLimitToggle = BuildToggle();
        _rateLimitWaitNumeric = BuildNumeric(10, 600);
        _maxUnfollowsPerDayNumeric = BuildNumeric(1, 1000);

        var waitControl = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        waitControl.Controls.Add(_rateLimitWaitNumeric);
        waitControl.Controls.Add(new Label { Text = T("giây", "seconds"), ForeColor = Theme.TextMuted, AutoSize = true, Margin = new Padding(4, 6, 0, 0) });

        return new List<Control>
        {
            BuildRow("⏱", T("Delay giữa mỗi lượt bỏ theo dõi","Delay between unfollows"), T("Chờ ngẫu nhiên trong khoảng này để tránh bị TikTok khoá","Random wait to avoid TikTok rate-limiting"), delayControl),
            BuildRow("🔢", T("Giới hạn mỗi phiên","Session limit"), T("Số tài khoản tối đa bỏ theo dõi trong 1 lần bấm Bắt đầu","Max accounts to unfollow per Start click"), _sessionLimitNumeric),
            BuildRow("📅", T("Giới hạn mỗi ngày","Daily limit"), T("Tổng số unfollow tối đa trong 1 ngày — để bảo vệ tài khoản","Max total unfollows per day — to protect your account"), _maxUnfollowsPerDayNumeric),
            BuildRow("🌐", T("Ưu tiên API trước Playwright","Prefer API over browser"), T("Nhanh hơn; tự fallback sang trình duyệt khi API lỗi","Faster; auto-falls back to browser on API error"), _preferApiToggle),
            BuildRow("🤝", T("Bỏ qua bạn bè (follow qua lại)","Skip mutual follows"), T("Không unfollow tài khoản đang follow lại mình","Don't unfollow accounts who follow you back"), _skipFriendsToggle),
            BuildRow("⚡", T("Tự động đợi khi bị rate limit","Auto-wait on rate limit"), T("Đợi rồi tiếp tục thay vì hiện hộp thoại hỏi","Wait and retry instead of showing a dialog"), _autoWaitRateLimitToggle),
            BuildRow("⏳", T("Thời gian đợi rate limit","Rate limit wait time"), T("Số giây tự động đợi khi TikTok giới hạn tần suất","Seconds to auto-wait when TikTok rate-limits"), waitControl),
            BuildRow("🔔", T("Cảnh báo khi bị rate limit","Notify on rate limit"), T("Hiện hộp thoại hỏi (chỉ dùng khi tắt tự động đợi)","Show dialog (only used when Auto-wait is off)"), _notifyRateLimitToggle),
        };
    }

    private List<Control> BuildBrowserRows()
    {
        _embeddedBrowserToggle = BuildToggle();
        _runHiddenToggle = BuildToggle();

        return new List<Control>
        {
            BuildRow("🌐", T("Dùng trình duyệt tích hợp (thử nghiệm)", "Use the built-in browser (beta)"),
                T("Không cần Chrome: TikTok và YouTube chạy trong cửa sổ của app (cần WebView2, có sẵn trên Windows 11). Đăng nhập ở tab Tài khoản. Áp dụng cho lần chạy tiếp theo.",
                  "No Chrome needed: TikTok and YouTube run in an app window (needs WebView2, included with Windows 11). Sign in on the Accounts tab. Applies the next time you run."), _embeddedBrowserToggle),
            BuildRow("🫥", T("Chạy trình duyệt ngầm", "Run the browser in the background"),
                T("Cửa sổ trình duyệt làm việc ẩn đi (vẫn chạy bình thường). Có nút nhỏ ở thanh dưới của trang Bỏ theo dõi / YouTube để hiện lại. Áp dụng cho lần chạy tiếp theo.",
                  "The working browser window stays hidden (it works the same). A small button on the bottom bar of the Unfollow / YouTube pages shows it again. Applies the next time you run."), _runHiddenToggle),
        };
    }
    private List<Control> BuildUpdateRows()
    {
        _checkUpdatesToggle = BuildToggle();
        _autoInstallToggle = BuildToggle();
        var check = new Guna2Button
        {
            Text = T("Kiểm tra ngay", "Check now"),
            Font = Theme.FontMedium,
            Size = new Size(130, 28),
            FillColor = ColorTranslator.FromHtml("#2a2a2a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        check.Click += async (_, _) => await CheckForUpdateNowAsync(check);

        return new List<Control>
        {
            BuildRow("🔔", T("Kiểm tra bản mới khi mở app", "Check for updates when the app starts"),
                T("Hỏi GitHub xem có bản phát hành mới không. Chỉ một yêu cầu, không gửi dữ liệu của bạn.",
                  "Asks GitHub whether a newer release exists. One request; none of your data is sent."), _checkUpdatesToggle),
            BuildRow("⬇️", T("Tự động cài bản mới", "Install updates automatically"),
                T("Tải bản mới ở nền, kiểm tra SHA256, rồi cài vào lần mở app kế tiếp. Tắt thì app chỉ báo và bạn bấm Cập nhật.",
                  "Downloads a new version in the background, checks its SHA256 and installs it the next time the app starts. Off: the app only tells you and you press Update."), _autoInstallToggle),
            BuildRow("ℹ️", T($"Phiên bản hiện tại: v{AppVersion.Text}", $"Current version: v{AppVersion.Text}"),
                T("Xem ngay có bản mới không.", "Look for a newer version now."), check),
        };
    }

    private async Task CheckForUpdateNowAsync(Guna2Button button)
    {
        button.Enabled = false;
        try
        {
            var status = await UpdateController.CheckAsync(autoDownload: false);
            var message = status switch
            {
                UpdateStatus.Available or UpdateStatus.Ready =>
                    T($"Có bản mới {UpdateController.Info?.Tag}. Dải thông báo ở đầu cửa sổ có nút Cập nhật.",
                      $"A new version {UpdateController.Info?.Tag} is available. The bar at the top of the window has the Update button."),
                _ when UpdateController.LastCheckError is { } err => T($"Không kiểm tra được: {err}", $"Could not check: {err}"),
                _ => T($"Bạn đang dùng bản mới nhất (v{AppVersion.Text}).", $"You are on the latest version (v{AppVersion.Text})."),
            };
            MessageBox.Show(this, message, "Social Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private List<Control> BuildLogRows()
    {
        var open = new Guna2Button
        {
            Text = T("Mở thư mục", "Open folder"),
            Font = Theme.FontMedium,
            Size = new Size(130, 28),
            FillColor = ColorTranslator.FromHtml("#2a2a2a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AppPaths.LogDir, UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write("WARN", "Could not open the log folder: " + ex.Message); }
        };

        return new List<Control>
        {
            BuildRow("📂", T("Thư mục nhật ký", "Log folder"),
                T("tiktok.log, youtube.log, challenges.log (captcha), update.log, download.log, errors.log. Chỉ nằm trên máy bạn; xem qua trước khi gửi cho ai.",
                  "tiktok.log, youtube.log, challenges.log (captchas), update.log, download.log, errors.log. They stay on this computer; look them over before sending them to anyone."), open),
        };
    }

    private List<Control> BuildAppRows()
    {
        _startWithWindowsToggle = BuildToggle();
        _minimizeToTrayToggle = BuildToggle();

        return new List<Control>
        {
            BuildRow("🖥", T("Khởi động cùng Windows","Start with Windows"), T("Tự mở Social Toolkit khi đăng nhập máy","Auto-launch Social Toolkit on login"), _startWithWindowsToggle),
            BuildRow("🔽", T("Thu nhỏ xuống khay hệ thống","Minimize to tray"), T("Đóng cửa sổ sẽ ẩn app xuống system tray thay vì thoát","Closing the window hides the app to the system tray"), _minimizeToTrayToggle),
        };
    }

    private List<Control> BuildLanguageRows()
    {
        _languageCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, FlatStyle = FlatStyle.Flat, BackColor = ColorTranslator.FromHtml("#161616"), ForeColor = Theme.TextPrimary };
        _languageCombo.Items.AddRange(new object[] { "Tiếng Việt (vi)", "English (en)" });

        return new List<Control>
        {
            BuildRow("🌍", T("Ngôn ngữ giao diện","Interface language"), T("Ứng dụng sẽ tự khởi động lại khi thay đổi","App will restart automatically on change"), _languageCombo),
        };
    }

    private static NumericUpDown BuildNumeric(int min, int max, int increment = 1) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = increment,
        Width = 90,
        BackColor = ColorTranslator.FromHtml("#161616"),
        ForeColor = Theme.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static Guna2ToggleSwitch BuildToggle() => new()
    {
        Size = new Size(46, 22),
        CheckedState = { FillColor = Theme.Accent },
        UncheckedState = { FillColor = ColorTranslator.FromHtml("#2a2a2a") },
        Cursor = Cursors.Hand,
    };

    private static Control BuildRow(string icon, string title, string desc, Control control)
    {
        var row = new Panel { Width = ContentWidth - 16, Height = 60, Padding = new Padding(8, 0, 8, 0) };

        var iconLabel = new Label { Text = icon, Font = new Font("Segoe UI Emoji", 14f), Size = new Size(32, 32), Location = new Point(0, 14), TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Panel };
        var titleLabel = new Label { Text = title, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(40, 10), BackColor = Theme.Panel };
        var descLabel = new Label { Text = desc, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(40, 30), MaximumSize = new Size(360, 0), BackColor = Theme.Panel };

        if (control.AutoSize)
        {
            // Composite AutoSize controls (e.g. the output-dir textbox+button row) report a
            // stale/default PreferredSize until they've been through a layout pass with a
            // parent — force one now so positioning below uses the real width.
            control.Size = control.GetPreferredSize(Size.Empty);
        }

        control.Location = new Point(row.Width - control.Width - 8, (row.Height - control.Height) / 2);
        control.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        row.Controls.Add(iconLabel);
        row.Controls.Add(titleLabel);
        row.Controls.Add(descLabel);
        row.Controls.Add(control);

        var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };
        row.Controls.Add(sep);

        return row;
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

        _savedInfo = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.Green,
            AutoSize = true,
            Location = new Point(20, 18),
        };

        var btnSave = new Guna2Button
        {
            Text = T("Lưu cài đặt", "Save settings"),
            Font = Theme.FontMedium,
            Size = new Size(140, 32),
            Location = new Point(560, 12),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnSave.Click += (_, _) => SaveToSettings();

        bar.Controls.Add(_savedInfo);
        bar.Controls.Add(btnSave);
        bar.Controls.Add(sep);
        return bar;
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        _outputDirBox.Text = s.DefaultOutputDir;
        _formatCombo.SelectedItem = s.DefaultFormat;
        if (_formatCombo.SelectedIndex < 0)
        {
            _formatCombo.SelectedIndex = 0;
        }

        _concurrentNumeric.Value = Math.Clamp(s.ConcurrentDownloads, (int)_concurrentNumeric.Minimum, (int)_concurrentNumeric.Maximum);
        _autoUpdateToggle.Checked = s.AutoUpdateYtDlp;
        _cookieBrowserCombo.SelectedItem = _cookieBrowserCombo.Items.Contains(s.CookieBrowser) ? s.CookieBrowser : "chrome";
        if (_cookieBrowserCombo.SelectedIndex < 0) _cookieBrowserCombo.SelectedIndex = 0;

        _delayMinNumeric.Value = Math.Clamp(s.UnfollowDelayMinMs, (int)_delayMinNumeric.Minimum, (int)_delayMinNumeric.Maximum);
        _delayMaxNumeric.Value = Math.Clamp(s.UnfollowDelayMaxMs, (int)_delayMaxNumeric.Minimum, (int)_delayMaxNumeric.Maximum);
        _sessionLimitNumeric.Value = Math.Clamp(s.UnfollowSessionLimit, (int)_sessionLimitNumeric.Minimum, (int)_sessionLimitNumeric.Maximum);
        _maxUnfollowsPerDayNumeric.Value = Math.Clamp(s.MaxUnfollowsPerDay, (int)_maxUnfollowsPerDayNumeric.Minimum, (int)_maxUnfollowsPerDayNumeric.Maximum);
        _preferApiToggle.Checked = s.PreferApiOverPlaywright;
        _skipFriendsToggle.Checked = s.SkipFriends;
        _autoWaitRateLimitToggle.Checked = s.AutoWaitOnRateLimit;
        _rateLimitWaitNumeric.Value = Math.Clamp(s.RateLimitWaitSeconds, (int)_rateLimitWaitNumeric.Minimum, (int)_rateLimitWaitNumeric.Maximum);
        _notifyRateLimitToggle.Checked = s.NotifyOnRateLimit;

        _startWithWindowsToggle.Checked = s.StartWithWindows;
        _minimizeToTrayToggle.Checked = s.MinimizeToTray;
        _embeddedBrowserToggle.Checked = s.UseEmbeddedBrowser;
        _runHiddenToggle.Checked = s.RunBrowserHidden;
        _checkUpdatesToggle.Checked = s.CheckForUpdates;
        _autoInstallToggle.Checked = s.AutoInstallUpdates;

        _languageCombo.SelectedIndex = s.Language == "en" ? 1 : 0;
    }

    private void SaveToSettings()
    {
        var min = (int)_delayMinNumeric.Value;
        var max = (int)_delayMaxNumeric.Value;
        if (min > max)
        {
            (min, max) = (max, min);
        }

        var s = _settingsService.Current;
        s.DefaultOutputDir = _outputDirBox.Text.Trim();
        s.DefaultFormat = _formatCombo.SelectedItem?.ToString() ?? "mp4";
        s.ConcurrentDownloads = (int)_concurrentNumeric.Value;
        s.AutoUpdateYtDlp = _autoUpdateToggle.Checked;
        s.CookieBrowser = _cookieBrowserCombo.SelectedItem?.ToString() ?? "chrome";

        s.UnfollowDelayMinMs = min;
        s.UnfollowDelayMaxMs = max;
        s.UnfollowSessionLimit = (int)_sessionLimitNumeric.Value;
        s.MaxUnfollowsPerDay = (int)_maxUnfollowsPerDayNumeric.Value;
        s.PreferApiOverPlaywright = _preferApiToggle.Checked;
        s.SkipFriends = _skipFriendsToggle.Checked;
        s.AutoWaitOnRateLimit = _autoWaitRateLimitToggle.Checked;
        s.RateLimitWaitSeconds = (int)_rateLimitWaitNumeric.Value;
        s.NotifyOnRateLimit = _notifyRateLimitToggle.Checked;

        s.StartWithWindows = _startWithWindowsToggle.Checked;
        s.MinimizeToTray = _minimizeToTrayToggle.Checked;
        s.UseEmbeddedBrowser = _embeddedBrowserToggle.Checked;
        s.RunBrowserHidden = _runHiddenToggle.Checked;
        s.CheckForUpdates = _checkUpdatesToggle.Checked;
        s.AutoInstallUpdates = _autoInstallToggle.Checked;

        var newLang = _languageCombo.SelectedIndex == 1 ? "en" : "vi";
        var langChanged = newLang != s.Language;
        s.Language = newLang;

        _settingsService.Save();
        ApplyStartWithWindows(s.StartWithWindows);

        if (langChanged)
        {
            _savedInfo.Text = T("✓ Đã lưu — đang khởi động lại…", "✓ Saved — restarting…");
            // Delay để user thấy message trước khi restart
            var timer = new System.Windows.Forms.Timer { Interval = 800 };
            timer.Tick += (_, _) => { timer.Stop(); Application.Restart(); };
            timer.Start();
        }
        else
        {
            _savedInfo.Text = T($"✓ Đã lưu lúc {DateTime.Now:HH:mm:ss}", $"✓ Saved at {DateTime.Now:HH:mm:ss}");
        }
    }

    private static void ApplyStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (enabled)
            {
                key?.SetValue("TikTool", $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key?.DeleteValue("TikTool", throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best effort — thiếu quyền registry không nên chặn việc lưu các cài đặt khác.
        }
    }
}
