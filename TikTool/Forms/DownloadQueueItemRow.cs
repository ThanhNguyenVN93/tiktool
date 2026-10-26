using Guna.UI2.WinForms;
using TikTool.Models;

namespace TikTool.Forms;

/// <summary>One row in the download queue list — thumb icon, title/meta, progress bar, status badge.</summary>
public sealed class DownloadQueueItemRow : Guna2Panel
{
    private readonly Label _titleLabel;
    private readonly Label _metaLabel;
    private readonly Guna2ProgressBar _progressBar;
    private readonly Label _statusBadge;

    public DownloadItem Item { get; }

    public DownloadQueueItemRow(DownloadItem item, int width)
    {
        Item = item;
        Width = width;
        Height = 60;
        BorderRadius = 8;
        FillColor = Theme.Panel;
        BorderColor = Theme.Border;
        BorderThickness = 1;
        Margin = new Padding(0, 0, 0, 6);

        var thumb = new Guna2Panel
        {
            Size = new Size(36, 36),
            Location = new Point(12, 12),
            BorderRadius = 6,
            FillColor = ColorTranslator.FromHtml("#1a1a2e"),
        };
        thumb.Controls.Add(new Label
        {
            Text = item.Format == "mp3" ? "🎵" : "🎬",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
        });

        _titleLabel = new Label
        {
            Text = item.Title,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Theme.TextPrimary,
            AutoSize = false,
            Size = new Size(width - 260, 18),
            Location = new Point(58, 8),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Theme.Panel,
        };
        _metaLabel = new Label
        {
            Text = BuildMetaText(),
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(width - 260, 16),
            Location = new Point(58, 26),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.Panel,
        };

        _progressBar = new Guna2ProgressBar
        {
            Location = new Point(58, 46),
            Size = new Size(width - 260, 6),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            ProgressColor = Theme.Accent,
            ProgressColor2 = ColorTranslator.FromHtml("#ff6b35"),
            FillColor = ColorTranslator.FromHtml("#1e1e1e"),
            Visible = false,
        };

        _statusBadge = new Label
        {
            Text = "Chờ",
            Font = Theme.FontLabel,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(90, 22),
            Location = new Point(width - 106, 19),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };

        Controls.Add(thumb);
        Controls.Add(_titleLabel);
        Controls.Add(_metaLabel);
        Controls.Add(_progressBar);
        Controls.Add(_statusBadge);
    }

    public void Refresh(DownloadItem item)
    {
        _titleLabel.Text = item.Title;
        _metaLabel.Text = BuildMetaText();

        switch (item.Status)
        {
            case DownloadStatus.Waiting:
                _progressBar.Visible = false;
                _statusBadge.Text = "Chờ";
                _statusBadge.ForeColor = Theme.TextMuted;
                break;
            case DownloadStatus.Downloading:
                _progressBar.Visible = true;
                _progressBar.Value = Math.Clamp((int)item.Progress, 0, 100);
                _statusBadge.Text = $"{item.Progress:0}%";
                _statusBadge.ForeColor = Theme.Accent;
                break;
            case DownloadStatus.Done:
                _progressBar.Visible = false;
                _statusBadge.Text = "✓ Xong";
                _statusBadge.ForeColor = Theme.Green;
                break;
            case DownloadStatus.Error:
                _progressBar.Visible = false;
                _statusBadge.Text = "✗ Lỗi";
                _statusBadge.ForeColor = ColorTranslator.FromHtml("#f87171");
                break;
        }
    }

    private string BuildMetaText()
    {
        var format = Item.Format.ToUpperInvariant();
        var parts = new List<string> { format };
        if (!string.IsNullOrEmpty(Item.Quality))
        {
            parts.Add(Item.Quality);
        }

        if (!string.IsNullOrEmpty(Item.FileSize))
        {
            parts.Add(Item.FileSize);
        }

        if (Item.Status == DownloadStatus.Error && Item.ErrorMessage is not null)
        {
            parts.Add(Item.ErrorMessage);
        }

        return string.Join(" · ", parts);
    }
}
