using TikTool.Services;

namespace TikTool.Tests;

public class AgeTextTests
{
    [Theory]
    [InlineData("13h ago", 0)]
    [InlineData("15 minutes ago", 0)]
    [InlineData("5d ago", 5)]
    [InlineData("2w ago", 14)]
    [InlineData("2 weeks ago", 14)]
    [InlineData("9mo ago", 270)]
    [InlineData("1 year ago", 365)]
    [InlineData("3y ago", 1095)]
    [InlineData("16y ago", 5840)]
    public void Reads_both_the_compact_and_the_long_forms_as_days(string text, int expectedDays)
    {
        var m = AgeText.Find(text);

        Assert.True(m.Success);
        Assert.Equal(expectedDays, AgeText.ApproxDays(int.Parse(m.Groups[1].Value), m.Groups[2].Value));
    }

    [Fact]
    public void Finds_the_first_age_inside_a_whole_page_text()
    {
        var page = "Latest Popular Oldest 0:22 My Second Dragon 21K 16y ago 0:27 My dragon! 19K 15y ago";

        Assert.Equal("16y ago", AgeText.Find(page).Value);
    }

    [Theory]
    [InlineData("Latest Popular Oldest")]
    [InlineData("Chicago")]
    [InlineData("")]
    public void Ignores_text_without_an_age(string text)
    {
        Assert.False(AgeText.Find(text).Success);
    }

    [Theory]
    [InlineData("3y ago", "khoảng 3 năm trước", "about 3 years ago")]
    [InlineData("1 year ago", "khoảng 1 năm trước", "about 1 year ago")]
    [InlineData("9mo ago", "khoảng 9 tháng trước", "about 9 months ago")]
    [InlineData("2w ago", "khoảng 2 tuần trước", "about 2 weeks ago")]
    [InlineData("5d ago", "5 ngày trước", "5 days ago")]
    [InlineData("13h ago", "13 giờ trước", "13 hours ago")]
    [InlineData("5m ago", "5 phút trước", "5 minutes ago")]
    public void Formats_a_sentence_in_each_language(string text, string vi, string en)
    {
        var (actualVi, actualEn) = AgeText.Format(text);

        Assert.Equal(vi, actualVi);
        Assert.Equal(en, actualEn);
    }

    [Fact]
    public void Leaves_unknown_text_unchanged()
    {
        Assert.Equal(("hôm qua", "hôm qua"), AgeText.Format("hôm qua"));
    }
}