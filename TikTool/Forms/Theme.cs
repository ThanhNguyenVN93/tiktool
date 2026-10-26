namespace TikTool.Forms;

internal static class Theme
{
    public static readonly Color Background = ColorTranslator.FromHtml("#0f0f0f");
    public static readonly Color Sidebar = ColorTranslator.FromHtml("#0d0d0d");
    public static readonly Color Panel = ColorTranslator.FromHtml("#141414");
    public static readonly Color Border = ColorTranslator.FromHtml("#1e1e1e");
    public static readonly Color BorderLight = ColorTranslator.FromHtml("#252525");
    public static readonly Color Accent = ColorTranslator.FromHtml("#ff2d55");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#ff4466");
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#e8e8e8");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#888888");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#555555");
    public static readonly Color Green = ColorTranslator.FromHtml("#22c55e");
    public static readonly Color Blue = ColorTranslator.FromHtml("#60a5fa");
    public static readonly Color Orange = ColorTranslator.FromHtml("#fb923c");

    public static readonly Font FontRegular = new("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font FontMedium = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font FontTitle = new("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font FontLabel = new("Segoe UI", 7.5f, FontStyle.Bold);
}
