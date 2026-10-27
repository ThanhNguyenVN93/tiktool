using System.Text;
using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>Small on-disk state for the YouTube feature: the cached "latest upload" per channel (so the slow page
/// reads happen once), the list of channels the user protected, and the CSV backup written before unsubscribing.</summary>
internal sealed class YouTubeStore
{
    public sealed class AgeEntry
    {
        public long? LastUploadUnix { get; set; }
        public string Text { get; set; } = "";
        public bool NoVideos { get; set; }
        public bool HasShorts { get; set; }
        public long CheckedAtUnix { get; set; }

        /// <summary>2 = read together with the Shorts-tab check; older readings are re-read.</summary>
        public int Version { get; set; }
    }

    // YouTube only gives a coarse age ("3y ago"), so a week-old reading is still good enough for an inactivity check.
    private static readonly TimeSpan AgeMaxAge = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private Dictionary<string, AgeEntry> _ages = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);

    public YouTubeStore()
    {
        try
        {
            if (File.Exists(AppPaths.YouTubeAgesFile))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, AgeEntry>>(File.ReadAllText(AppPaths.YouTubeAgesFile));
                if (loaded is not null)
                {
                    _ages = new Dictionary<string, AgeEntry>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // a damaged cache only means re-reading
        }

        try
        {
            if (File.Exists(AppPaths.YouTubeProtectedFile))
            {
                _protected = new HashSet<string>(
                    File.ReadAllLines(AppPaths.YouTubeProtectedFile).Select(l => l.Trim()).Where(l => l.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // start with nothing protected rather than failing the page
        }
    }

    public bool TryGetFreshAge(string url, out AgeEntry entry)
    {
        lock (_gate)
        {
            if (_ages.TryGetValue(url, out var e)
                && e.Version >= 2
                && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(e.CheckedAtUnix) <= AgeMaxAge)
            {
                entry = e;
                return true;
            }
        }

        entry = new AgeEntry();
        return false;
    }

    public void SetAge(string url, long? lastUploadUnix, string text, bool noVideos, bool hasShorts)
    {
        lock (_gate)
        {
            _ages[url] = new AgeEntry
            {
                LastUploadUnix = lastUploadUnix,
                Text = text,
                NoVideos = noVideos,
                HasShorts = hasShorts,
                CheckedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Version = 2,
            };
        }
    }

    public void SaveAges()
    {
        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_ages);
            }

            File.WriteAllText(AppPaths.YouTubeAgesFile, json);
        }
        catch
        {
            // best effort
        }
    }

    public bool IsProtected(string url)
    {
        lock (_gate)
        {
            return _protected.Contains(url);
        }
    }

    public void SetProtected(string url, bool value)
    {
        string[] lines;
        lock (_gate)
        {
            if (value)
            {
                _protected.Add(url);
            }
            else
            {
                _protected.Remove(url);
            }

            lines = _protected.ToArray();
        }

        try
        {
            File.WriteAllLines(AppPaths.YouTubeProtectedFile, lines);
        }
        catch
        {
            // best effort
        }
    }

    public static string BuildCsv(IEnumerable<YouTubeChannel> channels)
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        var sb = new StringBuilder();
        sb.AppendLine("Name,Url,LastUploadEstimated,LastUploadAge,Inactive,Protected,HasShorts");
        foreach (var c in channels)
        {
            var last = c.LastUploadUnix is long s ? DateTimeOffset.FromUnixTimeSeconds(s).ToString("yyyy-MM-dd") : "";
            sb.AppendLine(string.Join(",", Q(c.Name), Q(c.Url), Q(last), Q(c.LastUploadText), c.IsInactive ? "yes" : "no", c.IsProtected ? "yes" : "no", c.HasShorts ? "yes" : "no"));
        }

        return sb.ToString();
    }

    /// <summary>Writes the whole current list next to the app data so every unsubscribe run can be undone by hand
    /// (each row has the channel link).</summary>
    public string WriteBackup(IEnumerable<YouTubeChannel> channels)
    {
        var path = Path.Combine(AppPaths.YouTubeBackupDir, $"subscriptions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(path, BuildCsv(channels), new UTF8Encoding(true));
        return path;
    }
}