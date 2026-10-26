namespace TikTool.Models;

public class HistoryItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Type { get; set; } = ""; // "Download" | "Unfollow"
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Success { get; set; }
    public long FileSizeBytes { get; set; } // dùng cho stat "tổng GB", 0 nếu Unfollow
}
