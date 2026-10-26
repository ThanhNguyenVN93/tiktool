using TikTool.Forms;

namespace TikTool.Pages;

internal class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title, string subtitle)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var subLabel = new Label
        {
            Text = subtitle,
            Font = Theme.FontRegular,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 52)
        };

        Controls.Add(subLabel);
        Controls.Add(titleLabel);
    }
}
