using System.Security.Cryptography;
using System.Text;
using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

/// <summary>YouTube Unsubscribe: connect once (the tool's own Chrome profile), list the subscriptions with logos, read each
/// channel's latest upload, mark the inactive ones, then unsubscribe the chosen ones with a confirmation, an automatic CSV
/// backup, pacing and a stop on repeated failures. Layout and button states follow the TikTok page.</summary>
internal sealed class YouTubePage : UserControl
{
    private const int ContentWidth = 700;

    // "No uploads for more than N days". The default (1 year) is deliberately conservative.
    private static readonly int[] ThresholdDays = { 30, 90, 180, 365, 730 };

    private readonly SettingsService _settings;
    private readonly HistoryService _history;
    private readonly YouTubeStore _store = new();
    private YouTubeService? _service;

    private readonly List<YouTubeChannel> _channels = new();
    private readonly Dictionary<YouTubeChannel, YouTubeChannelRow> _rows = new();
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private bool _loadedOnce;
    private string _progressText = "";

    private Label _headerBadge = null!;
    private ComboBox _criteriaCombo = null!;
    private ComboBox _sortCombo = null!;
    private ComboBox _viewCombo = null!;
    private TextBox _searchBox = null!;
    private System.Windows.Forms.Timer _searchTimer = null!;
    private Label _sectionLabel = null!;
    private Label _emptyHint = null!;
    private FlowLayoutPanel _listFlow = null!;
    private LogPanel _log = null!;
    private Label _actionInfo = null!;
    private Guna2Button _btnConnect = null!;
    private Guna2Button _btnSelectInactive = null!;
    private Guna2Button _btnSelectShown = null!;
    private Guna2Button _btnExport = null!;
    private Guna2Button _btnClear = null!;
    private Guna2Button _btnPrimary = null!;

    public YouTubePage(SettingsService settings, HistoryService history)
    {
        _settings = settings;
        _history = history;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        // Dock=Fill first, then Top/Bottom siblings (see CookiePage for why).
        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
        Controls.Add(BuildActionBar());

        StyleDisabledButtons();
        UpdateButtonStates();
        UpdateHeaderBadge();
    }

    /// <summary>Closes the tool's Chrome window cleanly (called when the app exits).</summary>
    public async Task ShutdownAsync()
    {
        _cts?.Cancel();
        if (_service is not null)
        {
            await _service.DisposeAsync();
        }
    }

    // ══════════════════════════════════════════════════════════════════ UI

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
        header.Controls.Add(new Label
        {
            Text = T("Hủy Đăng Ký YouTube Không Hoạt Động", "Unsubscribe Inactive YouTube Channels"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        });
        header.Controls.Add(new Label
        {
            Text = T("Tìm kênh lâu rồi không đăng video và hủy đăng ký hàng loạt, có sao lưu.",
                     "Find channels that stopped uploading and unsubscribe in bulk, with a backup."),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        });

        _headerBadge = new Label
        {
            Font = Theme.FontLabel,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
        };
        var badgeHost = new Guna2Panel { AutoSize = true, BorderRadius = 20, FillColor = Color.FromArgb(40, 40, 40), Location = new Point(560, 8) };
        badgeHost.Controls.Add(_headerBadge);
        header.Controls.Add(badgeHost);

        void PlaceBadge() => badgeHost.Left = Math.Max(300, header.Width - badgeHost.Width - 20);
        header.Resize += (_, _) => PlaceBadge();
        badgeHost.SizeChanged += (_, _) => PlaceBadge();

        header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        return header;
    }

    private Control BuildBody()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

