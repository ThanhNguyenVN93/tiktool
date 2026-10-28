using System.Diagnostics;
using Guna.UI2.WinForms;
using TikTool.Models;

namespace TikTool.Forms;

/// <summary>One subscribed YouTube channel: checkbox, logo, channel name (a link that opens the channel), a meta line with the
/// last upload and subscriber count, and a status badge. Clicking the row (but not the name) toggles the selection.
/// Right-click: protect the channel so it can never be unsubscribed.</summary>
public sealed class YouTubeChannelRow : Guna2Panel
{
    public YouTubeChannel Channel { get; }
    public event Action? SelectionChanged;
    public event Action<YouTubeChannel, bool>? ProtectToggled;

    private readonly CheckBox _checkbox;
    private readonly Label _nameLabel;
    private readonly Label _metaLabel;
    private readonly Label _statusBadge;
    private readonly Label _lockIcon;
    private readonly Guna2Panel _avatarHost;
    private bool _avatarShown;

    // Decoded logos are shared between rows: the list is rebuilt on every filter or sort change.
    private static readonly Dictionary<string, Image> ImageCache = new();

    public YouTubeChannelRow(YouTubeChannel channel, int width)
    {
        Channel = channel;
        Width = width;
        Height = 56;
        BorderRadius = 8;
        FillColor = Theme.Panel;
        BorderColor = Theme.Border;
        BorderThickness = 1;
        Margin = new Padding(0, 0, 0, 6);
        Cursor = Cursors.Hand;

        _checkbox = new CheckBox
        {
            Location = new Point(10, 18),
            Size = new Size(20, 20),
            Checked = channel.IsSelected,
            BackColor = Theme.Panel,
            FlatStyle = FlatStyle.Flat,
        };
        _checkbox.CheckedChanged += (_, _) =>
        {
            Channel.IsSelected = _checkbox.Checked;
            SelectionChanged?.Invoke();
        };

        var avatarColor = ColorFromSeed(channel.Name);
        _avatarHost = new Guna2Panel
        {
            Size = new Size(36, 36),
            Location = new Point(42, 10),
            BorderRadius = 18,
            FillColor = avatarColor,
        };
        _avatarHost.Controls.Add(new Label
        {
            Text = string.IsNullOrEmpty(channel.Name) ? "?" : char.ToUpperInvariant(channel.Name[0]).ToString(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = avatarColor,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        });

        _nameLabel = new Label
        {
            Text = channel.Name,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = false,
            Size = new Size(width - 280, 18),
            Location = new Point(90, 9),
            AutoEllipsis = true,
            BackColor = Theme.Panel,
            Cursor = Cursors.Hand,
        };
        // The name is a link to the channel: hover underlines it, click opens it in the default browser (not a toggle).
        _nameLabel.MouseEnter += (_, _) => _nameLabel.Font = new Font(_nameLabel.Font, FontStyle.Bold | FontStyle.Underline);
        _nameLabel.MouseLeave += (_, _) => _nameLabel.Font = new Font(_nameLabel.Font, FontStyle.Bold);
        _nameLabel.Click += (_, _) => OpenChannel();

        _metaLabel = new Label
        {
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(width - 280, 16),
            Location = new Point(90, 29),
            BackColor = Theme.Panel,
        };

        _statusBadge = new Label
        {
            Font = Theme.FontLabel,
            AutoSize = false,
            Size = new Size(130, 22),
            Location = new Point(width - 146, 17),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };

        _lockIcon = new Label
        {
            Font = new Font("Segoe UI", 9f),
            ForeColor = ColorTranslator.FromHtml("#fbbf24"),
            AutoSize = false,
            Size = new Size(24, 22),
            Location = new Point(width - 174, 17),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
        };

        Controls.Add(_checkbox);
        Controls.Add(_avatarHost);
        Controls.Add(_nameLabel);
        Controls.Add(_metaLabel);
        Controls.Add(_statusBadge);
        Controls.Add(_lockIcon);

        EventHandler toggle = (_, _) => _checkbox.Checked = !_checkbox.Checked;
        Click += toggle;
        _metaLabel.Click += toggle;

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add(
                Channel.IsProtected
                    ? T("🔓  Bỏ bảo vệ kênh này", "🔓  Remove protection")
                    : T("🔒  Bảo vệ kênh này (không bao giờ hủy đăng ký)", "🔒  Protect this channel (never unsubscribe)"),
                null,
                (_, _) =>
                {
                    Channel.IsProtected = !Channel.IsProtected;
                    if (Channel.IsProtected)
                    {
                        Channel.IsSelected = false;
                        _checkbox.Checked = false;
                    }

                    ProtectToggled?.Invoke(Channel, Channel.IsProtected);
                    RefreshStatus();
                });
            menu.Items.Add(T("🌐  Mở kênh trong trình duyệt", "🌐  Open the channel in the browser"), null, (_, _) => OpenChannel());
            menu.Items.Add(T("📋  Sao chép link kênh", "📋  Copy the channel link"), null, (_, _) =>
            {
                try { Clipboard.SetText(Channel.Url); } catch { /* clipboard can be busy */ }
            });
        };
        ContextMenuStrip = menu;

        RefreshStatus();
    }

