using System.Diagnostics;
using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>Runs the two background auto-update checks on app start: yt-dlp's own
/// self-updater, and pulling a newer selectors.json from GitHub if the remote "version" field
/// is ahead of the local one. Both are best-effort — a failure here must never crash the app
/// or block the UI (see how MainForm calls this: fire-and-forget inside Task.Run).</summary>
public sealed class UpdateService
{
    private const string SelectorsUrl =
        "https://raw.githubusercontent.com/ThanhNguyenVN93/tiktool/main/config/selectors.json";

    private readonly YtDlpService _ytDlp;
    private readonly HttpClient _http = new();

    public UpdateService(YtDlpService ytDlp)
    {
        _ytDlp = ytDlp;
    }

    public async Task CheckAndUpdateAllAsync(IProgress<string>? log = null)
    {
        await UpdateYtDlpAsync(log);
        await UpdateSelectorsAsync(log);
    }

    private async Task UpdateYtDlpAsync(IProgress<string>? log)
    {
        try
        {
            if (!File.Exists(_ytDlp.ExecutablePath))
            {
                return;
            }

            log?.Report("[Update] Đang kiểm tra bản cập nhật yt-dlp...");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = _ytDlp.ExecutablePath,
                Arguments = "--update",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return;
            }

            await process.WaitForExitAsync();
            log?.Report(process.ExitCode == 0 ? "[Update] yt-dlp đã ở bản mới nhất." : "[Update] Kiểm tra yt-dlp thất bại (bỏ qua).");
        }
        catch (Exception ex)
        {
            log?.Report($"[Update] Lỗi khi cập nhật yt-dlp: {ex.Message}");
        }
    }

    private async Task UpdateSelectorsAsync(IProgress<string>? log)
    {
        try
        {
            var local = SelectorsService.Load();
            var remoteJson = await _http.GetStringAsync(SelectorsUrl);
            var remote = JsonSerializer.Deserialize<SelectorsConfig>(remoteJson);
            if (remote is null)
            {
                return;
            }

            if (IsNewer(remote.Version, local.Version))
            {
                await File.WriteAllTextAsync(AppPaths.SelectorsFile, remoteJson);
                SelectorsService.Invalidate();
                log?.Report($"[Update] Đã cập nhật selectors.json ({local.Version} → {remote.Version}).");
            }
        }
        catch (Exception ex)
        {
            // Không có mạng, repo chưa tồn tại, hoặc JSON hỏng — bỏ qua, dùng bản local.
            log?.Report($"[Update] Không thể kiểm tra selectors.json: {ex.Message}");
        }
    }

    private static bool IsNewer(string remoteVersion, string localVersion)
    {
        if (Version.TryParse(remoteVersion, out var remote) && Version.TryParse(localVersion, out var local))
        {
            return remote > local;
        }

        return string.CompareOrdinal(remoteVersion, localVersion) > 0;
    }
}
