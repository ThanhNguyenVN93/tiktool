using System.Diagnostics;
using Guna.UI2.WinForms;
using TikTool.Pages;
using TikTool.Services;

namespace TikTool.Forms;

public sealed class MainForm : Form
{
    private readonly Panel _contentPanel;
    private readonly List<NavItem> _navItems = new();
    private readonly Dictionary<string, UserControl> _pages = new();
    private string _activeKey = "download";

    private readonly UpdateService _updateService;
    private readonly SettingsService _settingsServiceRef;
    private readonly YtDlpService _ytDlpServiceRef;
    private readonly CookieService _cookieServiceRef;
    private readonly PlaywrightService _playwrightServiceRef;
    private NotifyIcon? _trayIcon;

    // "Unfollow" is a collapsible group: clicking the parent only opens/closes it, the pages are the children.
    private Guna2Button _unfollowParent = null!;
    private FlowLayoutPanel _unfollowChildren = null!;
    private bool _unfollowExpanded;
    private YouTubePage? _youTubePage;
    private const string TikTokUnfollowKey = "unfollow";
    private const string YouTubeUnsubKey = "youtube-unsub";

    // Footer status row refs — updated async after Load
    private Label _lblYtDlp = null!;
    private Guna2Panel _dotYtDlp = null!;
    private Label _lblChrome = null!;
    private Guna2Panel _dotChrome = null!;
    private Label _lblCookie = null!;
    private Guna2Panel _dotCookie = null!;

    // app updates (see UpdateController): a bar above the pages, shown only when there is something to say
    private Panel _updateBar = null!;
    private Label _updateText = null!;
    private Guna2Button _btnUpdateNow = null!;
    private Guna2Button _btnUpdateInfo = null!;
    private Guna2Button _btnUpdateLater = null!;
    private bool _updateDismissed;

    public MainForm()
    {
        Text = "Social Toolkit";
        Icon = LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1080, 700);
        MinimumSize = new Size(900, 600);
        // The pages use a fixed content width, so a bigger window only adds empty space: keep one size.
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Theme.Background;
        Font = Theme.FontRegular;

        // Tất cả service tạo 1 lần ở đây rồi inject vào các page qua constructor — mỗi page
        // không tự new() service của mình, để AccountsPage/UnfollowPage cùng chia sẻ đúng 1
        // CookieService, và SettingsPage/HistoryPage thao tác trên cùng dữ liệu mà
        // UnfollowPage/DownloadPage đọc/ghi.
        var settingsService = new SettingsService();
        _settingsServiceRef = settingsService;
        var cookieService = new CookieService();
        _cookieServiceRef = cookieService;
        var ytDlpService = new YtDlpService();
        _ytDlpServiceRef = ytDlpService;
        var playwrightService = new PlaywrightService();
        _playwrightServiceRef = playwrightService;
        var tikTokApiService = new TikTokApiService();
        var historyService = new HistoryService();
        var sessionStateService = new SessionStateService();
        _updateService = new UpdateService(ytDlpService);

