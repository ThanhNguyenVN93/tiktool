using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TikTool.Models;

namespace TikTool.Services;

public sealed class YtDlpMediaInfo
{
    public string Title { get; set; } = "";
    public string Quality { get; set; } = "";
    public string FileSize { get; set; } = "";
    public long FileSizeBytes { get; set; }
}

/// <summary>Wraps yt-dlp.exe via Process: fetches media info and downloads with progress
/// reporting. Never throws out of DownloadAsync without having retried first.</summary>
public sealed class YtDlpService
{
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(\d{1,3}(?:\.\d+)?)%\s+of\s+([\d.]+\w+)\s+at\s+([\d.]+\w+/s|Unknown speed)",
        RegexOptions.Compiled);

    public string ExecutablePath { get; }
    private readonly Lazy<bool> _ffmpegAvailable;

    public YtDlpService()
    {
        ExecutablePath = ResolveExecutable();
        _ffmpegAvailable = new Lazy<bool>(DetectFfmpeg);
    }

    /// <summary>True if ffmpeg is reachable (bundled next to the app or on PATH). Without it,
    /// yt-dlp cannot merge separately-downloaded video/audio tracks into one file, so downloads
    /// must fall back to an already-muxed progressive format instead.</summary>
    public bool FfmpegAvailable => _ffmpegAvailable.Value;

    private static string? BundledFfmpegPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool DetectFfmpeg()
    {
        if (BundledFfmpegPath() is not null)
        {
            return true;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(2000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "yt-dlp.exe"),
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "yt-dlp"; // fall back to PATH
    }

    public async Task<YtDlpMediaInfo?> GetInfoAsync(string url, string? rawCookieString = null, CancellationToken ct = default)
    {
        var args = new List<string> { "--dump-json", "--no-playlist", "--no-warnings" };
        if (!string.IsNullOrEmpty(rawCookieString))
            args.AddRange(new[] { "--add-header", $"Cookie:{rawCookieString}" });
        args.Add(url);
        var (exitCode, stdout, _) = await RunCaptureAsync(args, ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? url : url;

            var height = root.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;
            var quality = height > 0 ? $"{height}p" : "—";

            long fileSize = 0;
            if (root.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number)
            {
                fileSize = fs.GetInt64();
            }
            else if (root.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number)
            {
                fileSize = fsa.GetInt64();
            }

            return new YtDlpMediaInfo
            {
                Title = title,
                Quality = quality,
                FileSize = fileSize > 0 ? FormatBytes(fileSize) : "—",
                FileSizeBytes = fileSize,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task DownloadAsync(DownloadItem item, IProgress<DownloadProgressInfo> progress, string? rawCookieString = null, CancellationToken ct = default)
    {
        const int maxRetries = 2;
        Exception? lastError = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await RunDownloadAsync(item, progress, rawCookieString, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxRetries)
                {
                    await Task.Delay(1500, ct);
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Download thất bại không rõ lý do");
    }

    private async Task RunDownloadAsync(DownloadItem item, IProgress<DownloadProgressInfo> progress, string? rawCookieString, CancellationToken ct)
    {
        Directory.CreateDirectory(item.OutputDir);

        using var process = new Process
        {
            StartInfo = BuildStartInfo(BuildDownloadArgs(item, rawCookieString)),
        };

        var errorLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            var m = ProgressRegex.Match(e.Data);
            if (m.Success)
            {
                progress.Report(new DownloadProgressInfo
                {
                    Percent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Size = m.Groups[2].Value,
                    Speed = m.Groups[3].Value,
                });
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                errorLines.Add(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "Không tìm thấy yt-dlp.exe. Hãy đặt file yt-dlp.exe vào thư mục cài đặt Social Toolkit (Resources\\yt-dlp.exe) hoặc thêm vào PATH.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var reason = errorLines.Count > 0 ? errorLines[^1] : $"yt-dlp thoát với mã lỗi {process.ExitCode}";
            throw new InvalidOperationException(reason);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunCaptureAsync(IEnumerable<string> args, CancellationToken ct)
    {
        using var process = new Process { StartInfo = BuildStartInfo(args) };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "", "yt-dlp.exe not found");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private ProcessStartInfo BuildStartInfo(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    /// <summary>Without ffmpeg, yt-dlp cannot merge separately-downloaded video+audio tracks or
    /// transcode to mp3 — it just saves each requested stream as-is. Many modern videos have no
    /// single progressive (pre-muxed) format at all, so forcing "-f best" can fail outright with
    /// "Requested format is not available"; the video+audio selector chain below degrades more
    /// gracefully (falls back to separate .f###.* files, which still succeeds) than forcing a
    /// format that may not exist. mp3 has no such fallback — extraction genuinely requires
    /// ffmpeg — so it falls back to saving the best audio track in its native container instead.</summary>
    private List<string> BuildDownloadArgs(DownloadItem item, string? rawCookieString = null)
    {
        var args = new List<string> { item.Url, "-P", item.OutputDir, "--newline", "--no-warnings", "--no-playlist" };

        if (!string.IsNullOrEmpty(rawCookieString))
            args.AddRange(new[] { "--add-header", $"Cookie:{rawCookieString}" });

        var ffmpegPath = BundledFfmpegPath();
        if (ffmpegPath is not null)
            args.AddRange(new[] { "--ffmpeg-location", ffmpegPath });

        switch (item.Format)
        {
            case "mp3":
                if (FfmpegAvailable)
                {
                    args.AddRange(new[] { "-x", "--audio-format", "mp3" });
                }
                else
                {
                    args.AddRange(new[] { "-f", "bestaudio/best" });
                }
                break;

            case "mkv":
                args.AddRange(new[] { "-f", "bestvideo+bestaudio" });
                if (FfmpegAvailable)
                {
                    args.AddRange(new[] { "--merge-output-format", "mkv" });
                }
                break;

            default: // mp4
                args.AddRange(new[] { "-f", "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/best" });
                if (FfmpegAvailable)
                {
                    args.AddRange(new[] { "--merge-output-format", "mp4" });
                }
                break;
        }

        return args;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
