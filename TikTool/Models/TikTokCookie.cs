namespace TikTool.Models;

public class TikTokCookie
{
    public string SessionId { get; set; } = "";
    public string MsToken { get; set; } = "";
    public string TtWebId { get; set; } = "";
    public string RawCookieString { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string? Username { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysLeft => ExpiryDate is DateTime d ? (int)Math.Max(0, (d - DateTime.Now).TotalDays) : null;
    public string Source { get; set; } = "manual"; // chrome | edge | manual | login
}
