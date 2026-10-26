using Guna.UI2.WinForms;

namespace TikTool.Forms;

/// <summary>
/// Collapsible "method" card used on CookiePage. Click the header to expand/collapse;
/// wire up <see cref="OnExpand"/> across a group of cards to collapse the others (accordion).
/// Header/content use explicit Location/Height (no Dock) so the card's own Height can be set
/// directly in Expand()/Collapse() — see MainForm/CookiePage history for why Dock ordering
/// inside a dynamically-resized container is not worth the risk here.
/// </summary>
public sealed class CookieMethodCard : Guna2Panel
{
    private const int CollapsedHeight = 64;
    private const int HeaderHeight = 54;

    private readonly Panel _content;
    private readonly Label _badge;

    public event Action? OnExpand;

    public bool IsExpanded => _content.Visible;

    public CookieMethodCard(int width, string icon, string title, string subtitle, string badgeText, Color badgeColor)
    {
        Width = width;
        Height = CollapsedHeight;
        BorderRadius = 10;
        FillColor = Theme.Panel;
        BorderColor = Theme.Border;
        BorderThickness = 1;
        Margin = new Padding(0, 0, 0, 10);
        Cursor = Cursors.Hand;

        var iconLabel = new Label { Text = icon, Font = new Font("Segoe UI", 16f), AutoSize = false, Size = new Size(30, 26), Location = new Point(16, 15), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Panel };
        var titleLabel = new Label { Text = title, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White, AutoSize = false, Size = new Size(width - 200, 20), Location = new Point(50, 13), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Panel };
        var subLabel = new Label { Text = subtitle, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.TextMuted, AutoSize = false, Size = new Size(width - 100, 18), Location = new Point(50, 33), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Panel };
        _badge = new Label { Text = badgeText, Font = Theme.FontLabel, ForeColor = badgeColor, AutoSize = false, Size = new Size(140, 20), Location = new Point(width - 156, 16), TextAlign = ContentAlignment.MiddleRight, BackColor = Theme.Panel };

        Controls.Add(iconLabel);
        Controls.Add(titleLabel);
        Controls.Add(subLabel);
        Controls.Add(_badge);

        _content = new Panel { Location = new Point(16, HeaderHeight), Width = width - 32, AutoSize = true, BackColor = Theme.Panel, Visible = false };
        Controls.Add(_content);

        EventHandler toggle = (_, _) => Toggle();
        Click += toggle;
        foreach (var c in new Control[] { iconLabel, titleLabel, subLabel, _badge })
        {
            c.Cursor = Cursors.Hand;
            c.Click += toggle;
        }
    }

    /// <summary>Sets the expandable body content; each control should already have its own
    /// Width/Height/Margin set (Location.Y is assigned here by stacking them top-to-bottom).</summary>
    public void SetContent(params Control[] controls)
    {
        _content.Controls.Clear();
        var y = 0;
        foreach (var c in controls)
        {
            y += c.Margin.Top;
            c.Location = new Point(0, y);
            _content.Controls.Add(c);
            y += c.Height + c.Margin.Bottom;
        }

        _content.Height = y;
        if (IsExpanded)
        {
            Height = HeaderHeight + _content.Height + 16;
        }
    }

    public void Expand()
    {
        if (IsExpanded)
        {
            return;
        }

        _content.Visible = true;
        Height = HeaderHeight + _content.Height + 16;
        BorderColor = Theme.Accent;
        OnExpand?.Invoke();
    }

    public void Collapse()
    {
        if (!IsExpanded)
        {
            return;
        }

        _content.Visible = false;
        Height = CollapsedHeight;
        BorderColor = Theme.Border;
    }

    private void Toggle()
    {
        if (IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }
}
