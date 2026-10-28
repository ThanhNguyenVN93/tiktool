using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TikTool.Services;

internal sealed record UpdateInfo(SemVer Version, string Tag, string PageUrl, string ZipUrl, string ZipName, long ZipSize, string ShaUrl, string Notes);

internal sealed record ApplyResult(bool Ok, int Replaced, string? Error);

/// <summary>Finds a newer release on GitHub, downloads it, checks its SHA256, unpacks it, and swaps the files of the running app. A running exe or dll
/// cannot be overwritten or deleted on Windows but it CAN be renamed, so each changed file is renamed to "name.old" and the new file is copied in
/// its place (everything is undone if one step fails); the new copy deletes the ".old" files when it starts. Nothing in %APPDATA% is touched, and
/// Resources\yt-dlp.exe / ffmpeg.exe are never replaced (the user or yt-dlp's own updater owns them).</summary>
internal static class AppUpdater
{
    private const string Repo = "ThanhNguyenVN93/tiktool";
    private const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    private static readonly string[] NeverReplace = { @"Resources\yt-dlp.exe", @"Resources\ffmpeg.exe", "install.bat", "portable.bat", "uninstall.bat", "portable.flag" };
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SocialToolkit-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // ------------------------------------------------------------------------------------------------ find

    /// <summary>The newest release that is newer than <paramref name="current"/> and has a Windows zip plus its checksum file, both hosted in this
    /// repository's releases. Drafts are ignored; a stable install is never moved to a pre-release.</summary>
    internal static UpdateInfo? PickUpdate(string releasesJson, SemVer current)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        UpdateInfo? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (Bool(release, "draft") || (Bool(release, "prerelease") && !current.IsPrerelease))
            {
                continue;
            }

