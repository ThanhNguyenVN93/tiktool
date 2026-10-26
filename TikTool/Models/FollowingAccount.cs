namespace TikTool.Models;

public enum UnfollowStatus
{
    Pending,
    Unfollowing,
    Done,
    Failed,
}

/// <summary>One account from the current user's TikTok following list.</summary>
public class FollowingAccount
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int VideoCount { get; set; } = -1; // -1 = chưa kiểm tra
    public long? LastPostUnixSeconds { get; set; } // null = chưa biết hoặc chưa từng đăng

    public int? DaysSinceLastPost => LastPostUnixSeconds is long s
        ? (int)(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(s)).TotalDays
        : null;

    // Set bởi UnfollowPage theo tiêu chí người dùng chọn (0 video, hoặc không đăng quá N ngày)
    // — không tự tính cứng ở đây vì "kênh chết" là một lựa chọn của người dùng, không phải một
    // sự thật cố định của dữ liệu.
    public bool IsDead { get; set; }

    public bool IsSelected { get; set; }
    public UnfollowStatus Status { get; set; } = UnfollowStatus.Pending;
    public string? ErrorMessage { get; set; }
}
