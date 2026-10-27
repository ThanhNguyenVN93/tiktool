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

    // relation value from TikTok's API / UNIVERSAL_DATA_FOR_REHYDRATION:
    //   0 = no relation, 1 = I follow them only, 2 = mutual (friend / follows back)
    // -1 = unknown (not yet fetched). Set during LoadFollowing (API interceptor) or CheckDead (profile visit).
    public int Relation { get; set; } = -1;

    /// <summary>True when the account follows back (mutual follow, TikTok relation == 2).
    /// Populated during list load or dead-channel scan. Unknown until then (Relation == -1).</summary>
    public bool IsFriend => Relation == 2;

    // Set bởi UnfollowPage theo tiêu chí người dùng chọn (0 video, hoặc không đăng quá N ngày)
    // — không tự tính cứng ở đây vì "kênh chết" là một lựa chọn của người dùng, không phải một
    // sự thật cố định của dữ liệu.
    public bool IsDead { get; set; }
    public bool IsWhitelisted { get; set; }

    /// <summary>Signed avatar link from TikTok's list API (expires, so it is downloaded right away).</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Local cached avatar file, when one has been downloaded.</summary>
    public string? AvatarPath { get; set; }

    public bool IsSelected { get; set; }
    public UnfollowStatus Status { get; set; } = UnfollowStatus.Pending;
    public string? ErrorMessage { get; set; }
}
