using System.Text.RegularExpressions;

namespace TikTool.Services;

/// <summary>YouTube writes how long ago a video was posted as "13h ago", "5d ago", "9mo ago", "3y ago" (or in full words,
/// "2 weeks ago"). This turns that coarse text into an approximate number of days and into a sentence in each language.</summary>
internal static class AgeText
{
    private const string Body = @"(\d+)\s*(second|minute|hour|day|week|month|year|sec|min|mo|[smhdwy])s?\s+ago";

    private static readonly Regex Anywhere = new(Body, RegexOptions.IgnoreCase);
    private static readonly Regex Whole = new("^" + Body + "$", RegexOptions.IgnoreCase);

    /// <summary>The first age phrase inside a longer page text.</summary>
    public static Match Find(string text) => Anywhere.Match(text ?? "");

    public static int ApproxDays(int amount, string unit) => unit.ToLowerInvariant() switch
    {
        "y" or "year" => amount * 365,
        "mo" or "month" => amount * 30,
        "w" or "week" => amount * 7,
        "d" or "day" => amount,
        _ => 0, // seconds, minutes, hours
    };

    /// <summary>The age phrase as a normal sentence: ("khoảng 3 năm trước", "about 3 years ago"). The figure is coarse, hence
    /// "about" for weeks, months and years. Text that is not an age phrase is returned unchanged.</summary>
    public static (string Vi, string En) Format(string text)
    {
        var m = Whole.Match(text ?? "");
        if (!m.Success)
        {
            return (text ?? "", text ?? "");
        }

        var n = int.Parse(m.Groups[1].Value);
        var (vi, en) = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "y" or "year" => ("năm", "year"),
            "mo" or "month" => ("tháng", "month"),
            "w" or "week" => ("tuần", "week"),
            "d" or "day" => ("ngày", "day"),
            "h" or "hour" => ("giờ", "hour"),
            "m" or "min" or "minute" => ("phút", "minute"),
            _ => ("giây", "second"),
        };

        var approx = en is "year" or "month" or "week";
        return ((approx ? "khoảng " : "") + $"{n} {vi} trước", $"{(approx ? "about " : "")}{n} {en}{(n == 1 ? "" : "s")} ago");
    }
}