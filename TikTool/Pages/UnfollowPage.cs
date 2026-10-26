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

    public UnfollowPage(
        PlaywrightService playwright,
        TikTokApiService apiService,
        CookieService cookieService,
        SettingsService settingsService,
        HistoryService historyService)
    {
        _playwright = playwright;
        _apiService = apiService;
        _cookieService = cookieService;
        _settingsService = settingsService;
        _historyService = historyService;

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
        var row = new Panel { Width = ContentWidth, Height = 56, Margin = new Padding(0, 0, 0, 16) };

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
            Text = T("☠  Chọn tất cả kênh chết", "☠  Select all dead"),
            Font = Theme.FontMedium,
            Size = new Size(190, 28),
            Location = new Point(ContentWidth - 300, 0),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnSelectDead.Click += (_, _) => SelectAllDead();

        _listFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(0, 36),
            Width = ContentWidth,
            BackColor = Color.Transparent,
        };

        wrap.Controls.Add(_sectionLabel);
        wrap.Controls.Add(_btnSelectDead);
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
            Location = new Point(20, 18),
        };

        _btnStart = new Guna2Button
        {
            Text = T("Bắt đầu bỏ theo dõi", "Start unfollowing"),
            Font = Theme.FontMedium,
            Size = new Size(160, 32),
            Location = new Point(ContentWidth - 160, 12),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnStart.Click += async (_, _) => await StartOrStopUnfollowAsync();

        _btnClearSelection = new Guna2Button
        {
            Text = T("Bỏ chọn", "Clear selection"),
            Font = Theme.FontMedium,
            Size = new Size(100, 32),
            Location = new Point(ContentWidth - 160 - 108, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnClearSelection.Click += (_, _) => ClearSelection();

        bar.Controls.Add(_actionInfo);
        bar.Controls.Add(_btnClearSelection);
        bar.Controls.Add(_btnStart);
        bar.Controls.Add(sep);
        return bar;
    }

    // ══════════════════════════════════════════════════════════
    // ACTIONS
    // ══════════════════════════════════════════════════════════

    private async Task ReloadFollowingListAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null || string.IsNullOrEmpty(cookie.Username))
        {
            _log.Log(T("[Unfollow] Chưa có cookie đã xác thực — sang tab Cookie để đăng nhập trước.",
                        "[Unfollow] No authenticated cookie — go to Cookie tab to sign in first."), LogLevel.Warn);
            return;
        }

        _isBusy = true;
        _btnReload.Enabled = false;
        _btnStart.Text = T("⏹  Dừng", "⏹  Stop");
        _cts = new CancellationTokenSource();
        var reloadCt = _cts.Token;
        _log.Log(T("[Playwright] Đang mở Chrome thật (sẽ hiện cửa sổ) và inject cookie...",
                    "[Playwright] Opening real Chrome (a window will appear) and injecting cookie..."), LogLevel.Info);

        try
        {
            await EnsurePlaywrightAsync(cookie);

            _log.Log(T("[Unfollow] Đang tải danh sách đang theo dõi (cuộn tự động)...",
                        "[Unfollow] Loading following list (auto-scroll)..."), LogLevel.Info);
            var progress = new Progress<string>(m => _log.Log(m, LogLevel.Info));
            var selectors = SelectorsService.Load();
            var accounts = await _playwright.LoadFollowingListAsync(selectors, cookie.Username!, progress, reloadCt);

            _accounts.Clear();
            _accounts.AddRange(accounts);
            _log.Log(T($"[Unfollow] Đã tải {accounts.Count} tài khoản. Đang kiểm tra ngày đăng gần nhất...",
                        $"[Unfollow] Loaded {accounts.Count} accounts. Checking last post dates..."), LogLevel.Ok);

            await CheckDeadChannelsAsync(reloadCt);
            RecomputeDeadStatus();

            RebuildList();
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
            _btnStart.Text = T("🚫  Bắt đầu Unfollow", "🚫  Start Unfollow");
            _cts?.Dispose();
            _cts = null;
            UpdateHeaderBadge();
        }
    }

    private async Task EnsurePlaywrightAsync(TikTokCookie cookie)
    {
        if (_playwright.IsLaunched)
        {
            return;
        }

        await _playwright.LaunchAsync(cookie);
    }

    /// <summary>Sequential per-account lookup, one Playwright page navigation per profile (see
    /// PlaywrightService.GetUserDetailAsync for why this can't be a lightweight direct API call
    /// — TikTok's user-detail API needs a signature a plain request can't produce). Not
    /// parallelized — see "Ghi chú bổ sung cho Bước 4" in PROGRESS.md: hammering profile pages
    /// concurrently risks a rate limit/bot-check before the unfollow pass even starts, and a
    /// single headless page can only navigate one URL at a time anyway.</summary>
    private async Task CheckDeadChannelsAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < _accounts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var account = _accounts[i];
            var detail = await _playwright.GetUserDetailAsync(account.Username, ct);
            if (detail is { } d)
            {
                account.UserId = d.UserId;
                account.VideoCount = d.VideoCount;
                account.LastPostUnixSeconds = d.LastPostUnixSeconds;
            }

            if ((i + 1) % 10 == 0)
            {
                _log.Log(T($"[Unfollow] Đã kiểm tra {i + 1}/{_accounts.Count} tài khoản...", $"[Unfollow] Checked {i + 1}/{_accounts.Count} accounts..."), LogLevel.Info);
            }

            await Task.Delay(300);
        }
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

        foreach (var account in _accounts)
        {
            var row = new FollowingItemControl(account, ContentWidth);
            row.SelectionChanged += UpdateActionInfo;
            _rows[account] = row;
            _listFlow.Controls.Add(row);
        }

        _listFlow.ResumeLayout(true);
        UpdateSectionLabel();
        UpdateActionInfo();
    }

    private void SelectAllDead()
    {
        foreach (var account in _accounts.Where(a => a.IsDead))
        {
            account.IsSelected = true;
            if (_rows.TryGetValue(account, out var row))
            {
                row.RefreshStatus();
            }
        }

        RebuildList();
        _log.Log(T($"[Unfollow] Đã chọn {_accounts.Count(a => a.IsDead)} kênh chết.", $"[Unfollow] Selected {_accounts.Count(a => a.IsDead)} dead channels."), LogLevel.Info);
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

        var sessionLimit = _settingsService.Current.UnfollowSessionLimit;
        var selected = _accounts.Where(a => a.IsSelected && a.Status != UnfollowStatus.Done).Take(sessionLimit).ToList();
        if (selected.Count == 0)
        {
            _log.Log(T("[Unfollow] Chưa chọn tài khoản nào để unfollow.", "[Unfollow] No accounts selected to unfollow."), LogLevel.Warn);
            return;
        }

        var skippedBySessionLimit = _accounts.Count(a => a.IsSelected && a.Status != UnfollowStatus.Done) - selected.Count;
        if (skippedBySessionLimit > 0)
        {
            _log.Log(T($"[Unfollow] Giới hạn {sessionLimit} tài khoản/phiên (Cài đặt) — {skippedBySessionLimit} tài khoản còn lại sẽ chờ lần chạy sau.",
                        $"[Unfollow] Session limit {sessionLimit} accounts (Settings) — {skippedBySessionLimit} remaining will wait for next run."), LogLevel.Warn);
        }

        var cookie = await _cookieService.LoadCookieAsync();
        if (cookie is null)
        {
            _log.Log(T("[Unfollow] Không tìm thấy cookie — sang tab Cookie trước.", "[Unfollow] Cookie not found — go to Cookie tab first."), LogLevel.Error);
            return;
        }

        _isBusy = true;
        _btnStart.Text = T("⏹  Dừng", "⏹  Stop");
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
                            _log.Log($"[Unfollow] {rex.Message}", LogLevel.Error);

                            if (_settingsService.Current.NotifyOnRateLimit)
                            {
                                var choice = MessageBox.Show(
                                    T($"{rex.Message}\n\nTiếp tục unfollow các tài khoản còn lại?",
                                      $"{rex.Message}\n\nContinue unfollowing the remaining accounts?"),
                                    "TikTool — Rate limit",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);
                                if (choice == DialogResult.No)
                                {
                                    throw new OperationCanceledException();
                                }
                            }
                            else
                            {
                                _log.Log(T("[Unfollow] Cảnh báo rate limit đang tắt (Cài đặt) — tự động dùng trình duyệt và tiếp tục.",
                                        "[Unfollow] Rate limit notification is off (Settings) — auto-switching to browser and continuing."), LogLevel.Warn);
                            }

                            await Task.Delay(10_000, ct);
                            await EnsurePlaywrightAsync(cookie);
                            await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), ct);
                        }
                        catch (Exception)
                        {
                            await EnsurePlaywrightAsync(cookie);
                            await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), ct);
                        }
                    }
                    else
                    {
                        await EnsurePlaywrightAsync(cookie);
                        await _playwright.ClickUnfollowAsync(account.Username, SelectorsService.Load(), ct);
                    }

                    account.Status = UnfollowStatus.Done;
                    _log.Log(T($"[Unfollow] ✓ Đã unfollow @{account.Username}", $"[Unfollow] ✓ Unfollowed @{account.Username}"), LogLevel.Ok);
                    await _historyService.AddAsync(new HistoryItem
                    {
                        Type = "Unfollow",
                        Title = $"@{account.Username}",
                        Detail = account.VideoCount == 0 ? T("Kênh chết (0 video)", "Dead channel (0 videos)") : $"{account.VideoCount} video",
                        Success = true,
                    });
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

                var settings = _settingsService.Current;
                var delay = Random.Shared.Next(settings.UnfollowDelayMinMs, Math.Max(settings.UnfollowDelayMinMs + 1, settings.UnfollowDelayMaxMs));
                await Task.Delay(delay, ct);
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
            _btnStart.Text = T("🚫  Bắt đầu Unfollow", "🚫  Start Unfollow");
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

    private void UpdateSectionLabel()
    {
        var dead = _accounts.Count(a => a.IsDead);
        _sectionLabel.Text = T($"{_accounts.Count} kênh đang theo dõi · {dead} kênh chết", $"{_accounts.Count} following · {dead} dead");
    }

    private void UpdateActionInfo()
    {
        void Update()
        {
            var selected = _accounts.Count(a => a.IsSelected);
            _actionInfo.Text = T($"{selected} đã chọn", $"{selected} selected");
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
