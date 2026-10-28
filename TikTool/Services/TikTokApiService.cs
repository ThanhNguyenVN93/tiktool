using TikTool.Models;

namespace TikTool.Services;

/// <summary>Thrown when TikTok signals rate limiting (HTTP 429, or status_code 2061 in the
/// response body) — the caller should stop the unfollow loop and let the user decide whether
/// to continue.</summary>
public sealed class RateLimitException : Exception
{
    public RateLimitException(string message) : base(message)
    {
    }
}

/// <summary>Calling TikTok's internal unfollow API directly (no browser) via plain HttpClient.
/// CONFIRMED LIVE (2026-09-13, twice now — first for `api/user/detail/`, now for this endpoint
/// too): TikTok accepts the request and returns a 2xx-looking response for an UNSIGNED call —
/// even from an authenticated session's own cookies — but silently does NOT perform the action,
/// because the real web client attaches an X-Bogus/msToken/X-Gnarly signature computed by
/// obfuscated JS that a bare HttpClient (or a manual fetch) never runs. Live-tested: this class's
/// `UnfollowAsync` reported success while the account was demonstrably still being followed
/// afterward. There is no way to produce that signature outside TikTok's own page JS, so this
/// path is retired — <see cref="UnfollowAsync"/> now always throws, which routes every unfollow
/// through <see cref="PlaywrightService.ClickUnfollowAsync"/> instead (a real, signed browser
/// session), via the existing fallback-on-exception logic in UnfollowPage. Video-count lookups
/// hit the same signature wall and were already moved to
/// <see cref="PlaywrightService.GetUserDetailAsync"/> for the same reason.</summary>
public sealed class TikTokApiService
{
    public Task UnfollowAsync(string userId, TikTokCookie cookie, CancellationToken ct = default) =>
        throw new NotSupportedException(
            T("Unfollow qua API trực tiếp không dùng được — TikTok âm thầm bỏ qua request không có chữ ký hợp lệ (trả về thành công giả). Dùng trình duyệt thật thay thế.", "Unfollowing through the API directly does not work — TikTok silently ignores requests without a valid signature (and reports a fake success). Use the real browser instead."));
}
