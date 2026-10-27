using TikTool.Models;

namespace TikTool.Models;

/// <summary>Snapshot của một phiên Unfollow — lưu vào disk để resume sau khi crash/đóng app.
/// Cũng lưu daily unfollow counter để giới hạn số lần unfollow mỗi ngày.</summary>
public class SessionState
{
    // === Phiên Unfollow ===
    public List<FollowingAccount> Accounts { get; set; } = new();
    public int TotalFollowing { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;

    // === Daily quota ===
    public int UnfollowedToday { get; set; }
    public DateTime QuotaDate { get; set; } = DateTime.Today;

    // Helper: còn có thể resume không (< 24h cũ và còn account chưa xong)
    public bool IsResumable =>
        (DateTime.Now - SavedAt).TotalHours < 24
        && Accounts.Any(a => a.IsSelected && a.Status == UnfollowStatus.Pending);
}
