using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>
/// Quản lý 3 thứ trong 1 file session_resume.dat:
/// 1. Resume phiên unfollow sau khi crash / đóng app
/// 2. Whitelist — kênh được bảo vệ, không bao giờ unfollow
/// 3. Daily quota — đếm số lần unfollow hôm nay
/// </summary>
public sealed class SessionStateService
{
    private SessionState _state = new();
    private HashSet<string> _whitelist = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SessionStateService()
    {
        LoadSession();
        LoadWhitelist();
    }

    // ─── Session resume ────────────────────────────────────────────────────

    public bool HasResumableSession()
    {
        lock (_lock) return _state.IsResumable;
    }

    /// <summary>Trả về snapshot nếu có thể resume, null nếu không.</summary>
    public SessionState? GetResumableSession()
    {
        lock (_lock) return _state.IsResumable ? _state : null;
    }

    /// <summary>Lưu danh sách accounts + totalFollowing sau CheckDead hoặc sau mỗi unfollow.</summary>
    public void SaveSession(List<FollowingAccount> accounts, int totalFollowing)
    {
        lock (_lock)
        {
            _state.Accounts = accounts.ToList();
            _state.TotalFollowing = totalFollowing;
            _state.SavedAt = DateTime.Now;
        }
        PersistSession();
    }

    /// <summary>Gọi khi phiên hoàn thành bình thường hoặc user chọn "Bắt đầu mới".</summary>
    public void ClearSession()
    {
        lock (_lock)
        {
            _state.Accounts.Clear();
            _state.TotalFollowing = 0;
            _state.SavedAt = DateTime.MinValue;
        }
        PersistSession();
    }

    // ─── Daily quota ───────────────────────────────────────────────────────

    public int GetUnfollowedToday()
    {
        lock (_lock)
        {
            EnsureQuotaDateReset();
            return _state.UnfollowedToday;
        }
    }

    public void IncrementUnfollowedToday()
    {
        lock (_lock)
        {
            EnsureQuotaDateReset();
            _state.UnfollowedToday++;
        }
        PersistSession();
    }

    private void EnsureQuotaDateReset()
    {
        // Gọi trong lock
        if (_state.QuotaDate.Date < DateTime.Today)
        {
            _state.UnfollowedToday = 0;
            _state.QuotaDate = DateTime.Today;
        }
    }

    // ─── Whitelist ──────────────────────────────────────────────────────────

    public bool IsWhitelisted(string username) =>
        _whitelist.Contains(username);

    public IReadOnlyCollection<string> GetWhitelist() => _whitelist;

    public void AddToWhitelist(string username)
    {
        lock (_lock) _whitelist.Add(username);
        PersistWhitelist();
    }

    public void RemoveFromWhitelist(string username)
    {
        lock (_lock) _whitelist.Remove(username);
        PersistWhitelist();
    }

    // ─── Persistence ───────────────────────────────────────────────────────

    private void LoadSession()
    {
        try
        {
            if (File.Exists(AppPaths.SessionFile))
            {
                var json = File.ReadAllText(AppPaths.SessionFile);
                var state = JsonSerializer.Deserialize<SessionState>(json);
                if (state is not null) _state = state;
            }
        }
        catch { _state = new SessionState(); }
    }

    private void LoadWhitelist()
    {
        try
        {
            if (File.Exists(AppPaths.WhitelistFile))
            {
                var lines = File.ReadAllLines(AppPaths.WhitelistFile);
                _whitelist = new HashSet<string>(
                    lines.Select(l => l.Trim()).Where(l => l.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private void PersistSession()
    {
        try
        {
            SessionState snapshot;
            lock (_lock) snapshot = _state;

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            WriteFileResilient(AppPaths.SessionFile, json);
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", $"Could not save session: {ex.Message}");
        }
    }

    private void PersistWhitelist()
    {
        try
        {
            string[] lines;
            lock (_lock) lines = _whitelist.ToArray();
            WriteFileResilient(AppPaths.WhitelistFile, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            AppLog.Write("ERROR", $"Could not save whitelist: {ex.Message}");
        }
    }

    /// <summary>Writes via a temp file + replace (so a crash cannot leave a half-written file); if the replace is blocked
    /// (security software has been seen silently denying overwrite-by-rename) falls back to writing the file directly,
    /// and logs the problem instead of losing the data without a trace.</summary>
    private static void WriteFileResilient(string path, string content)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write("WARN", $"Replace-write of {Path.GetFileName(path)} failed ({ex.GetType().Name}: {ex.Message}) - writing directly.");
            File.WriteAllText(path, content);
            try { File.Delete(tmp); } catch { }
        }
    }
}
