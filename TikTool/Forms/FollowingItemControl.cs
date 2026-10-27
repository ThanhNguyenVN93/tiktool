using Guna.UI2.WinForms;
using TikTool.Models;

namespace TikTool.Forms;

/// <summary>One row in the following list — checkbox, colored initial avatar, name/meta,
/// dead/active/unfollow-status badge. Clicking anywhere on the row (not just the checkbox)
/// toggles selection.</summary>
public sealed class FollowingItemControl : Guna2Panel
{
    public FollowingAccount Account { get; }
    public event Action? SelectionChanged;
    /// <summary>Raised khi user toggle whitelist từ right-click menu — true = thêm, false = xóa.</summary>
    public event Action<FollowingAccount, bool>? WhitelistToggled;

    private readonly CheckBox _checkbox;
    private readonly Label _metaLabel;
    private readonly Label _statusBadge;
    private readonly Label _whitelistIcon;

    public FollowingItemControl(FollowingAccount account, int width)
    {
        Account = account;
        Width = width;
        Height = 52;
        BorderRadius = 8;
        FillColor = Theme.Panel;
        BorderColor = Theme.Border;
        BorderThickness = 1;
        Margin = new Padding(0, 0, 0, 6);
        Cursor = Cursors.Hand;

        _checkbox = new CheckBox
        {
            Location = new Point(10, 16),
            Size = new Size(20, 20),
            Checked = account.IsSelected,
            BackColor = Theme.Panel,
            FlatStyle = FlatStyle.Flat,
        };
        _checkbox.CheckedChanged += (_, _) =>
        {
            Account.IsSelected = _checkbox.Checked;
            SelectionChanged?.Invoke();
        };

        var avatarColor = ColorFromSeed(account.Username);
        var initial = string.IsNullOrEmpty(account.Nickname) ? "?" : char.ToUpperInvariant(account.Nickname[0]).ToString();
        var avatar = new Guna2Panel
        {
            Size = new Size(32, 32),
            Location = new Point(42, 10),
            BorderRadius = 16,
            FillColor = avatarColor,
        };
        avatar.Controls.Add(new Label
        {
            Text = initial,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = avatarColor,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        });

        var nameLabel = new Label
        {
            Text = string.IsNullOrEmpty(account.Nickname) ? $"@{account.Username}" : account.Nickname,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = false,
            Size = new Size(width - 260, 18),
            Location = new Point(86, 8),
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
            Location = new Point(86, 26),
            BackColor = Theme.Panel,
        };

        _statusBadge = new Label
        {
            Text = BadgeText(),
            Font = Theme.FontLabel,
            ForeColor = BadgeColor(),
            AutoSize = false,
            Size = new Size(120, 22),
            Location = new Point(width - 136, 15),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Theme.Panel,
        };

        _whitelistIcon = new Label
        {
            Text = account.IsWhitelisted ? "🔒" : "",
            Font = new Font("Segoe UI", 9f),
            ForeColor = ColorTranslator.FromHtml("#fbbf24"),
            AutoSize = false,
            Size = new Size(24, 22),
            Location = new Point(width - 164, 15),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
        };

        Controls.Add(_checkbox);
        Controls.Add(avatar);
        Controls.Add(nameLabel);
        Controls.Add(_metaLabel);
        Controls.Add(_statusBadge);
        Controls.Add(_whitelistIcon);

        EventHandler toggle = (_, _) => _checkbox.Checked = !_checkbox.Checked;
        Click += toggle;
        nameLabel.Click += toggle;
        _metaLabel.Click += toggle;

        // Right-click context menu để whitelist/unwhitelist
        var ctxMenu = new ContextMenuStrip();
        ctxMenu.Opening += (_, _) =>
        {
            ctxMenu.Items.Clear();
            if (Account.IsWhitelisted)
            {
                ctxMenu.Items.Add(T("🔓  Bỏ bảo vệ kênh này", "🔓  Remove from whitelist"), null, (_, _) =>
                {
                    Account.IsWhitelisted = false;
                    WhitelistToggled?.Invoke(Account, false);
                    RefreshStatus();
                });
            }
            else
            {
                ctxMenu.Items.Add(T("🔒  Bảo vệ kênh này (không unfollow)", "🔒  Protect this channel (never unfollow)"), null, (_, _) =>
                {
                    Account.IsWhitelisted = true;
                    Account.IsSelected = false;
                    _checkbox.Checked = false;
                    WhitelistToggled?.Invoke(Account, true);
                    RefreshStatus();
                });
            }
        };
        ContextMenuStrip = ctxMenu;
    }

    private static string T(string vi, string _en) => vi; // stub — theo pattern của UnfollowPage

    /// <summary>Called after an async video-count check or unfollow attempt updates the
    /// underlying <see cref="FollowingAccount"/> — repaints the meta line and status badge.</summary>
    public void RefreshStatus()
    {
        _metaLabel.Text = BuildMetaText();
        _statusBadge.Text = BadgeText();
        _statusBadge.ForeColor = BadgeColor();
        _whitelistIcon.Text = Account.IsWhitelisted ? "🔒" : "";
        _checkbox.Enabled = !Account.IsWhitelisted;
    }

    private string BuildMetaText()
    {
        if (Account.VideoCount < 0)
        {
            return $"@{Account.Username} · chưa kiểm tra";
        }

        if (Account.VideoCount == 0)
        {
            return $"@{Account.Username} · chưa từng đăng video";
        }

        var activityPart = Account.DaysSinceLastPost is int days
            ? $"{Account.VideoCount} video · đăng gần nhất {days} ngày trước"
            : $"{Account.VideoCount} video";
        return $"@{Account.Username} · {activityPart}";
    }

    private string BadgeText() => Account.Status switch
    {
        UnfollowStatus.Unfollowing => "Đang xử lý...",
        UnfollowStatus.Done => "✓ Đã unfollow",
        UnfollowStatus.Failed => "✗ Lỗi",
        _ => Account.VideoCount < 0 ? "Chưa kiểm tra" : Account.IsDead ? "💀 Kênh chết" : "Đang hoạt động",
    };

    private Color BadgeColor() => Account.Status switch
    {
        UnfollowStatus.Unfollowing => Theme.Accent,
        UnfollowStatus.Done => Theme.Green,
        UnfollowStatus.Failed => ColorTranslator.FromHtml("#f87171"),
        _ => Account.VideoCount < 0 ? Theme.TextMuted : Account.IsDead ? ColorTranslator.FromHtml("#f87171") : Theme.TextMuted,
    };

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
