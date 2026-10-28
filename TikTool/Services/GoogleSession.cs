using Microsoft.Data.Sqlite;

namespace TikTool.Services;

/// <summary>Is the built-in browser signed in to Google/YouTube? The answer the Accounts tab learned this run wins; before that the profile's cookie
/// database is read (Chromium flushes new cookies to disk with a delay, hence the in-memory answer). Nothing here touches the user's own browsers.</summary>
internal static class GoogleSession
{
    private static bool? _known;

    public static void MarkSignedIn() => _known = true;

    public static void MarkSignedOut() => _known = false;

    public static bool IsSignedIn() => _known ?? CookieDatabaseHasSession();

    private static bool CookieDatabaseHasSession()
    {
        var cookieFile = Path.Combine(AppPaths.YouTubeWebViewDir, "EBWebView", "Default", "Network", "Cookies");
        if (!File.Exists(cookieFile))
        {
            return false;
        }

        var copy = Path.Combine(Path.GetTempPath(), $"tiktool_yt_cookies_{Guid.NewGuid():N}.db");
        try
        {
            using (var src = new FileStream(cookieFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(copy, FileMode.Create, FileAccess.Write))
            {
                src.CopyTo(dst);
            }

            using var conn = new SqliteConnection($"Data Source={copy};Pooling=False;Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM cookies WHERE host_key LIKE '%google.com' AND name IN ('SAPISID', '__Secure-1PSID', 'SID') LIMIT 1";
            return cmd.ExecuteScalar() != null;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try { File.Delete(copy); } catch (IOException) { }
        }
    }
}