            var tag = Str(release, "tag_name");
            if (!SemVer.TryParse(tag, out var version) || version <= current || (best is not null && version <= best.Version))
            {
                continue;
            }

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            string? zipUrl = null, zipName = null, shaUrl = null;
            long size = 0;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                var url = Str(asset, "browser_download_url");
                if (url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name, @"^SocialToolkit-.*-win-x64\.zip$"))
                {
                    zipUrl = url;
                    zipName = name;
                    size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                }
            }

            if (zipUrl is null || zipName is null)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var url = Str(asset, "browser_download_url");
                if (Str(asset, "name") == zipName + ".sha256" && url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    shaUrl = url;
                }
            }

            if (shaUrl is null)
            {
                continue; // no checksum, no update
            }

            best = new UpdateInfo(version, tag, Str(release, "html_url"), zipUrl, zipName, size, shaUrl, Str(release, "body"));
        }

        return best;
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=15", ct);
        return PickUpdate(json, AppVersion.Current);
    }

    // ------------------------------------------------------------------------------------------------ download and stage

    /// <summary>Downloads the zip, refuses it unless its SHA256 matches the checksum file, unpacks it into the staging folder and leaves a marker.</summary>
    public static async Task DownloadAndStageAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        var root = AppPaths.UpdateDir;
        var zipPath = Path.Combine(root, info.ZipName);
        var stagedDir = Path.Combine(root, "staged");

        var checksumText = await Http.GetStringAsync(info.ShaUrl, ct);
        var expected = Regex.Match(checksumText, @"\b[0-9a-fA-F]{64}\b").Value.ToLowerInvariant();
        if (expected.Length != 64)
        {
            throw new InvalidOperationException("The checksum file does not contain a SHA256.");
        }

        using (var response = await Http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? info.ZipSize;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(zipPath);
            var buffer = new byte[256 * 1024];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }
        }

        string actual;
        await using (var check = File.OpenRead(zipPath))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
        }

        if (actual != expected)
        {
            File.Delete(zipPath);
            throw new InvalidOperationException("The download does not match its SHA256, so it was discarded.");
        }

        if (Directory.Exists(stagedDir))
        {
            Directory.Delete(stagedDir, true);
        }

        ExtractZipSafely(zipPath, stagedDir);
        if (!File.Exists(Path.Combine(stagedDir, "TikTool.exe")))
        {
            throw new InvalidOperationException("The package does not contain TikTool.exe.");
        }

        File.WriteAllText(Path.Combine(root, "staged.json"), JsonSerializer.Serialize(new StagedMarker(info.Tag, info.PageUrl, actual)));
        File.Delete(zipPath);
    }

    private sealed record StagedMarker(string Tag, string PageUrl, string Sha256);

    /// <summary>The unpacked update waiting to be installed, when it is newer than the running version.</summary>
    public static (string Dir, SemVer Version, string Tag, string PageUrl)? FindStaged()
    {
        try
        {
            var root = AppPaths.UpdateDir;
            var markerPath = Path.Combine(root, "staged.json");
            var dir = Path.Combine(root, "staged");
            if (!File.Exists(markerPath) || !File.Exists(Path.Combine(dir, "TikTool.exe")))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<StagedMarker>(File.ReadAllText(markerPath));
            return marker is not null && SemVer.TryParse(marker.Tag, out var v) && v > AppVersion.Current ? (dir, v, marker.Tag, marker.PageUrl) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void PurgeStaging()
    {
        try
        {
            var root = AppPaths.UpdateDir;
            if (Directory.Exists(Path.Combine(root, "staged")))
            {
                Directory.Delete(Path.Combine(root, "staged"), true);
            }

            File.Delete(Path.Combine(root, "staged.json"));
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Unpacks a zip whose files sit under one top-level folder, without that folder, and refuses any entry that would land outside <paramref name="destination"/>.</summary>
    internal static void ExtractZipSafely(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var firstSegments = files.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
        var strip = firstSegments.Count == 1 && files.All(e => e.FullName.Replace('\\', '/').Contains('/'));

        foreach (var entry in files)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (strip)
            {
                relative = relative[(relative.IndexOf('/') + 1)..];
            }

            var full = Path.GetFullPath(Path.Combine(destination, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The package has an entry outside its folder: " + entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    // ------------------------------------------------------------------------------------------------ apply

    /// <summary>Swaps the files of the app in <paramref name="targetDir"/> for the ones in <paramref name="stagedDir"/>. All or nothing.</summary>
    internal static ApplyResult ApplyFiles(string stagedDir, string targetDir)
    {
        var done = new List<(string Target, string? Old)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(stagedDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(stagedDir, file);
                if (NeverReplace.Any(n => relative.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var target = Path.Combine(targetDir, relative);
                string? old = null;
                if (File.Exists(target))
                {
                    if (SameContent(file, target))
                    {
                        continue;
                    }

                    old = UniqueOldName(target);
                    File.Move(target, old);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    File.Copy(file, target, overwrite: false);
                }
                catch (Exception)
                {
                    if (old is not null)
                    {
                        File.Move(old, target);
                    }

                    throw;
                }

                done.Add((target, old));
            }

            return new ApplyResult(true, done.Count, null);
        }
        catch (Exception ex)
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.Delete(done[i].Target);
                    if (done[i].Old is not null)
                    {
                        File.Move(done[i].Old!, done[i].Target);
                    }
                }
                catch (Exception)
                {
                    // nothing more can be done for this one
                }
            }

            return new ApplyResult(false, 0, ex.Message);
        }
    }

    private static string UniqueOldName(string target)
    {
        var candidate = target + ".old";
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = target + ".old" + n;
        }

        return candidate;
    }

    private static bool SameContent(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length)
        {
            return false;
        }

        using var sa = File.OpenRead(a);
        using var sb = File.Open(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }

    /// <summary>Deletes the ".old" files an earlier update left behind (they are free once the old process is gone).</summary>
    public static void CleanupOldFiles(string targetDir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(targetDir, "*.old*", SearchOption.AllDirectories).Where(f => Regex.IsMatch(f, @"\.old\d*$")))
            {
                try { File.Delete(file); } catch (Exception) { /* still in use: next start */ }
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Starts the (now updated) app; it waits for this process to end before it takes the single-instance lock.</summary>
    public static void StartNewCopy(string targetDir, int waitForPid)
    {
        var exe = Path.Combine(targetDir, Path.GetFileName(Environment.ProcessPath) ?? "TikTool.exe");
        Process.Start(new ProcessStartInfo { FileName = exe, Arguments = $"--wait-pid {waitForPid}", WorkingDirectory = targetDir, UseShellExecute = false });
    }

    public static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception)
        {
            // already gone
        }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}