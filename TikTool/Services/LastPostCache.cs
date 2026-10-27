using System.Text.Json;

namespace TikTool.Services;

/// <summary>Remembers each account's last-post date between runs, so the slow, block-prone
/// profile visits only ever have to happen once per account. An entry is only trusted while the
/// account's video count still matches what was recorded (a new post changes the count) and it is
/// younger than <see cref="MaxAge"/>.</summary>
internal sealed class LastPostCache
{
    public sealed class Entry
    {
        public int VideoCount { get; set; }
        public long LastPostUnixSeconds { get; set; }
        public long CheckedAtUnixSeconds { get; set; }
    }

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);

    public LastPostCache()
    {
        try
        {
            if (File.Exists(AppPaths.LastPostCacheFile))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(AppPaths.LastPostCacheFile));
                if (loaded is not null)
                {
                    _map = new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // a corrupt cache just means re-checking; never block the app over it
        }
    }

    /// <summary>Returns the cached last-post time only if it is still trustworthy for an account
    /// that currently has <paramref name="currentVideoCount"/> videos.</summary>
    public long? TryGetFresh(string username, int currentVideoCount)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(username, out var e) || e.VideoCount != currentVideoCount)
            {
                return null;
            }

            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(e.CheckedAtUnixSeconds);
            return age <= MaxAge ? e.LastPostUnixSeconds : null;
        }
    }

    public void Set(string username, int videoCount, long lastPostUnixSeconds)
    {
        lock (_gate)
        {
            _map[username] = new Entry
            {
                VideoCount = videoCount,
                LastPostUnixSeconds = lastPostUnixSeconds,
                CheckedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
        }
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_map);
            }

            File.WriteAllText(AppPaths.LastPostCacheFile, json);
        }
        catch
        {
            // best effort
        }
    }
}
