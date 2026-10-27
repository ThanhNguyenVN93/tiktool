namespace TikTool.Models;

public enum YouTubeChannelStatus
{
    Pending,
    Unsubscribing,
    Done,
    Failed,
}

/// <summary>One channel from the signed-in user's YouTube subscriptions.</summary>
public sealed class YouTubeChannel
{
    public string Name { get; set; } = "";

    /// <summary>The channel link exactly as YouTube lists it (https://www.youtube.com/@handle or /channel/UC...).</summary>
    public string Url { get; set; } = "";

    public string AvatarUrl { get; set; } = "";
    public string? AvatarPath { get; set; }

    /// <summary>"@handle" when the link has one, otherwise the last part of the link.</summary>
    public string Handle { get; set; } = "";

    /// <summary>Subscriber count as YouTube writes it ("1.2M subscribers"), when the list showed one.</summary>
    public string Subscribers { get; set; } = "";

    /// <summary>Estimated time of the newest upload. YouTube only shows a coarse age ("3y ago"), so this is approximate.</summary>
    public long? LastUploadUnix { get; set; }

    public string LastUploadText { get; set; } = "";
    public bool AgeChecked { get; set; }
    public bool NoVideos { get; set; }

    public int? DaysSinceLastUpload => LastUploadUnix is long s
        ? (int)Math.Max(0, (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(s)).TotalDays)
        : null;

    public bool IsInactive { get; set; }
    public bool IsProtected { get; set; }
    public bool IsSelected { get; set; }
    public YouTubeChannelStatus Status { get; set; } = YouTubeChannelStatus.Pending;
    public string? ErrorMessage { get; set; }
}