    private void OpenChannel()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Channel.Url, UseShellExecute = true });
        }
        catch
        {
            // no default browser registered - nothing sensible to do
        }
    }

    /// <summary>Repaints everything that can change after loading, an age check or an unsubscribe attempt.</summary>
    public void RefreshStatus()
    {
        TryApplyAvatar();
        _metaLabel.Text = BuildMeta();
        _statusBadge.Text = BadgeText();
        _statusBadge.ForeColor = BadgeColor();
        _lockIcon.Text = Channel.IsProtected ? "🔒" : "";
        _checkbox.Enabled = !Channel.IsProtected && Channel.Status != YouTubeChannelStatus.Done;
        if (_checkbox.Checked != Channel.IsSelected)
        {
            _checkbox.Checked = Channel.IsSelected;
        }
    }

    private string BuildMeta()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Channel.Handle))
        {
            parts.Add(Channel.Handle);
        }

        if (!Channel.AgeChecked)
        {
            parts.Add(T("đang đọc ngày đăng…", "reading the upload date…"));
        }
        else if (Channel.NoVideos)
        {
            parts.Add(Channel.HasShorts ? T("chỉ có Shorts", "Shorts only") : T("chưa có video", "no videos"));
        }
        else if (!string.IsNullOrEmpty(Channel.LastUploadText))
        {
            var age = FormatAge(Channel.LastUploadText);
            parts.Add(T($"video mới nhất {age.Vi}", $"latest video {age.En}"));
        }

        if (Channel.HasShorts && !Channel.NoVideos)
        {
            parts.Add(T("có Shorts", "posts Shorts"));
        }

        if (!string.IsNullOrEmpty(Channel.Subscribers))
        {
            parts.Add(Channel.Subscribers);
        }

        return string.Join(" · ", parts);
    }

    private static (string Vi, string En) FormatAge(string text) => TikTool.Services.AgeText.Format(text);

    private string BadgeText() => Channel.Status switch
    {
        YouTubeChannelStatus.Unsubscribing => T("Đang xử lý...", "Working..."),
        YouTubeChannelStatus.Done => T("✓ Đã hủy đăng ký", "✓ Unsubscribed"),
        YouTubeChannelStatus.Failed => T("✗ Lỗi", "✗ Error"),
        _ => !Channel.AgeChecked ? T("Chưa kiểm tra", "Not checked")
            : Channel.IsUncertain ? T("Có Shorts — chưa rõ", "Has Shorts — unclear")
            : Channel.IsInactive ? T("Không hoạt động", "Inactive")
            : T("Đang hoạt động", "Active"),
    };

    private Color BadgeColor() => Channel.Status switch
    {
        YouTubeChannelStatus.Unsubscribing => Theme.Accent,
        YouTubeChannelStatus.Done => Theme.Green,
        YouTubeChannelStatus.Failed => ColorTranslator.FromHtml("#f87171"),
        _ => Channel.AgeChecked && Channel.IsUncertain ? Theme.Orange
            : Channel.AgeChecked && Channel.IsInactive ? ColorTranslator.FromHtml("#f87171") : Theme.TextMuted,
    };

    private static Image? LoadImage(string path)
    {
        lock (ImageCache)
        {
            if (ImageCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(path));
                using var source = Image.FromStream(ms);
                var copy = new Bitmap(source);
                ImageCache[path] = copy;
                return copy;
            }
            catch
            {
                return null;
            }
        }
    }

    private void TryApplyAvatar()
    {
        if (_avatarShown || string.IsNullOrEmpty(Channel.AvatarPath) || !File.Exists(Channel.AvatarPath))
        {
            return;
        }

        var image = LoadImage(Channel.AvatarPath);
        if (image is null)
        {
            return;
        }

        _avatarHost.Controls.Clear();
        _avatarHost.Controls.Add(new Guna2CirclePictureBox
        {
            Dock = DockStyle.Fill,
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        });
        _avatarShown = true;
    }

    private static Color ColorFromSeed(string seed)
    {
        var palette = new[]
        {
            ColorTranslator.FromHtml("#ff2d55"), ColorTranslator.FromHtml("#60a5fa"),
            ColorTranslator.FromHtml("#22c55e"), ColorTranslator.FromHtml("#fb923c"),
            ColorTranslator.FromHtml("#a78bfa"), ColorTranslator.FromHtml("#fbbf24"),
        };
        var hash = 0;
        foreach (var c in seed)
        {
            hash = hash * 31 + c;
        }

        return palette[Math.Abs(hash) % palette.Length];
    }
}