        _log = new LogPanel { Dock = DockStyle.Fill };
        var logHost = new Panel { Dock = DockStyle.Bottom, Height = 116, Padding = new Padding(20, 0, 20, 16), BackColor = Theme.Background };
        logHost.Controls.Add(_log);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background, Padding = new Padding(20, 16, 20, 0) };
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

        body.Controls.Add(scroll);
        body.Controls.Add(logHost);
        return body;
    }

    private Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Font = Theme.FontLabel,
        ForeColor = ColorTranslator.FromHtml("#444444"),
        AutoSize = true,
        Location = new Point(x, y),
    };

    private ComboBox MakeCombo(int x, int y, int width, params string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(x, y),
            Size = new Size(width, 30),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    private Control BuildFilterRow()
    {
        var row = new Panel { Width = ContentWidth, Height = 118, Margin = new Padding(0, 0, 0, 16) };
        var half = ContentWidth / 2;

        row.Controls.Add(MakeLabel(T("Kênh không hoạt động khi", "A channel is inactive when"), 0, 0));
        _criteriaCombo = MakeCombo(0, 20, half - 8,
            T("Không đăng video > 30 ngày", "No uploads > 30 days"),
            T("Không đăng video > 90 ngày", "No uploads > 90 days"),
            T("Không đăng video > 180 ngày", "No uploads > 180 days"),
            T("Không đăng video > 1 năm (mặc định)", "No uploads > 1 year (default)"),
            T("Không đăng video > 2 năm", "No uploads > 2 years"));
        _criteriaCombo.SelectedIndex = 3;
        _criteriaCombo.SelectedIndexChanged += (_, _) =>
        {
            RecomputeInactive();
            RebuildList();
            UpdateHeaderBadge();
        };
        row.Controls.Add(_criteriaCombo);

        row.Controls.Add(MakeLabel(T("Sắp xếp", "Sort"), half + 8, 0));
        _sortCombo = MakeCombo(half + 8, 20, half - 8,
            T("Thứ tự của YouTube", "YouTube order"),
            T("Lâu không đăng nhất trước", "Longest inactive first"),
            T("Mới đăng nhất trước", "Newest upload first"),
            T("Tên A → Z", "Name A → Z"));
        _sortCombo.SelectedIndexChanged += (_, _) => RebuildList();
        row.Controls.Add(_sortCombo);

        var third = ContentWidth / 3;
        row.Controls.Add(MakeLabel(T("Hiển thị", "Show"), 0, 62));
        _viewCombo = MakeCombo(0, 82, third - 8,
            T("Tất cả kênh", "All channels"),
            T("Chỉ kênh không hoạt động", "Inactive only"),
            T("Chỉ kênh còn hoạt động", "Active only"),
            T("Chưa kiểm tra", "Not checked yet"),
            T("Chỉ kênh đã chọn", "Selected only"),
            T("Kênh được bảo vệ", "Protected"));
        _viewCombo.SelectedIndexChanged += (_, _) => RebuildList();
        row.Controls.Add(_viewCombo);

        row.Controls.Add(MakeLabel(T("Tìm kiếm", "Search"), third, 62));
        _searchBox = new TextBox
        {
            Location = new Point(third, 83),
            Size = new Size(third * 2 - 4, 26),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Tìm theo tên kênh / @handle", "Search channel name / @handle"),
        };
        // Rebuilding hundreds of rows per keystroke would stutter, so wait for a short pause in typing.
        _searchTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RebuildList();
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        row.Controls.Add(_searchBox);
        return row;
    }
    private Guna2Button MakeButton(string text, Size size, Point location, Color fill, Color fore) => new()
    {
        Text = text,
        Font = Theme.FontMedium,
        Size = size,
        Location = location,
        FillColor = fill,
        ForeColor = fore,
        BorderRadius = 7,
        Cursor = Cursors.Hand,
    };

    private Control BuildListSection()
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true };
        var quiet = ColorTranslator.FromHtml("#1a1a1a");

        _sectionLabel = new Label
        {
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = false,
            AutoEllipsis = true,
            Size = new Size(Math.Max(120, ContentWidth - 150 - 8 - 170 - 8 - 170 - 12), 18),
            Location = new Point(0, 6),
        };

        _btnConnect = MakeButton("", new Size(150, 28), new Point(ContentWidth - 150, 0), Theme.Accent, Color.White);
        _btnConnect.Click += async (_, _) => await ConnectAndLoadAsync();

        _btnSelectInactive = MakeButton(T("💤  Chọn không hoạt động", "💤  Select inactive"), new Size(170, 28),
            new Point(ContentWidth - 150 - 8 - 170, 0), quiet, Theme.TextSecondary);
        _btnSelectInactive.Click += (_, _) => SelectInactive();

        _btnSelectShown = MakeButton(T("☑  Chọn tất cả đang hiện", "☑  Select all shown"), new Size(170, 28),
            new Point(ContentWidth - 150 - 8 - 170 - 8 - 170, 0), quiet, Theme.TextSecondary);
        _btnSelectShown.Click += (_, _) => SelectShown();

        _emptyHint = new Label
        {
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(0, 70),
        };

        _listFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(0, 36),
            Width = ContentWidth,
            BackColor = Color.Transparent,
        };

        wrap.Controls.Add(_emptyHint);
        wrap.Controls.Add(_sectionLabel);
        wrap.Controls.Add(_btnSelectShown);
        wrap.Controls.Add(_btnSelectInactive);
        wrap.Controls.Add(_btnConnect);
        wrap.Controls.Add(_listFlow);
        wrap.Height = 250;
        return wrap;
    }

    private Control BuildActionBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = ColorTranslator.FromHtml("#0d0d0d") };
        var quiet = ColorTranslator.FromHtml("#2a2a2a");

        _actionInfo = new Label
        {
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 21),
        };

        _btnPrimary = MakeButton("", new Size(210, 32), new Point(ContentWidth - 210, 12), Theme.Accent, Color.White);
        _btnPrimary.Click += async (_, _) => await PrimaryAsync();

        _btnClear = MakeButton(T("Bỏ chọn", "Clear"), new Size(110, 32), new Point(ContentWidth - 210 - 8 - 110, 12), quiet, Theme.TextSecondary);
        _btnClear.Click += (_, _) => ClearSelection();

        _btnExport = MakeButton(T("💾  Xuất CSV", "💾  Export CSV"), new Size(140, 32), new Point(ContentWidth - 210 - 8 - 110 - 8 - 140, 12), quiet, Theme.TextSecondary);
        _btnExport.Click += (_, _) => ExportCsv();

        bar.Controls.Add(_actionInfo);
        bar.Controls.Add(_btnExport);
        bar.Controls.Add(_btnClear);
        bar.Controls.Add(_btnPrimary);
        bar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        return bar;
    }

    private void StyleDisabledButtons()
    {
        foreach (var b in new[] { _btnConnect, _btnSelectInactive, _btnSelectShown, _btnExport, _btnClear, _btnPrimary })
        {
            b.DisabledState.FillColor = ColorTranslator.FromHtml("#151515");
            b.DisabledState.ForeColor = ColorTranslator.FromHtml("#4a4a4a");
            b.DisabledState.BorderColor = ColorTranslator.FromHtml("#151515");
        }
    }

    /// <summary>The single source of truth for which buttons are usable (same four states as the TikTok page): nothing to act
    /// on before a list is loaded; only Stop while busy; Unsubscribe needs a selection and shows its count.</summary>
    private void UpdateButtonStates()
    {
        var hasChannels = _channels.Count > 0;
        var selectedCount = SelectableSelected().Count();
        var anyInactive = _channels.Any(c => c.IsInactive && !c.IsProtected && c.Status != YouTubeChannelStatus.Done);

        _btnConnect.Enabled = !_isBusy;
        _btnConnect.Text = _loadedOnce ? T("🔄  Tải lại", "🔄  Reload") : T("🔌  Kết nối", "🔌  Connect");
        _btnSelectInactive.Enabled = !_isBusy && anyInactive;
        _btnSelectShown.Enabled = !_isBusy && hasChannels;
        _btnClear.Enabled = !_isBusy && _channels.Any(c => c.IsSelected);
        _btnExport.Enabled = !_isBusy && hasChannels;
        _btnPrimary.Enabled = _isBusy || selectedCount > 0;
        _btnPrimary.Text = _isBusy
            ? T("⏹  Dừng", "⏹  Stop")
            : selectedCount > 0
                ? T($"🚫  Hủy đăng ký ({selectedCount})", $"🚫  Unsubscribe ({selectedCount})")
                : T("🚫  Hủy đăng ký", "🚫  Unsubscribe");

        _emptyHint.Visible = !hasChannels;
        _emptyHint.Text = _isBusy
            ? T("Đang kết nối và tải danh sách đăng ký…", "Connecting and loading your subscriptions…")
            : _loadedOnce
                ? T("Không tìm thấy kênh đăng ký nào.", "No subscribed channels found.")
                : T("Bấm \"Kết nối\": một cửa sổ Chrome riêng sẽ mở, bạn đăng nhập Google một lần.",
                    "Press \"Connect\": a separate Chrome window opens and you sign in to Google once.");

        _actionInfo.Text = T($"{selectedCount} đã chọn", $"{selectedCount} selected");
    }

    private void UpdateHeaderBadge()
    {
        var inactive = _channels.Count(c => c.IsInactive);
        _headerBadge.Text = !_loadedOnce
            ? T("Chưa kết nối", "Not connected")
            : T($"{_channels.Count} kênh · {inactive} không hoạt động", $"{_channels.Count} channels · {inactive} inactive");
        _headerBadge.ForeColor = inactive > 0 ? Theme.Orange : Theme.TextMuted;
    }

    private void UpdateSectionLabel(int shown)
    {
        if (!_loadedOnce)
        {
            _sectionLabel.Text = T("Chưa tải danh sách đăng ký", "Subscriptions not loaded");
            return;
        }

        var inactive = _channels.Count(c => c.IsInactive);
        var showing = shown != _channels.Count;
        _sectionLabel.Text = T(
            $"{_channels.Count} kênh đăng ký · {inactive} không hoạt động" + (showing ? $" · hiện {shown}" : ""),
            $"{_channels.Count} subscriptions · {inactive} inactive" + (showing ? $" · showing {shown}" : ""))
            + (_progressText.Length > 0 ? " · " + _progressText : "");
    }

    // ══════════════════════════════════════════════════════════════════ data

    private IEnumerable<YouTubeChannel> SelectableSelected() =>
        _channels.Where(c => c.IsSelected && !c.IsProtected && c.Status != YouTubeChannelStatus.Done);

    private IProgress<string> MakeProgress() => new Progress<string>(m =>
        _log.Log("[YouTube] " + m, m.StartsWith('⚠') ? LogLevel.Warn : m.StartsWith('✓') ? LogLevel.Ok : m.StartsWith('✗') ? LogLevel.Error : LogLevel.Info));

    private void RecomputeInactive()
    {
        var days = ThresholdDays[Math.Max(0, _criteriaCombo.SelectedIndex)];
        foreach (var c in _channels)
        {
            // Shorts show no dates: old long videos + a Shorts tab means we cannot tell, so it is not called inactive.
            c.IsUncertain = c.AgeChecked && c.HasShorts && (c.NoVideos || (c.DaysSinceLastUpload ?? 0) >= 30);
            c.IsInactive = c.AgeChecked && !c.IsUncertain && (c.NoVideos || (c.DaysSinceLastUpload ?? 0) >= days);
        }
    }

    private List<YouTubeChannel> GetVisible()
    {
        IEnumerable<YouTubeChannel> q = _channels;

        switch (_viewCombo.SelectedIndex)
        {
            case 1: q = q.Where(c => c.IsInactive); break;
            case 2: q = q.Where(c => c.AgeChecked && !c.IsInactive); break;
            case 3: q = q.Where(c => !c.AgeChecked); break;
            case 4: q = q.Where(c => c.IsSelected); break;
            case 5: q = q.Where(c => c.IsProtected); break;
        }

        var term = _searchBox.Text.Trim().TrimStart('@');
        if (term.Length > 0)
        {
            q = q.Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                          || c.Handle.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        switch (_sortCombo.SelectedIndex)
        {
            case 1: // checked first; channels with no videos, then the longest-silent; unknown ages last
                q = q.OrderBy(c => c.AgeChecked ? 0 : 1)
                     .ThenBy(c => c.NoVideos ? 0 : 1)
                     .ThenByDescending(c => c.DaysSinceLastUpload ?? -1);
                break;
            case 2:
                q = q.OrderBy(c => c.AgeChecked ? 0 : 1)
                     .ThenBy(c => c.NoVideos ? 1 : 0)
                     .ThenBy(c => c.DaysSinceLastUpload ?? int.MaxValue);
                break;
            case 3:
                q = q.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        return q.ToList();
    }

    private void RebuildList()
    {
        var visible = GetVisible();

        _listFlow.SuspendLayout();
        _listFlow.Controls.Clear();
        _rows.Clear();
        foreach (var channel in visible)
        {
            var row = new YouTubeChannelRow(channel, ContentWidth);
            row.SelectionChanged += UpdateButtonStates;
            row.ProtectToggled += (c, isProtected) =>
            {
                _store.SetProtected(c.Url, isProtected);
                _log.Log(isProtected
                    ? T($"[YouTube] 🔒 Đã bảo vệ {c.Name}.", $"[YouTube] 🔒 Protected {c.Name}.")
                    : T($"[YouTube] 🔓 Đã bỏ bảo vệ {c.Name}.", $"[YouTube] 🔓 Removed the protection of {c.Name}."), LogLevel.Info);
                UpdateButtonStates();
            };
            _rows[channel] = row;
            _listFlow.Controls.Add(row);
        }

        _listFlow.ResumeLayout(true);
        UpdateSectionLabel(visible.Count);
        UpdateButtonStates();
    }

    private void RefreshRow(YouTubeChannel channel)
    {
        if (_rows.TryGetValue(channel, out var row))
        {
            row.RefreshStatus();
        }
    }

    private void SelectInactive()
    {
        var count = 0;
        foreach (var c in _channels.Where(c => c.IsInactive && !c.IsProtected && c.Status != YouTubeChannelStatus.Done))
        {
            c.IsSelected = true;
            count++;
        }

        RebuildList();
        _log.Log(T($"[YouTube] Đã chọn {count} kênh không hoạt động.", $"[YouTube] Selected {count} inactive channels."), LogLevel.Info);
    }

    private void SelectShown()
    {
        var count = 0;
        foreach (var c in GetVisible().Where(c => !c.IsProtected && c.Status != YouTubeChannelStatus.Done))
        {
            c.IsSelected = true;
            count++;
        }

        RebuildList();
        _log.Log(T($"[YouTube] Đã chọn {count} kênh đang hiển thị.", $"[YouTube] Selected {count} shown channels."), LogLevel.Info);
    }

    private void ClearSelection()
    {
        foreach (var c in _channels)
        {
            c.IsSelected = false;
        }

        RebuildList();
    }
    // ══════════════════════════════════════════════════════════════════ actions

    /// <summary>Runs one long operation at a time; the primary button turns into Stop while it runs.</summary>
    private async Task RunBusyAsync(Func<CancellationToken, Task> work)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _cts = new CancellationTokenSource();
        UpdateButtonStates();
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Log(T("[YouTube] Đã dừng theo yêu cầu.", "[YouTube] Stopped by user."), LogLevel.Warn);
        }
        catch (Exception ex)
        {
            _log.Log($"[YouTube] {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _isBusy = false;
            _cts?.Dispose();
            _cts = null;
            UpdateButtonStates();
            UpdateHeaderBadge();
        }
    }

    private Task ConnectAndLoadAsync() => RunBusyAsync(async ct =>
    {
        var progress = MakeProgress();
        if (_service is not null && !await _service.IsAliveAsync())
        {
            progress.Report(T("Cửa sổ Chrome của tool đã đóng — đang kết nối lại...", "The tool's Chrome window was closed — reconnecting..."));
            await _service.DisposeAsync();
            _service = null;
        }

        _service ??= new YouTubeService();
        progress.Report(T("Đang mở Chrome riêng...", "Opening the separate Chrome window..."));
        await _service.ConnectAsync(progress, ct);

        var list = await _service.LoadSubscriptionsAsync(progress, ct);
        _channels.Clear();
        _channels.AddRange(list);
        foreach (var c in _channels)
        {
            c.IsProtected = _store.IsProtected(c.Url);
            if (_store.TryGetFreshAge(c.Url, out var cached))
            {
                c.LastUploadUnix = cached.LastUploadUnix;
                c.LastUploadText = cached.Text;
                c.NoVideos = cached.NoVideos;
                c.HasShorts = cached.HasShorts;
                c.AgeChecked = true;
            }
        }

        _loadedOnce = true;
        RecomputeInactive();
        RebuildList();
        UpdateHeaderBadge();
        StartAvatarDownloads();

        await CheckAgesAsync(progress, ct);
    });

    /// <summary>Reads the latest upload of every channel that has no fresh cached value (about a second each), updating the
    /// rows as it goes. Stopping keeps what was read; the next load only reads the rest.</summary>
    private async Task CheckAgesAsync(IProgress<string> progress, CancellationToken ct)
    {
        var todo = _channels.Where(c => !c.AgeChecked).ToList();
        if (todo.Count == 0 || _service is null)
        {
            return;
        }

        var minutes = Math.Max(1, (int)Math.Round(todo.Count * 1.5 / 60));
        progress.Report(T($"Đang đọc ngày đăng video mới nhất của {todo.Count} kênh (khoảng {minutes} phút, bấm Dừng để ngừng)...",
                          $"Reading the latest upload of {todo.Count} channels (about {minutes} min, press Stop to cancel)..."));

        var done = 0;
        var failures = 0;
        var streak = 0;
        _progressText = T($"đang đọc ngày đăng 0/{todo.Count}", $"reading upload dates 0/{todo.Count}");
        UpdateSectionLabel(_rows.Count);
        try
        {
            foreach (var channel in todo)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var age = await _service.ReadLatestUploadAsync(channel.Url, ct);
                    channel.LastUploadUnix = age.LastUploadUnix;
                    channel.LastUploadText = age.Text;
                    channel.NoVideos = age.NoVideos;
                    channel.HasShorts = age.HasShorts;
                    channel.AgeChecked = true;
                    _store.SetAge(channel.Url, age.LastUploadUnix, age.Text, age.NoVideos, age.HasShorts);
                    streak = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    streak++;
                    progress.Report(T($"⚠ Không đọc được {channel.Name}: {ex.Message}", $"⚠ Could not read {channel.Name}: {ex.Message}"));
                    if (streak >= 5)
                    {
                        progress.Report(T("⚠ Nhiều kênh liên tiếp không đọc được — dừng để tránh bị chặn. Bấm Tải lại sau một lúc.",
                                          "⚠ Several channels in a row could not be read — stopping to avoid a block. Press Reload in a while."));
                        break;
                    }
                }

                RecomputeInactive();
                RefreshRow(channel);
                done++;
                _progressText = T($"đang đọc ngày {done}/{todo.Count}", $"reading dates {done}/{todo.Count}");
                UpdateSectionLabel(_rows.Count);
                if (done % 10 == 0)
                {
                    _store.SaveAges();
                    UpdateHeaderBadge();
                    UpdateButtonStates();
                    progress.Report(T($"Đã đọc {done}/{todo.Count} kênh...", $"Read {done}/{todo.Count} channels..."));
                }

                await Task.Delay(Random.Shared.Next(300, 600), ct);
            }
        }
        finally
        {
            _progressText = "";
            _store.SaveAges();
            RecomputeInactive();
            RebuildList();
            UpdateHeaderBadge();
        }

        progress.Report(T($"✓ Xong: đọc được {done - failures}/{todo.Count} kênh, {_channels.Count(c => c.IsInactive)} kênh không hoạt động.",
                          $"✓ Done: read {done - failures}/{todo.Count} channels, {_channels.Count(c => c.IsInactive)} inactive."));
    }

    private static string AvatarKey(YouTubeChannel channel) =>
        "yt_" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(channel.Url)))[..16];

    /// <summary>Logos: reuse cached files, download the rest in the background (4 at a time).</summary>
    private void StartAvatarDownloads()
    {
        var toDownload = new List<YouTubeChannel>();
        foreach (var c in _channels)
        {
            c.AvatarPath = AvatarCache.CachedPath(AvatarKey(c));
            if (c.AvatarPath is not null)
            {
                RefreshRow(c);
            }
            else if (!string.IsNullOrEmpty(c.AvatarUrl))
            {
                toDownload.Add(c);
            }
        }

        if (toDownload.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(toDownload.Select(async c =>
            {
                await gate.WaitAsync();
                try
                {
                    var path = await AvatarCache.DownloadAsync(AvatarKey(c), c.AvatarUrl, CancellationToken.None);
                    if (path is not null)
                    {
                        c.AvatarPath = path;
                        BeginInvoke(() => RefreshRow(c));
                    }
                }
                catch
                {
                    // one missing logo must never affect the others
                }
                finally
                {
                    gate.Release();
                }
            }));
        });
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"youtube-subscriptions-{DateTime.Now:yyyyMMdd}.csv",
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, YouTubeStore.BuildCsv(_channels), new UTF8Encoding(true));
        _log.Log(T($"[YouTube] Đã xuất {_channels.Count} kênh → {dialog.FileName}", $"[YouTube] Exported {_channels.Count} channels → {dialog.FileName}"), LogLevel.Ok);
    }

    private async Task PrimaryAsync()
    {
        if (_isBusy)
        {
            _cts?.Cancel();
            return;
        }

        var limit = Math.Max(1, _settings.Current.UnfollowSessionLimit);
        var allSelected = SelectableSelected().ToList();
        var selected = allSelected.Take(limit).ToList();
        if (selected.Count == 0)
        {
            _log.Log(T("[YouTube] Chưa chọn kênh nào để hủy đăng ký.", "[YouTube] No channels selected to unsubscribe."), LogLevel.Warn);
            return;
        }

        if (allSelected.Count > selected.Count)
        {
            _log.Log(T($"[YouTube] Giới hạn mỗi lượt: {limit} kênh — {allSelected.Count - selected.Count} kênh còn lại chờ lượt sau.",
                       $"[YouTube] Per-run limit: {limit} channels — the other {allSelected.Count - selected.Count} wait for the next run."), LogLevel.Warn);
        }

        if (_service is null || !await _service.IsAliveAsync())
        {
            _log.Log(T("[YouTube] Chưa kết nối, hoặc cửa sổ Chrome của tool đã đóng — bấm \"Kết nối\" / \"Tải lại\" trước.",
                       "[YouTube] Not connected, or the tool's Chrome window was closed — press \"Connect\" / \"Reload\" first."), LogLevel.Warn);
            return;
        }

        if (!ConfirmUnsubscribe(selected))
        {
            _log.Log(T("[YouTube] Đã hủy — chưa hủy đăng ký kênh nào.", "[YouTube] Cancelled — nothing was unsubscribed."), LogLevel.Info);
            return;
        }

        await RunBusyAsync(async ct =>
        {
            var backup = _store.WriteBackup(_channels);
            _log.Log(T($"[YouTube] Đã sao lưu danh sách đăng ký (CSV): {backup}", $"[YouTube] Subscriptions backed up (CSV): {backup}"), LogLevel.Ok);
            _log.Log(T($"[YouTube] Bắt đầu hủy đăng ký {selected.Count} kênh...", $"[YouTube] Unsubscribing from {selected.Count} channels..."), LogLevel.Info);

            var ok = 0;
            var failStreak = 0;
            foreach (var channel in selected)
            {
                ct.ThrowIfCancellationRequested();
                channel.Status = YouTubeChannelStatus.Unsubscribing;
                RefreshRow(channel);

                (bool Ok, string? Error) result;
                try
                {
                    result = await _service!.UnsubscribeAsync(channel, ct);
                }
                catch (OperationCanceledException)
                {
                    channel.Status = YouTubeChannelStatus.Pending;
                    RefreshRow(channel);
                    throw;
                }
                catch (Exception ex)
                {
                    result = (false, ex.Message.Split('\n')[0]);
                }

                if (result.Ok)
                {
                    channel.Status = YouTubeChannelStatus.Done;
                    channel.IsSelected = false;
                    ok++;
                    failStreak = 0;
                    _log.Log(T($"[YouTube] ✓ Đã hủy đăng ký {channel.Name}", $"[YouTube] ✓ Unsubscribed from {channel.Name}"), LogLevel.Ok);
                }
                else
                {
                    channel.Status = YouTubeChannelStatus.Failed;
                    channel.ErrorMessage = result.Error;
                    failStreak++;
                    _log.Log(T($"[YouTube] ✗ Lỗi {channel.Name}: {result.Error}", $"[YouTube] ✗ Error {channel.Name}: {result.Error}"), LogLevel.Error);
                }

                await _history.AddAsync(new HistoryItem
                {
                    Type = "Unsubscribe",
                    Title = channel.Name,
                    Detail = result.Ok ? $"YouTube · {channel.Url}" : $"YouTube · {channel.Url} · {result.Error}",
                    Success = result.Ok,
                });

                RefreshRow(channel);
                UpdateButtonStates();
                UpdateHeaderBadge();

                if (failStreak >= 3)
                {
                    _log.Log(T("[YouTube] ⚠ 3 lỗi liên tiếp — dừng để bảo vệ tài khoản. Hãy thử lại sau.",
                               "[YouTube] ⚠ 3 errors in a row — stopping to protect the account. Try again later."), LogLevel.Warn);
                    break;
                }

                var s = _settings.Current;
                await Task.Delay(Random.Shared.Next(s.UnfollowDelayMinMs, Math.Max(s.UnfollowDelayMinMs + 1, s.UnfollowDelayMaxMs)), ct);
            }

            _log.Log(T($"[YouTube] Xong lượt này: hủy đăng ký {ok}/{selected.Count} kênh.", $"[YouTube] Run finished: unsubscribed from {ok}/{selected.Count} channels."), LogLevel.Ok);
        });
    }

    /// <summary>The last chance before a real change to the account: counts, a few names, the backup and how to undo.
    /// Defaults to "No".</summary>
    private bool ConfirmUnsubscribe(List<YouTubeChannel> selected)
    {
        var active = selected.Count(c => !c.IsInactive);
        var inactive = selected.Count - active;
        var sample = string.Join(", ", selected.Take(5).Select(c => c.Name)) + (selected.Count > 5 ? ", ..." : "");

        var vi = new StringBuilder();
        vi.AppendLine($"Bạn sắp hủy đăng ký {selected.Count} kênh YouTube:");
        vi.AppendLine($"  • {inactive} kênh không hoạt động");
        if (active > 0)
        {
            vi.AppendLine($"  • {active} kênh CÒN HOẠT ĐỘNG hoặc chưa kiểm tra (không thuộc diện không hoạt động)");
        }

        vi.AppendLine();
        vi.AppendLine($"Ví dụ: {sample}");
        vi.AppendLine();
        vi.Append("Danh sách đăng ký sẽ được sao lưu ra file CSV trước. Muốn đăng ký lại phải làm tay từng kênh (link trong CSV và tab Lịch sử). Tiếp tục?");

        var en = new StringBuilder();
        en.AppendLine($"You are about to unsubscribe from {selected.Count} YouTube channels:");
        en.AppendLine($"  • {inactive} inactive channels");
        if (active > 0)
        {
            en.AppendLine($"  • {active} channels that are STILL ACTIVE or not checked (not inactive)");
        }

        en.AppendLine();
        en.AppendLine($"For example: {sample}");
        en.AppendLine();
        en.Append("Your subscriptions are backed up to a CSV file first. Subscribing again means doing it by hand, one channel at a time (links are in the CSV and the History tab). Continue?");

        return MessageBox.Show(
            this,
            T(vi.ToString(), en.ToString()),
            T("Social Toolkit — Xác nhận hủy đăng ký", "Social Toolkit — Confirm unsubscribe"),
            MessageBoxButtons.YesNo,
            active > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}