        var sidebar = BuildSidebar();

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background
        };

        Controls.Add(_contentPanel);
        Controls.Add(BuildUpdateBar());
        Controls.Add(sidebar);

        RegisterPage("download", new DownloadPage(ytDlpService, historyService, settingsService, cookieService));
        var unfollowPage = new UnfollowPage(playwrightService, tikTokApiService, cookieService, settingsService, historyService, sessionStateService);
        unfollowPage.RequestLogin = OpenAccounts;
        RegisterPage(TikTokUnfollowKey, unfollowPage);
        _youTubePage = new YouTubePage(settingsService, historyService);
        _youTubePage.RequestLogin = OpenAccounts;
        RegisterPage(YouTubeUnsubKey, _youTubePage);
        var accountsPage = new AccountsPage(cookieService, playwrightService, settingsService);
        accountsPage.CookieStatusChanged += () => _ = RefreshFooterAsync();
        RegisterPage("accounts", accountsPage);
        RegisterPage("settings", new SettingsPage(settingsService));
        RegisterPage("history", new HistoryPage(historyService));
        RegisterPage("guide", new GuidePage());

        NavigateTo("download");

        Load += (_, _) =>
        {
            InitTrayIcon();
            _ = RefreshFooterAsync();

            if (settingsService.Current.CheckForUpdates)
            {
                _ = Task.Run(() => UpdateController.CheckAsync(settingsService.Current.AutoInstallUpdates));
            }

            if (settingsService.Current.AutoUpdateYtDlp)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _updateService.CheckAndUpdateAllAsync();
                    }
                    catch
                    {
                        // Update lỗi (mất mạng, GitHub đổi cấu trúc...) không được crash app.
                    }
                });
            }
        };
    }

    private void InitTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(T("Mở Social Toolkit", "Open Social Toolkit"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Tải video", "Download"), null, (_, _) => OpenFromTray("download"));
        menu.Items.Add(T("TikTok Unfollow", "TikTok Unfollow"), null, (_, _) => OpenFromTray(TikTokUnfollowKey));
        menu.Items.Add(T("YouTube Unsubscribe", "YouTube Unsubscribe"), null, (_, _) => OpenFromTray(YouTubeUnsubKey));
        menu.Items.Add(T("Lịch sử", "History"), null, (_, _) => OpenFromTray("history"));
        menu.Items.Add(T("Cài đặt", "Settings"), null, (_, _) => OpenFromTray("settings"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Thoát", "Exit"), null, (_, _) => { _trayIcon!.Visible = false; Application.Exit(); });

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Social Toolkit",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    internal static Icon LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }

    // ------------------------------------------------------------------------------------------ app updates

    private Panel BuildUpdateBar()
    {
        _updateBar = new Panel { Dock = DockStyle.Top, Height = 44, Visible = false, BackColor = ColorTranslator.FromHtml("#1b2338") };
        _updateText = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(16, 0),
            Height = 44,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            Font = Theme.FontMedium,
            BackColor = Color.Transparent,
        };
        _btnUpdateNow = MakeUpdateButton(Theme.Accent, Color.White, 150);
        _btnUpdateInfo = MakeUpdateButton(ColorTranslator.FromHtml("#2a2a2a"), Theme.TextSecondary, 150);
        _btnUpdateInfo.Text = T("Có gì mới / tải tay", "What's new / manual");
        _btnUpdateLater = MakeUpdateButton(ColorTranslator.FromHtml("#2a2a2a"), Theme.TextSecondary, 90);
        _btnUpdateLater.Text = T("Để sau", "Later");

        _btnUpdateNow.Click += async (_, _) => await OnUpdateNowAsync();
        _btnUpdateInfo.Click += (_, _) =>
        {
            if (UpdateController.Info is { } info)
            {
                OpenUrl(info.PageUrl);
            }
        };
        _btnUpdateLater.Click += (_, _) =>
        {
            _updateDismissed = true;
            RefreshUpdateBar();
        };

        _updateBar.Controls.AddRange(new Control[] { _updateText, _btnUpdateNow, _btnUpdateInfo, _btnUpdateLater });
        _updateBar.Resize += (_, _) => LayoutUpdateBar();
        UpdateController.Changed += () =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(RefreshUpdateBar);
            }
        };
        return _updateBar;
    }

    private static Guna2Button MakeUpdateButton(Color fill, Color fore, int width) => new()
    {
        Size = new Size(width, 28),
        Font = Theme.FontMedium,
        FillColor = fill,
        ForeColor = fore,
        BorderRadius = 7,
        Cursor = Cursors.Hand,
        Visible = false,
    };

    private void LayoutUpdateBar()
    {
        var right = _updateBar.Width - 16;
        foreach (var button in new[] { _btnUpdateLater, _btnUpdateInfo, _btnUpdateNow })
        {
            if (!button.Visible)
            {
                continue;
            }

            right -= button.Width;
            button.Location = new Point(right, 8);
            right -= 8;
        }

        _updateText.Width = Math.Max(100, right - 16);
    }

    private void RefreshUpdateBar()
    {
        var status = UpdateController.Status;
        var info = UpdateController.Info;
        if (status == UpdateStatus.Checking)
        {
            _updateDismissed = false; // a new check (for example "Check now") brings the bar back
        }

        var show = !_updateDismissed && info is not null && status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Ready or UpdateStatus.Failed;
        _updateBar.Visible = show;
        if (!show || info is null)
        {
            return;
        }

        var auto = _settingsServiceRef.Current.AutoInstallUpdates;
        switch (status)
        {
            case UpdateStatus.Available:
                _updateText.Text = T($"Có bản mới {info.Tag} (bạn đang dùng v{AppVersion.Text}).", $"A new version {info.Tag} is available (you have v{AppVersion.Text}).");
                _btnUpdateNow.Text = T("Tải về", "Download");
                break;
            case UpdateStatus.Downloading:
                _updateText.Text = T($"Đang tải {info.Tag}… {UpdateController.Progress:P0}", $"Downloading {info.Tag}… {UpdateController.Progress:P0}");
                break;
            case UpdateStatus.Ready:
                _updateText.Text = auto
                    ? T($"{info.Tag} đã tải xong và được kiểm tra. Sẽ tự cài vào lần mở app kế tiếp, hoặc cài ngay.", $"{info.Tag} is downloaded and verified. It installs the next time you open the app, or install it now.")
                    : T($"{info.Tag} đã tải xong và được kiểm tra. Cài bây giờ?", $"{info.Tag} is downloaded and verified. Install it now?");
                _btnUpdateNow.Text = T("Khởi động lại && cập nhật", "Restart && update");
                break;
            default:
                _updateText.Text = T($"Không cập nhật được {info.Tag}: {UpdateController.Error}", $"Could not update to {info.Tag}: {UpdateController.Error}");
                _btnUpdateNow.Text = T("Thử lại", "Retry");
                break;
        }

        var working = status == UpdateStatus.Downloading;
        _btnUpdateNow.Visible = !working;
        _btnUpdateInfo.Visible = !working;
        _btnUpdateLater.Visible = !working;
        LayoutUpdateBar();
    }

    private async Task OnUpdateNowAsync()
    {
        try
        {
            if (UpdateController.Status != UpdateStatus.Ready)
            {
                // Download only; installing is a second, explicit step once the user sees it is ready.
                await UpdateController.DownloadAsync();
                return;
            }

            if (UpdateController.Info is not { } info)
            {
                return;
            }

            var running = _playwrightServiceRef.IsLaunched
                ? T("\n\nMột công việc đang chạy sẽ dừng (tiến độ đã được lưu).", "\n\nA running job will stop (its progress is saved).")
                : "";
            var ask = MessageBox.Show(
                this,
                T($"Cập nhật lên {info.Tag} bây giờ? App sẽ tự khởi động lại.{running}", $"Update to {info.Tag} now? The app restarts by itself.{running}"),
                T("Social Toolkit — Cập nhật", "Social Toolkit — Update"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes)
            {
                return;
            }

            _updateText.Text = T("Đang cài bản cập nhật, vui lòng đợi (app sẽ tự khởi động lại)...", "Installing the update, please wait (the app restarts by itself)...");
            _btnUpdateNow.Visible = _btnUpdateInfo.Visible = _btnUpdateLater.Visible = false;
            LayoutUpdateBar();
            var result = await Task.Run(UpdateController.ApplyAndRestart);
            if (result.Ok)
            {
                Application.Exit();
            }
            else
            {
                MessageBox.Show(this, T("Không cài được, đã hoàn tác (app vẫn như cũ): ", "Could not install, everything was rolled back (the app is unchanged): ") + result.Error,
                    "Social Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", "Update: " + ex, LogCategory.Update);
        }
    }

    /// <summary>Something needs the user (a captcha): a sound, the taskbar button flashes until the app is in front, and a balloon when the app sits in the tray.</summary>
    public void ShowAlert(string title, string text)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var info = new FlashInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FlashInfo>(), Window = Handle, Flags = 0x3 | 0xC, Count = uint.MaxValue, Timeout = 0 };
        FlashWindowEx(ref info);
        if (_trayIcon is { Visible: true })
        {
            _trayIcon.ShowBalloonTip(8000, title, text, ToolTipIcon.Warning);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Called when the user starts a second copy of the app: show this window instead of opening another one.</summary>
    public void BringToFrontFromAnotherInstance()
    {
        RestoreFromTray();
        TopMost = true;
        TopMost = false;
        Activate();
    }

    /// <summary>Called by a feature page that needs the user to sign in first.</summary>
    public void OpenAccounts(string platform)
    {
        NavigateTo("accounts");
        if (_pages.TryGetValue("accounts", out var page) && page is AccountsPage accounts)
        {
            accounts.ShowPlatform(platform);
        }
    }

    private void OpenFromTray(string pageKey)
    {
        RestoreFromTray();
        NavigateTo(pageKey);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        _trayIcon!.Visible = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _settingsServiceRef.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIcon!.Visible = true;
            _trayIcon.ShowBalloonTip(2000, "Social Toolkit", T("Ứng dụng vẫn đang chạy trong khay hệ thống.", "The app is still running in the system tray."), ToolTipIcon.Info);
            return;
        }

        _trayIcon?.Dispose();
        _ = _playwrightServiceRef.DisposeAsync().AsTask();
        if (_youTubePage is not null)
        {
            _ = _youTubePage.ShutdownAsync();
        }
        base.OnFormClosing(e);
    }

    private Guna2Panel BuildSidebar()
    {
        var sidebar = new Guna2Panel
        {
            Dock = DockStyle.Left,
            Width = 180,
            BackColor = Theme.Sidebar,
            Padding = new Padding(0, 12, 0, 0)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Sidebar
        };

        flow.Controls.Add(BuildLogo());
        flow.Controls.Add(BuildSectionLabel(T("CHỨC NĂNG", "FEATURES")));
        flow.Controls.Add(CreateNavItem("download", "📥", "Download"));
        flow.Controls.Add(CreateNavItem("accounts", "👤", T("Tài khoản", "Accounts")));
        BuildUnfollowGroup(flow);
        flow.Controls.Add(BuildSectionLabel(T("HỆ THỐNG", "SYSTEM")));
        flow.Controls.Add(CreateNavItem("settings", "🔧", T("Cài đặt", "Settings")));
        flow.Controls.Add(CreateNavItem("history", "📋", T("Lịch sử", "History")));
        flow.Controls.Add(CreateNavItem("guide", "📖", T("Hướng dẫn", "Guide")));
        flow.Controls.Add(BuildSectionLabel(T("CỘNG ĐỒNG", "COMMUNITY")));
        flow.Controls.Add(CreateLinkItem("🐛", T("Báo lỗi", "Report bug"), () => OpenUrl("https://github.com/ThanhNguyenVN93/tiktool/issues")));
        flow.Controls.Add(CreateLinkItem("❤️", T("Đóng góp", "Donate"), ShowDonation));

        sidebar.Controls.Add(flow);
        sidebar.Controls.Add(BuildFooter());

        return sidebar;
    }

    private void BuildUnfollowGroup(FlowLayoutPanel flow)
    {
        _unfollowParent = new Guna2Button
        {
            TextAlign = HorizontalAlignment.Left,
            Height = 36,
            Width = 164,
            Margin = new Padding(8, 2, 8, 2),
            Font = Theme.FontMedium,
            BorderRadius = 6,
            Cursor = Cursors.Hand,
            Animated = true,
            FillColor = Theme.Sidebar,
            ForeColor = Theme.TextMuted,
        };
        _unfollowParent.HoverState.FillColor = ColorTranslator.FromHtml("#161616");
        _unfollowParent.HoverState.ForeColor = ColorTranslator.FromHtml("#cccccc");
        _unfollowParent.Click += (_, _) => SetUnfollowExpanded(!_unfollowExpanded);

        _unfollowChildren = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Sidebar,
            Margin = new Padding(0),
        };

        foreach (var (key, label) in new[] { (TikTokUnfollowKey, "TikTok Unfollow"), (YouTubeUnsubKey, "YouTube Unsubscribe") })
        {
            var item = CreateNavItem(key, "", label);
            item.Text = $"  {label}";
            item.Width = 158;
            item.Margin = new Padding(14, 2, 8, 2);
            item.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _unfollowChildren.Controls.Add(item);
        }

        flow.Controls.Add(_unfollowParent);
        flow.Controls.Add(_unfollowChildren);
        SetUnfollowExpanded(false);
    }

    private void SetUnfollowExpanded(bool expanded)
    {
        _unfollowExpanded = expanded;
        _unfollowChildren.Visible = expanded;
        _unfollowParent.Text = $"  🚫   {T("Bỏ theo dõi", "Unfollow")}   {(expanded ? "▾" : "▸")}";
    }

    private static Control BuildLogo()
    {
        var panel = new Panel { Width = 164, Height = 48, Margin = new Padding(8, 0, 8, 12), BackColor = Theme.Sidebar };

        var mark = new Guna2Panel
        {
            Size = new Size(28, 28),
            Location = new Point(0, 8),
            BorderRadius = 8,
            FillColor = Theme.Accent
        };
        var markLabel = new Label
        {
            Text = "🎵",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        mark.Controls.Add(markLabel);

        var title = new Label
        {
            Text = "Social Toolkit",
            Font = Theme.FontTitle,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(38, 4)
        };
        var sub = new Label
        {
            Text = "Download && clean up",
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(38, 24)
        };

        panel.Controls.Add(mark);
        panel.Controls.Add(title);
        panel.Controls.Add(sub);
        return panel;
    }

    private static Control BuildSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Margin = new Padding(16, 8, 8, 4)
        };
    }

    private NavItem CreateNavItem(string key, string icon, string label)
    {
        var item = new NavItem(key, icon, label);
        item.Click += (_, _) => NavigateTo(key);
        _navItems.Add(item);
        return item;
    }

    private static Control CreateLinkItem(string icon, string label, Action onClick)
    {
        var btn = new Guna2Button
        {
            Text = $"  {icon}   {label}",
            TextAlign = HorizontalAlignment.Left,
            Height = 36,
            Width = 164,
            Margin = new Padding(8, 2, 8, 2),
            Font = Theme.FontMedium,
            BorderRadius = 6,
            Cursor = Cursors.Hand,
            Animated = true,
            FillColor = Theme.Sidebar,
            ForeColor = Theme.TextMuted,
        };
        btn.HoverState.FillColor = ColorTranslator.FromHtml("#161616");
        btn.HoverState.ForeColor = ColorTranslator.FromHtml("#cccccc");
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void ShowDonation()
    {
        using var form = new DonationForm();
        form.ShowDialog(this);
    }

    private Control BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 84,
            BackColor = Theme.Sidebar,
            Padding = new Padding(10, 10, 10, 10)
        };

        var (r0, dot0, lbl0) = BuildStatusRowTuple("yt-dlp ...", Theme.TextMuted, 0);
        var (r1, dot1, lbl1) = BuildStatusRowTuple("Chrome ...", Theme.TextMuted, 24);
        var (r2, dot2, lbl2) = BuildStatusRowTuple("Cookie ...", Theme.TextMuted, 48);

        _dotYtDlp = dot0; _lblYtDlp = lbl0;
        _dotChrome = dot1; _lblChrome = lbl1;
        _dotCookie = dot2; _lblCookie = lbl2;

        footer.Controls.Add(r0);
        footer.Controls.Add(r1);
        footer.Controls.Add(r2);

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Theme.Border
        };
        footer.Controls.Add(separator);

        return footer;
    }

    private static (Panel Row, Guna2Panel Dot, Label Label) BuildStatusRowTuple(string text, Color dotColor, int top)
    {
        var row = new Panel { Location = new Point(10, top + 8), Size = new Size(160, 20), BackColor = Theme.Sidebar };
        var dot = new Guna2Panel
        {
            Size = new Size(6, 6),
            Location = new Point(0, 7),
            BorderRadius = 3,
            FillColor = dotColor
        };
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(12, 2)
        };
        row.Controls.Add(dot);
        row.Controls.Add(label);
        return (row, dot, label);
    }

    private async Task RefreshFooterAsync()
    {
        // yt-dlp version
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _ytDlpServiceRef.ExecutablePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                var ver = (await p.StandardOutput.ReadToEndAsync()).Trim();
                await p.WaitForExitAsync();
                if (!string.IsNullOrEmpty(ver))
                    SetFooterRow(_lblYtDlp, _dotYtDlp, $"yt-dlp {ver}", Theme.Green);
                else
                    SetFooterRow(_lblYtDlp, _dotYtDlp, T("yt-dlp (không rõ ver)", "yt-dlp (unknown ver)"), Theme.Orange);
            }
        }
        catch
        {
            SetFooterRow(_lblYtDlp, _dotYtDlp, T("yt-dlp chưa cài", "yt-dlp not installed"), Theme.Orange);
        }

        // Chrome
        var chromePaths = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe"),
        };
        var chromeFound = chromePaths.Any(File.Exists);
        SetFooterRow(_lblChrome, _dotChrome,
            chromeFound ? T("Chrome đã cài", "Chrome found") : T("Chrome chưa tìm thấy", "Chrome not found"),
            chromeFound ? Theme.Blue : Theme.Orange);

        // Cookie
        try
        {
            var cookie = await _cookieServiceRef.LoadCookieAsync();
            if (cookie?.Username is string u)
            {
                var days = cookie.DaysLeft;
                SetFooterRow(_lblCookie, _dotCookie,
                    days.HasValue ? $"@{u} · {days}d" : $"@{u} ✓",
                    Theme.Green);
            }
            else
            {
                SetFooterRow(_lblCookie, _dotCookie, T("Cookie chưa xác thực", "Cookie not set"), Theme.TextMuted);
            }
        }
        catch
        {
            SetFooterRow(_lblCookie, _dotCookie, T("Cookie chưa xác thực", "Cookie not verified"), Theme.TextMuted);
        }
    }

    private void SetFooterRow(Label label, Guna2Panel dot, string text, Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetFooterRow(label, dot, text, color));
            return;
        }
        label.Text = text;
        dot.FillColor = color;
    }

    private void RegisterPage(string key, UserControl page)
    {
        page.Visible = false;
        _pages[key] = page;
        _contentPanel.Controls.Add(page);
    }

    private void NavigateTo(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            return;
        }

        if (_pages.TryGetValue(_activeKey, out var current))
        {
            current.Visible = false;
        }

        _activeKey = key;
        page.Visible = true;
        page.BringToFront();

        foreach (var nav in _navItems)
        {
            nav.Active = nav.PageKey == key;
        }

        var inUnfollowGroup = key is TikTokUnfollowKey or YouTubeUnsubKey;
        if (inUnfollowGroup)
        {
            SetUnfollowExpanded(true);
        }

        _unfollowParent.ForeColor = inUnfollowGroup ? Theme.Accent : Theme.TextMuted;
    }
}
