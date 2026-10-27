using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Pages;

internal sealed class HistoryPage : UserControl
{
    private const int ContentWidth = 700;

    private readonly HistoryService _historyService;
    private string _activeFilter = "all"; // all | today | week | month

    private Label _statDownloads = null!;
    private Label _statUnfollows = null!;
    private Label _statSize = null!;
    private readonly List<Guna2Button> _filterChips = new();
    private FlowLayoutPanel _listFlow = null!;
    private Label _emptyLabel = null!;

    public HistoryPage(HistoryService historyService)
    {
        _historyService = historyService;

        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
        Load += (_, _) => RefreshList();
        VisibleChanged += (_, _) =>
        {
            if (Visible && IsHandleCreated) RefreshList();
        };
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
            Text = T("Lịch sử", "History"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        });
        header.Controls.Add(new Label
        {
            Text = T("Toàn bộ lượt tải video và bỏ theo dõi đã thực hiện (tối đa 500 mục gần nhất)",
                     "All downloads and unfollows (up to 500 most recent)"),
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

        flow.Controls.Add(BuildStatRow());
        flow.Controls.Add(BuildFilterRow());
        flow.Controls.Add(BuildListSection());

        scroll.Controls.Add(flow);
        return scroll;
    }

    private Control BuildStatRow()
    {
        var row = new Panel { Width = ContentWidth, Height = 76, Margin = new Padding(0, 0, 0, 16) };
        var cardWidth = (ContentWidth - 16) / 3;

        var (card1, label1) = BuildStatCard("🎬", T("Video đã tải","Videos downloaded"), cardWidth, 0);
        var (card2, label2) = BuildStatCard("🚫", T("Đã bỏ theo dõi","Unfollowed"), cardWidth, cardWidth + 8);
        var (card3, label3) = BuildStatCard("💾", T("Tổng dung lượng","Total size"), cardWidth, (cardWidth + 8) * 2);

        _statDownloads = label1;
        _statUnfollows = label2;
        _statSize = label3;

        row.Controls.Add(card1);
        row.Controls.Add(card2);
        row.Controls.Add(card3);
        return row;
    }

    private static (Guna2Panel Card, Label ValueLabel) BuildStatCard(string icon, string title, int width, int x)
    {
        var card = new Guna2Panel
        {
            Size = new Size(width, 76),
            Location = new Point(x, 0),
            BorderRadius = 8,
            FillColor = Theme.Panel,
            BorderColor = Theme.Border,
            BorderThickness = 1,
        };

        var iconLabel = new Label { Text = icon, Font = new Font("Segoe UI Emoji", 14f), AutoSize = true, Location = new Point(12, 10), BackColor = Theme.Panel };
        var valueLabel = new Label { Text = "0", Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(12, 32), BackColor = Theme.Panel };
        var titleLabel = new Label { Text = title, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(46, 14), BackColor = Theme.Panel };

        card.Controls.Add(iconLabel);
        card.Controls.Add(titleLabel);
        card.Controls.Add(valueLabel);
        return (card, valueLabel);
    }

    private Control BuildFilterRow()
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        row.Controls.Add(BuildFilterChip("all", T("Tất cả","All")));
        row.Controls.Add(BuildFilterChip("unfollow", T("Đã Unfollow","Unfollowed")));
        row.Controls.Add(BuildFilterChip("today", T("Hôm nay","Today")));
        row.Controls.Add(BuildFilterChip("week", T("Tuần này","This week")));
        row.Controls.Add(BuildFilterChip("month", T("Tháng này","This month")));
        return row;
    }

    private Guna2Button BuildFilterChip(string key, string text)
    {
        var chip = new Guna2Button
        {
            Text = text,
            Tag = key,
            Font = Theme.FontMedium,
            Size = new Size(90, 28),
            Margin = new Padding(0, 0, 8, 0),
            FillColor = key == _activeFilter ? Theme.Accent : ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = key == _activeFilter ? Color.White : Theme.TextSecondary,
            BorderRadius = 14,
            Cursor = Cursors.Hand,
        };
        chip.Click += (_, _) =>
        {
            _activeFilter = key;
            foreach (var c in _filterChips)
            {
                var active = (string)c.Tag! == _activeFilter;
                c.FillColor = active ? Theme.Accent : ColorTranslator.FromHtml("#1a1a1a");
                c.ForeColor = active ? Color.White : Theme.TextSecondary;
            }

            RefreshList();
        };
        _filterChips.Add(chip);
        return chip;
    }

