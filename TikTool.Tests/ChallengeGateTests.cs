using TikTool.Services;

namespace TikTool.Tests;

public class ChallengeGateTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 40)]
    [InlineData(5, 70)]
    public void Reminders_start_close_together_and_thin_out(int n, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ChallengeGate.DefaultReminderAfter(n));
    }
}