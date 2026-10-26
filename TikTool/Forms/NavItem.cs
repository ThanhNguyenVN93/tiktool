using Guna.UI2.WinForms;

namespace TikTool.Forms;

internal sealed class NavItem : Guna2Button
{
    public string PageKey { get; }
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            ApplyStyle();
        }
    }

    public NavItem(string pageKey, string icon, string label)
    {
        PageKey = pageKey;
        Text = $"  {icon}   {label}";
        TextAlign = HorizontalAlignment.Left;
        Height = 36;
        Width = 164;
        Margin = new Padding(8, 2, 8, 2);
        Font = Theme.FontMedium;
        BorderRadius = 6;
        Cursor = Cursors.Hand;
        Animated = true;
        HoverState.FillColor = ColorTranslator.FromHtml("#161616");
        HoverState.ForeColor = ColorTranslator.FromHtml("#cccccc");
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        if (_active)
        {
            FillColor = Color.FromArgb(31, Theme.Accent);
            ForeColor = Theme.Accent;
        }
        else
        {
            FillColor = Theme.Sidebar;
            ForeColor = Theme.TextMuted;
        }
    }
}