    private Control BuildListSection()
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true };

        _listFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = ContentWidth,
            BackColor = Color.Transparent,
        };

        _emptyLabel = new Label
        {
            Text = T("Chưa có lịch sử nào.", "No history yet."),
            Font = Theme.FontRegular,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Visible = false,
        };

        wrap.Controls.Add(_listFlow);
        wrap.Controls.Add(_emptyLabel);
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

        var btnExport = new Guna2Button
        {
            Text = T("Xuất CSV","Export CSV"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(560, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnExport.Click += (_, _) => ExportCsv();

        var btnExportUnfollow = new Guna2Button
        {
            Text = T("↩ DS Unfollow","↩ Unfollow List"),
            Font = Theme.FontMedium,
            Size = new Size(130, 32),
            Location = new Point(420, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnExportUnfollow.Click += (_, _) => ExportUnfollowCsv();

        var btnClear = new Guna2Button
        {
            Text = T("Xóa lịch sử","Clear history"),
            Font = Theme.FontMedium,
            Size = new Size(110, 32),
            Location = new Point(300, 12),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = ColorTranslator.FromHtml("#f87171"),
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        btnClear.Click += async (_, _) => await ClearHistoryAsync();

        bar.Controls.Add(btnClear);
        bar.Controls.Add(btnExportUnfollow);
        bar.Controls.Add(btnExport);
        bar.Controls.Add(sep);
        return bar;
    }

    private void RefreshList()
    {
        var items = FilterItems(_historyService.Items);

        _listFlow.SuspendLayout();
        _listFlow.Controls.Clear();
        foreach (var item in items)
        {
            _listFlow.Controls.Add(BuildHistoryRow(item));
        }


        _listFlow.ResumeLayout(true);
        _emptyLabel.Visible = items.Count == 0;

        UpdateStats();
    }

    private List<HistoryItem> FilterItems(IReadOnlyList<HistoryItem> all)
    {
        var now = DateTime.Now;
        return _activeFilter switch
        {
            "unfollow" => all.Where(i => i.Type is "Unfollow" or "Unsubscribe").ToList(),
            "today" => all.Where(i => i.Timestamp.Date == now.Date).ToList(),
            "week" => all.Where(i => i.Timestamp >= now.AddDays(-7)).ToList(),
            "month" => all.Where(i => i.Timestamp >= now.AddMonths(-1)).ToList(),
            _ => all.ToList(),
        };
    }

    private void UpdateStats()
    {
        var all = _historyService.Items;
        _statDownloads.Text = all.Count(i => i.Type == "Download").ToString();
        _statUnfollows.Text = all.Count(i => i.Success && (i.Type is "Unfollow" or "Unsubscribe")).ToString();

        var totalBytes = all.Sum(i => i.FileSizeBytes);
        _statSize.Text = $"{totalBytes / 1024.0 / 1024.0 / 1024.0:0.00} GB";
    }

    private Control BuildHistoryRow(HistoryItem item)
    {
        var isUnfollow = item.Type == "Unfollow" && item.Success;
        var youTubeUrl = item.Type == "Unsubscribe" && item.Success ? ExtractYouTubeUrl(item.Detail) : null;
        var hasUndo = isUnfollow || youTubeUrl is not null;
        var row = new Guna2Panel
        {
            Width = ContentWidth,
            Height = 48,
            BorderRadius = 8,
            FillColor = Theme.Panel,
            BorderColor = Theme.Border,
            BorderThickness = 1,
            Margin = new Padding(0, 0, 0, 6),
        };

        var icon = item.Type == "Download" ? "🎬" : "🚫";
        var iconLabel = new Label { Text = icon, Font = new Font("Segoe UI Emoji", 12f), Size = new Size(28, 28), Location = new Point(10, 10), TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Panel };

        // Unfollow rows with a "Follow lại" button need less width for title
        var titleWidth = hasUndo ? ContentWidth - 370 : ContentWidth - 320;

        var titleLabel = new Label
        {
            Text = item.Title,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = false,
            Size = new Size(titleWidth, 18),
            Location = new Point(46, 6),
            AutoEllipsis = true,
            BackColor = Theme.Panel,
        };
        var detailLabel = new Label
        {
            Text = item.Detail,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(titleWidth, 16),
            Location = new Point(46, 24),
            AutoEllipsis = true,
            BackColor = Theme.Panel,
        };

        var timeLabel = new Label
        {
            Text = item.Timestamp.ToString("HH:mm dd/MM"),
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(90, 16),
            Location = new Point(ContentWidth - 180, 16),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };
        var statusLabel = new Label
        {
            Text = item.Success ? "✓" : "✗",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = item.Success ? Theme.Green : ColorTranslator.FromHtml("#f87171"),
            AutoSize = false,
            Size = new Size(30, 24),
            Location = new Point(ContentWidth - 46, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
        };

        row.Controls.Add(iconLabel);
        row.Controls.Add(titleLabel);
        row.Controls.Add(detailLabel);
        row.Controls.Add(timeLabel);
        row.Controls.Add(statusLabel);

        if (isUnfollow)
        {
            // Title là "@username" — bỏ "@" để lấy username
            var username = item.Title.TrimStart('@');
            var btnRefollow = new Guna2Button
            {
                Text = T("↩ Follow lại", "↩ Re-follow"),
                Font = Theme.FontLabel,
                Size = new Size(82, 26),
                Location = new Point(ContentWidth - 270, 11),
                FillColor = ColorTranslator.FromHtml("#1a1a1a"),
                ForeColor = Theme.TextSecondary,
                BorderRadius = 6,
                Cursor = Cursors.Hand,
            };
            btnRefollow.Click += (_, _) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"https://www.tiktok.com/@{username}",
                    UseShellExecute = true,
                });
            };
            row.Controls.Add(btnRefollow);
        }

        if (youTubeUrl is not null)
        {
            var btnResubscribe = new Guna2Button
            {
                Text = T("↩ Đăng ký lại", "↩ Re-subscribe"),
                Font = Theme.FontLabel,
                Size = new Size(96, 26),
                Location = new Point(ContentWidth - 284, 11),
                FillColor = ColorTranslator.FromHtml("#1a1a1a"),
                ForeColor = Theme.TextSecondary,
                BorderRadius = 6,
                Cursor = Cursors.Hand,
            };
            btnResubscribe.Click += (_, _) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = youTubeUrl, UseShellExecute = true });
            };
            row.Controls.Add(btnResubscribe);
        }

        return row;
    }

    /// <summary>A YouTube unsubscribe is stored as "YouTube · {channel link}" (plus " · error" when it failed).</summary>
    private static string? ExtractYouTubeUrl(string detail)
    {
        var parts = (detail ?? "").Split(" · ");
        return parts.Length >= 2 && parts[0] == "YouTube" && parts[1].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? parts[1] : null;
    }

    private async Task ClearHistoryAsync()
    {
        var confirm = MessageBox.Show(
            T("Xóa toàn bộ lịch sử? Hành động này không thể hoàn tác.",
              "Clear all history? This action cannot be undone."),
            "Social Toolkit",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await _historyService.ClearAsync();
        RefreshList();
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"tiktool-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        File.WriteAllText(dialog.FileName, _historyService.ExportCsv());
        MessageBox.Show(T($"Đã xuất {dialog.FileName}", $"Exported {dialog.FileName}"), "Social Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportUnfollowCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"tiktool-unfollow-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Title = T("Xuất danh sách kênh đã unfollow", "Export unfollowed channels list"),
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Username,Detail,Timestamp");
        foreach (var item in _historyService.Items.Where(i => i.Type == "Unfollow" && i.Success))
        {
            var username = item.Title.TrimStart('@');
            sb.AppendLine($"@{username},{CsvEscape(item.Detail)},{item.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
        MessageBox.Show(T($"Đã xuất danh sách unfollow → {dialog.FileName}\nMở trang TikTok của từng kênh để follow lại.",
                          $"Exported unfollow list → {dialog.FileName}\nOpen each channel's TikTok page to re-follow."),
                        "Social Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

}
