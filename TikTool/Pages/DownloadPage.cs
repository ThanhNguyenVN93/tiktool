using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

internal sealed class DownloadPage : UserControl
{
    private const int ContentWidth = 700;

    private readonly YtDlpService _ytDlp;
    private readonly HistoryService _historyService;
    private readonly SettingsService _settingsService;
    private readonly CookieService _cookieService;
    private readonly List<DownloadItem> _queue = new();
    private readonly Dictionary<DownloadItem, DownloadQueueItemRow> _rows = new();

    private CancellationTokenSource? _cts;
    private int _activeWorkers;
    private readonly SemaphoreSlim _pickLock = new(1, 1);

    private TextBox _urlBox = null!;
    private FlowLayoutPanel _formatRow = null!;
    private readonly List<Guna2Panel> _formatCards = new();
    private string _selectedFormat = "mp4";
    private TextBox _outputDirBox = null!;

    private FlowLayoutPanel _queueFlow = null!;
    private Label _queueCountLabel = null!;
    private LogPanel _log = null!;
    private Label _actionInfo = null!;
    private Guna2Button _btnStopAll = null!;
    private Guna2Button _btnAdd = null!;

    public DownloadPage(YtDlpService ytDlp, HistoryService historyService, SettingsService settingsService, CookieService cookieService)
    {
        _ytDlp = ytDlp;
        _historyService = historyService;
        _settingsService = settingsService;
        _cookieService = cookieService;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();

        Load += (_, _) => BeginInvoke(new Action(() =>
        {
            if (!_ytDlp.FfmpegAvailable)
            {
                _log.Log(
                    T("[yt-dlp] Không tìm thấy ffmpeg — chỉ tải được định dạng gốc đã ghép sẵn (không thể ghép video+audio tách riêng, không chuyển đổi MP3 chuẩn). Cài ffmpeg và thêm vào PATH để dùng đầy đủ tính năng.",
                      "[yt-dlp] ffmpeg not found — only pre-merged formats can be downloaded (cannot merge video+audio or convert to standard MP3). Install ffmpeg and add it to PATH for full features."),
                    LogLevel.Warn);
            }
        }));
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
            Text = "Download Video",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        };
        var subtitle = new Label
        {
            Text = T("Hỗ trợ TikTok, Instagram, Facebook Reels, YouTube và 1000+ trang khác",
                     "Supports TikTok, Instagram, Facebook Reels, YouTube and 1000+ other sites"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        };

        var badge = new Label
        {
            Text = "yt-dlp",
            Font = Theme.FontLabel,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = Theme.Accent,
            BackColor = Color.Transparent,
        };
        var badgeHost = new Guna2Panel
        {
            AutoSize = true,
            BorderRadius = 20,
            FillColor = Color.FromArgb(30, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B),
            Location = new Point(600, 16),
        };
        badgeHost.Controls.Add(badge);

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

        _log = new LogPanel { Dock = DockStyle.Fill, Category = LogCategory.Download };
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

        flow.Controls.Add(BuildUrlRow());
        flow.Controls.Add(BuildFormatSection());
        flow.Controls.Add(BuildOutputDirRow());
        flow.Controls.Add(BuildQueueSection());

        scroll.Controls.Add(flow);
        return scroll;
    }

    private Control BuildUrlRow()
    {
        var row = new Panel { Width = ContentWidth, Height = 40, Margin = new Padding(0, 0, 0, 16) };

        _urlBox = new TextBox
        {
            Location = new Point(0, 0),
            Size = new Size(ContentWidth - 116, 34),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontRegular,
            PlaceholderText = T("TikTok, Instagram, Facebook Reels, YouTube... — dán URL vào đây",
                "TikTok, Instagram, Facebook Reels, YouTube... — paste URL here"),
        };

        var btnAnalyze = new Guna2Button
        {
            Text = T("+ Thêm", "+ Add"),
            Font = Theme.FontMedium,
            Location = new Point(ContentWidth - 108, 0),
            Size = new Size(108, 34),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnAnalyze.Click += async (_, _) => await AddCurrentUrlAsync();

        row.Controls.Add(_urlBox);
        row.Controls.Add(btnAnalyze);
        return row;
    }

    private Control BuildFormatSection()
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };

        var label = new Label
        {
            Text = T("Định dạng xuất", "Output format"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(0, 0),
        };

        _formatRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(0, 22),
            BackColor = Color.Transparent,
        };

        _formatRow.Controls.Add(BuildFormatCard("mp4", "🎬 MP4", T("Video H264 · Tốt nhất", "Video H264 · Best quality")));
        _formatRow.Controls.Add(BuildFormatCard("mp3", "🎵 MP3", T("Chỉ âm thanh · 192kbps", "Audio only · 192kbps")));
        _formatRow.Controls.Add(BuildFormatCard("mkv", "📦 MKV", T("Chất lượng gốc", "Original quality")));

