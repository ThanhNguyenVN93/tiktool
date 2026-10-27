using System.Drawing;

namespace TikTool.Services;

/// <summary>Downloads and caches channel avatars on disk (one small file per username). TikTok's
/// avatar links are signed and expire, so they must be fetched soon after the list loads; once a
/// file is cached it is reused without needing the link again.</summary>
internal static class AvatarCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.tiktok.com/");
        return client;
    }

    private static string PathFor(string username)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(username.Select(c => invalid.Contains(c) ? '_' : c));
        return Path.Combine(AppPaths.AvatarDir, safe + ".img");
    }

    /// <summary>Path of a still-fresh cached avatar, or null.</summary>
    public static string? CachedPath(string username)
    {
        var path = PathFor(username);
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MaxAge ? path : null;
    }

    /// <summary>Downloads the avatar and returns its cached path, or null on any failure. The file must
    /// decode as an image (GDI+ cannot read WebP, so a .jpeg variant of the same link is tried first).</summary>
    public static async Task<string?> DownloadAsync(string username, string url, CancellationToken ct)
    {
        var candidates = url.Contains(".webp", StringComparison.OrdinalIgnoreCase)
            ? new[] { url.Replace(".webp", ".jpeg", StringComparison.OrdinalIgnoreCase), url }
            : new[] { url };

        foreach (var candidate in candidates)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(candidate, ct);
                using (var ms = new MemoryStream(bytes))
                using (Image.FromStream(ms))
                {
                    // decodes fine -> keep it
                }

                var path = PathFor(username);
                await File.WriteAllBytesAsync(path, bytes, ct);
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // try the next candidate
            }
        }

        return null;
    }
}