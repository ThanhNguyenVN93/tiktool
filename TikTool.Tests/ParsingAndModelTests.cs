using System.Collections.Concurrent;
using System.Text.Json;
using TikTool.Models;
using TikTool.Services;

namespace TikTool.Tests;

public class YouTubeCsvTests
{
    [Fact]
    public void Writes_a_header_and_quotes_names_with_commas_and_quotes()
    {
        var channel = new YouTubeChannel
        {
            Name = "A \"B\", C",
            Url = "https://www.youtube.com/@a",
            LastUploadUnix = new DateTimeOffset(2023, 10, 28, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            LastUploadText = "3y ago",
            IsInactive = true,
            IsProtected = false,
            HasShorts = true,
        };

        var csv = YouTubeStore.BuildCsv(new[] { channel });

        Assert.StartsWith("Name,Url,LastUploadEstimated,LastUploadAge,Inactive,Protected,HasShorts", csv);
        Assert.Contains("\"A \"\"B\"\", C\"", csv);
        Assert.Contains("\"2023-10-28\"", csv);
        Assert.Contains(",yes,no,yes", csv);
    }

    [Fact]
    public void An_unread_channel_has_an_empty_date()
    {
        var csv = YouTubeStore.BuildCsv(new[] { new YouTubeChannel { Name = "x", Url = "u" } });

        Assert.Contains("\"x\",\"u\",\"\",\"\",no,no,no", csv);
    }
}

public class PostDatesTests
{
    private static JsonElement Array(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Takes_the_newest_post_even_when_a_pinned_old_one_is_listed_first()
    {
        // A channel's first posts are often PINNED, which are old on purpose: taking item 0 made live channels look dead.
        var items = Array("[{\"createTime\":1000},{\"createTime\":3000},{\"createTime\":2000}]");

        Assert.Equal(3000, PlaywrightService.NewestCreateTime(items));
    }

    [Fact]
    public void An_empty_list_has_no_date()
    {
        Assert.Null(PlaywrightService.NewestCreateTime(Array("[]")));
    }

    [Fact]
    public void Ignores_items_without_a_numeric_create_time()
    {
        var items = Array("[{\"id\":\"x\"},{\"createTime\":\"5\"},{\"createTime\":7}]");

        Assert.Equal(7, PlaywrightService.NewestCreateTime(items));
    }
}

public class TikTokApiParsingTests
{
    private static ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)> Parse(string json)
    {
        var result = new ConcurrentDictionary<string, (string UserId, int VideoCount, int Relation)>(StringComparer.OrdinalIgnoreCase);
        PlaywrightService.ExtractUserDataFromApiResponse(json, result);
        return result;
    }

    [Fact]
    public void Reads_user_id_video_count_and_relation_from_the_nested_list_shape()
    {
        var result = Parse("{\"userList\":[" +
            "{\"user\":{\"uniqueId\":\"abc\",\"id\":\"123\",\"relation\":1},\"stats\":{\"videoCount\":0}}," +
            "{\"user\":{\"uniqueId\":\"def\",\"id\":\"456\",\"relation\":2},\"stats\":{\"videoCount\":12}}],\"hasMore\":false}");

        Assert.Equal(("123", 0, 1), result["abc"]);
        Assert.Equal(("456", 12, 2), result["def"]);
    }

    [Fact]
    public void Reads_the_flat_user_shape()
    {
        var result = Parse("{\"data\":[{\"uniqueId\":\"zed\",\"id\":\"9\",\"videoCount\":3,\"relation\":0}]}");

        Assert.Equal(("9", 3, 0), result["zed"]);
    }

    [Fact]
    public void Unknown_video_count_and_relation_stay_minus_one()
    {
        var result = Parse("{\"user\":{\"uniqueId\":\"q\",\"id\":\"1\"}}");

        Assert.Equal(("1", -1, -1), result["q"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"unrelated\":true}")]
    public void Never_throws_on_a_response_it_does_not_understand(string body)
    {
        Assert.Empty(Parse(body));
    }
}

public class ModelTests
{
    [Fact]
    public void Days_since_the_last_upload_counts_whole_days()
    {
        var channel = new YouTubeChannel { LastUploadUnix = DateTimeOffset.UtcNow.AddDays(-10).AddHours(-1).ToUnixTimeSeconds() };

        Assert.Equal(10, channel.DaysSinceLastUpload);
    }

    [Fact]
    public void A_future_upload_time_never_gives_negative_days()
    {
        var channel = new YouTubeChannel { LastUploadUnix = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds() };

        Assert.Equal(0, channel.DaysSinceLastUpload);
    }

    [Fact]
    public void An_unread_channel_has_no_days()
    {
        Assert.Null(new YouTubeChannel().DaysSinceLastUpload);
    }

    [Fact]
    public void A_mutual_follow_is_a_friend_only_when_relation_is_two()
    {
        Assert.True(new FollowingAccount { Relation = 2 }.IsFriend);
        Assert.False(new FollowingAccount { Relation = 1 }.IsFriend);
        Assert.False(new FollowingAccount { Relation = -1 }.IsFriend);
    }
}