        wrap.Controls.Add(label);
        wrap.Controls.Add(_formatRow);
        wrap.Height = 22 + 70;
        return wrap;
    }

    private Guna2Panel BuildFormatCard(string format, string name, string desc)
    {
        var card = new Guna2Panel
        {
            Size = new Size(220, 60),
            Margin = new Padding(0, 0, 8, 0),
            BorderRadius = 8,
            FillColor = Theme.Panel,
            BorderColor = format == _selectedFormat ? Theme.Accent : Theme.Border,
            BorderThickness = 1,
            Cursor = Cursors.Hand,
            Tag = format,
        };
        var nameLabel = new Label { Text = name, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.White, AutoSize = false, Size = new Size(200, 20), Location = new Point(10, 10), BackColor = Theme.Panel };
        var descLabel = new Label { Text = desc, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.TextMuted, AutoSize = false, Size = new Size(200, 18), Location = new Point(10, 32), BackColor = Theme.Panel };

        card.Controls.Add(nameLabel);
        card.Controls.Add(descLabel);

        EventHandler select = (_, _) => SelectFormat(format);
        card.Click += select;
        nameLabel.Click += select;
        descLabel.Click += select;
        nameLabel.Cursor = Cursors.Hand;
        descLabel.Cursor = Cursors.Hand;

        _formatCards.Add(card);
        return card;
    }

    private void SelectFormat(string format)
    {
        _selectedFormat = format;
        foreach (var card in _formatCards)
        {
            card.BorderColor = (string)card.Tag! == format ? Theme.Accent : Theme.Border;
        }
    }

    private Control BuildOutputDirRow()
    {
        var row = new Panel { Width = ContentWidth, Height = 40, Margin = new Padding(0, 0, 0, 16) };

        _outputDirBox = new TextBox
        {
            Location = new Point(0, 0),
            Size = new Size(ContentWidth - 116, 34),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontRegular,
            Text = AppPaths.DefaultDownloadDir,
        };

        var btnChoose = new Guna2Button
        {
            Text = T("Chọn...", "Browse..."),
            Font = Theme.FontMedium,
            Location = new Point(ContentWidth - 108, 0),
            Size = new Size(108, 34),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
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

        row.Controls.Add(_outputDirBox);
        row.Controls.Add(btnChoose);
        return row;
    }

    private Control BuildQueueSection()
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true };

        _queueCountLabel = new Label
        {
            Text = T("Hàng đợi download · 0 mục", "Download queue · 0 items"),
            Font = Theme.FontLabel,
            ForeColor = ColorTranslator.FromHtml("#444444"),
            AutoSize = true,
            Location = new Point(0, 0),
        };

        _queueFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(0, 24),
            Width = ContentWidth,
            BackColor = Color.Transparent,
        };

        wrap.Controls.Add(_queueCountLabel);
        wrap.Controls.Add(_queueFlow);
        wrap.Resize += (_, _) => _queueFlow.Height = wrap.Height - 24;
        wrap.Height = 200;
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
            Text = T("0 chờ · 0 đang tải · 0 xong", "0 waiting · 0 downloading · 0 done"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 18),
        };

        _btnAdd = new Guna2Button
        {
            Text = T("+ Thêm URL", "+ Add URL"),
            Font = Theme.FontMedium,
            Size = new Size(120, 32),
            Location = new Point(560, 12),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnAdd.Click += (_, _) => _urlBox.Focus();

        _btnStopAll = new Guna2Button
        {
            Text = T("Dừng tất cả", "Stop all"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(440, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _btnStopAll.Click += (_, _) => StopAll();

        bar.Controls.Add(_actionInfo);
        bar.Controls.Add(_btnStopAll);
        bar.Controls.Add(_btnAdd);
        bar.Controls.Add(sep);
        return bar;
    }

    private async Task AddCurrentUrlAsync()
    {
        var url = _urlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            _log.Log(T("[yt-dlp] Chưa nhập URL", "[yt-dlp] No URL entered"), LogLevel.Warn);
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputDirBox.Text))
        {
            _log.Log(T("[yt-dlp] Chưa chọn thư mục lưu", "[yt-dlp] No output folder selected"), LogLevel.Warn);
            return;
        }

        var item = new DownloadItem
        {
            Url = url,
            Title = url,
            Format = _selectedFormat,
            OutputDir = _outputDirBox.Text.Trim(),
            Status = DownloadStatus.Waiting,
        };

        _queue.Add(item);
        var row = new DownloadQueueItemRow(item, ContentWidth);
        _rows[item] = row;
        _queueFlow.Controls.Add(row);
        UpdateQueueCount();
        UpdateActionInfo();

        _urlBox.Clear();
        _log.Log(T($"[Queue] Đã thêm vào hàng đợi: {url}", $"[Queue] Added to queue: {url}"), LogLevel.Info);

        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        var maxWorkers = _settingsService.Current.ConcurrentDownloads;
        if (_activeWorkers >= maxWorkers) return;

        if (_cts == null || _cts.IsCancellationRequested)
            _cts = new CancellationTokenSource();

        Interlocked.Increment(ref _activeWorkers);
        var ct = _cts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                DownloadItem? next = null;
                await _pickLock.WaitAsync(ct);
                try
                {
                    next = _queue.FirstOrDefault(i => i.Status == DownloadStatus.Waiting);
                    if (next is not null)
                        next.Status = DownloadStatus.Downloading; // claim trước khi release lock
                }
                finally
                {
                    _pickLock.Release();
                }

                if (next is null) break;
                await DownloadOneAsync(next, ct);
            }
        }
        catch (OperationCanceledException)
        {
            if (Interlocked.Decrement(ref _activeWorkers) == 0)
            {
                _log.Log(T("[yt-dlp] Đã dừng theo yêu cầu", "[yt-dlp] Stopped by user"), LogLevel.Warn);
                _cts?.Dispose();
                _cts = null;
            }
            return;
        }

        if (Interlocked.Decrement(ref _activeWorkers) == 0)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task DownloadOneAsync(DownloadItem item, CancellationToken ct)
    {
        // Status đã được set = Downloading bởi worker trong ProcessQueueAsync
        RefreshRow(item);
        UpdateActionInfo();
        _log.Log(T($"[yt-dlp] Đang tải: {item.Url}", $"[yt-dlp] Downloading: {item.Url}"), LogLevel.Info);

        var savedCookie = await _cookieService.LoadCookieAsync();
        // Chỉ gửi TikTok session cookie cho TikTok — các nguồn khác (Instagram, Facebook, YouTube)
        // dùng yt-dlp không cookie hoặc cần cookie riêng của nền tảng đó.
        var isTikTok = item.Url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);
        var rawCookie = isTikTok ? savedCookie?.RawCookieString : null;

        var info = await _ytDlp.GetInfoAsync(item.Url, rawCookie, ct);
        if (info is not null)
        {
            item.Title = info.Title;
            item.Quality = info.Quality;
            item.FileSize = info.FileSize;
            item.FileSizeBytes = info.FileSizeBytes;
            RefreshRow(item);
        }

        var progress = new Progress<DownloadProgressInfo>(p =>
        {
            item.Progress = p.Percent;
            RefreshRow(item);
        });

        try
        {
            await _ytDlp.DownloadAsync(item, progress, rawCookie, ct);
            item.Status = DownloadStatus.Done;
            item.Progress = 100;
            _log.Log($"[Done] {item.Title} → {item.OutputDir}", LogLevel.Ok);
            await _historyService.AddAsync(new HistoryItem
            {
                Type = "Download",
                Title = item.Title,
                Detail = $"{item.Format.ToUpperInvariant()} · {item.Quality} · {item.FileSize}",
                Success = true,
                FileSizeBytes = item.FileSizeBytes,
            });
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Waiting;
            RefreshRow(item);
            throw;
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Error;
            item.ErrorMessage = ex.Message;
            _log.Log(T($"[Lỗi] {item.Title}: {ex.Message}", $"[Error] {item.Title}: {ex.Message}"), LogLevel.Error);
            await _historyService.AddAsync(new HistoryItem
            {
                Type = "Download",
                Title = item.Title,
                Detail = ex.Message,
                Success = false,
            });
        }

        RefreshRow(item);
        UpdateActionInfo();
    }

    private void RefreshRow(DownloadItem item)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshRowCore(item));
        }
        else
        {
            RefreshRowCore(item);
        }
    }

    private void RefreshRowCore(DownloadItem item)
    {
        if (_rows.TryGetValue(item, out var row))
        {
            row.Refresh(item);
        }
    }

    private void UpdateQueueCount()
    {
        _queueCountLabel.Text = T($"Hàng đợi download · {_queue.Count} mục", $"Download queue · {_queue.Count} items");
    }

    private void UpdateActionInfo()
    {
        void Update()
        {
            var waiting = _queue.Count(i => i.Status == DownloadStatus.Waiting);
            var downloading = _queue.Count(i => i.Status == DownloadStatus.Downloading);
            var done = _queue.Count(i => i.Status == DownloadStatus.Done);
            _actionInfo.Text = T($"{waiting} chờ · {downloading} đang tải · {done} xong", $"{waiting} waiting · {downloading} downloading · {done} done");
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

    private void StopAll()
    {
        _cts?.Cancel();
        if (_activeWorkers > 0)
            _log.Log(T("[yt-dlp] Đang dừng download hiện tại...", "[yt-dlp] Stopping current download..."), LogLevel.Warn);

        foreach (var item in _queue.Where(i => i.Status == DownloadStatus.Waiting))
        {
            item.Status = DownloadStatus.Error;
            item.ErrorMessage = T("Đã huỷ", "Cancelled");
            RefreshRow(item);
        }

        UpdateActionInfo();
    }
}
