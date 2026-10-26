using System.Text.Json.Serialization;

namespace TikTool.Models;

public class SelectorsConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("updated")]
    public string Updated { get; set; } = "";

    [JsonPropertyName("unfollow_btn")]
    public string UnfollowBtn { get; set; } = "[data-e2e='follow-button']";

    // The "Following" modal reachable from clicking the follow-count stat on the profile page is
    // confirmed (live, 2026-09-13) to stop loading past its first ~30 rows for large lists — even
    // for a human manually scrolling it in an ordinary, non-automated browser, so it's a real
    // TikTok bug, not a selector/automation problem, and not client-side-fixable. The "Following
    // accounts" panel reachable from tiktok.com/following's left sidebar ("View all") shows the
    // exact same data via a DIFFERENT UI surface and does NOT have that problem — confirmed live
    // by the account owner scrolling it all the way through. So LoadFollowingListAsync now goes
    // there instead. ProfileFollowingStat/ProfilePageUrl are kept only for reading the real total
    // follow count off the profile page (best-effort, for the "loaded fewer than total" warning).
    // Each row in the working panel is `<a data-e2e="following-user-button" href="/@username">`
    // wrapping both the nickname and username text — username comes from parsing the href (works
    // regardless of which nested data-e2e attributes are or aren't present), nickname from the
    // link's own leading text line. See ParseFollowingItemAsync.
    [JsonPropertyName("profile_page_url")]
    public string ProfilePageUrl { get; set; } = "https://www.tiktok.com/@{username}?lang=en";

    [JsonPropertyName("profile_following_stat")]
    public string ProfileFollowingStat { get; set; } = "[data-e2e='following']";

    [JsonPropertyName("following_item")]
    public string FollowingItem { get; set; } = "[data-e2e='following-user-button']";

    [JsonPropertyName("following_username")]
    public string FollowingUsername { get; set; } = "[data-e2e='following-user-desc']";

    [JsonPropertyName("following_nickname")]
    public string FollowingNickname { get; set; } = "[data-e2e='following-user-title']";

    [JsonPropertyName("last_post_date")]
    public string LastPostDate { get; set; } = "[data-e2e='user-post-item-desc']";

    [JsonPropertyName("api_unfollow_endpoint")]
    public string ApiUnfollowEndpoint { get; set; } = "https://www.tiktok.com/api/relation/user/unfollow/";

    [JsonPropertyName("api_verify_endpoint")]
    public string ApiVerifyEndpoint { get; set; } = "https://www.tiktok.com/passport/web/account/info/";
}
