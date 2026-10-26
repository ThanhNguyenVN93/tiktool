namespace TikTool.Models;

public enum DownloadStatus
{
    Waiting,
    Downloading,
    Done,
    Error,
}

public class DownloadItem
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Format { get; set; } = "mp4"; // mp4 | mp3 | mkv
    public string OutputDir { get; set; } = "";
    public DownloadStatus Status { get; set; } = DownloadStatus.Waiting;
    public double Progress { get; set; }
    public string FileSize { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string Quality { get; set; } = "";
    public string? ErrorMessage { get; set; }
}
