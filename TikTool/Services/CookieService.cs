using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TikTool.Models;

namespace TikTool.Services;

public enum CookieBrowser
{
    Chrome,
    Edge
}

/// <summary>
/// Reads TikTok cookies from Chromium browsers (Chrome/Edge), parses pasted cookie strings,
/// verifies a cookie against TikTok, and persists the active cookie encrypted with DPAPI.
/// </summary>
public sealed class CookieService
{
    private readonly HttpClient _http;

    public CookieService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
    }

    private static readonly Dictionary<CookieBrowser, string> UserDataDirs = new()
    {
        [CookieBrowser.Chrome] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"),
        [CookieBrowser.Edge] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data"),
    };

    private static readonly Dictionary<CookieBrowser, string> ProcessNames = new()
    {
        [CookieBrowser.Chrome] = "chrome",
        [CookieBrowser.Edge] = "msedge",
    };

    public static bool IsBrowserInstalled(CookieBrowser browser) => Directory.Exists(UserDataDirs[browser]);

    /// <summary>True if any window/background process of this browser is currently running —
    /// used to decide whether to offer "đóng {browser} rồi thử lại" before reading cookies.</summary>
    public static bool IsBrowserRunning(CookieBrowser browser) =>
        Process.GetProcessesByName(ProcessNames[browser]).Length > 0;

    /// <summary>Force-closes every running instance of the browser (all windows, including the
    /// background/tray one Chrome/Edge keep alive after the last window closes) so its cookie
    /// database is no longer locked. This closes all open tabs without prompting to save
    /// anything — callers MUST get explicit user confirmation before calling this, never call
    /// it silently.</summary>
    public static void KillBrowserProcesses(CookieBrowser browser)
    {
        foreach (var process in Process.GetProcessesByName(ProcessNames[browser]))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // Best effort — a process that exits on its own between GetProcessesByName and
                // Kill (or one we don't have permission to touch) shouldn't abort the others.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public async Task<TikTokCookie?> ReadFromChrome() => await ReadFromChromiumBrowser(CookieBrowser.Chrome);

    public async Task<TikTokCookie?> ReadFromEdge() => await ReadFromChromiumBrowser(CookieBrowser.Edge);

    /// <summary>Scans every Chrome/Edge profile (Default, Profile 1, Profile 2, ...) under the
    /// browser's User Data directory and returns the TikTok session from the first profile that
    /// has one. A single-profile check (just "Default") was the original implementation, but
    /// that misses a very common real-world case: the user is logged into TikTok in a secondary
    /// profile, not Default — that manifested as "Không tìm thấy session" even with the right
    /// browser open and logged in.</summary>
    public async Task<TikTokCookie?> ReadFromChromiumBrowser(CookieBrowser browser)
    {
        var userDataDir = UserDataDirs[browser];
        var localStatePath = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localStatePath))
        {
            return null;
        }

        var aesKey = ReadMasterKey(localStatePath);
        var browserName = browser == CookieBrowser.Chrome ? "Chrome" : "Edge";
        var sourceKey = browser == CookieBrowser.Chrome ? "chrome" : "edge";

        var lockedAny = false;
        foreach (var profileDir in EnumerateProfileDirs(userDataDir))
        {
            var cookiesPath = Path.Combine(profileDir, "Network", "Cookies");
            if (!File.Exists(cookiesPath))
            {
                cookiesPath = Path.Combine(profileDir, "Cookies");
            }

            if (!File.Exists(cookiesPath))
            {
                continue;
            }

            var tempDb = Path.Combine(Path.GetTempPath(), $"tiktool_cookies_{Guid.NewGuid():N}.db");
            try
            {
                CopyLockedFile(cookiesPath, tempDb);
            }
            catch (IOException)
            {
                lockedAny = true;
                continue;
            }

            try
            {
                var cookie = await ReadTikTokCookieFromDbAsync(tempDb, aesKey, sourceKey);
                if (cookie is not null)
                {
                    return cookie;
                }
            }
            finally
            {
                try { File.Delete(tempDb); } catch { /* best effort cleanup */ }
            }
        }

        if (lockedAny)
        {
            throw new InvalidOperationException(
                $"{browserName} đang khóa file cookie ở (ít nhất) 1 profile. Hãy đóng hoàn toàn {browserName} (kể cả icon khay hệ thống) rồi thử lại.");
        }

        return null;
    }

    /// <summary>Lists profile folders under User Data that actually contain a Cookies DB
    /// (Default, "Profile 1", "Profile 2", ... — Chrome/Edge name them however the user named
    /// them when creating the profile, so this can't just guess "Profile N"). "Default" is
    /// checked first since it's overwhelmingly the common case.</summary>
    private static IEnumerable<string> EnumerateProfileDirs(string userDataDir)
    {
        var defaultDir = Path.Combine(userDataDir, "Default");
        if (Directory.Exists(defaultDir))
        {
            yield return defaultDir;
        }

        if (!Directory.Exists(userDataDir))
        {
            yield break;
        }

        foreach (var dir in Directory.GetDirectories(userDataDir))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) || !name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return dir;
        }
    }

    private static async Task<TikTokCookie?> ReadTikTokCookieFromDbAsync(string dbPath, byte[] aesKey, string source)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long? sessionExpiresUtc = null;

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, encrypted_value, expires_utc FROM cookies WHERE host_key LIKE '%tiktok.com%'";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var encrypted = (byte[])reader["encrypted_value"];
            var expiresUtc = reader.GetInt64(2);

            var plain = DecryptCookieValue(encrypted, aesKey);
            if (plain is null)
            {
                continue;
            }

            values[name] = plain;
            if (name.Equals("sessionid", StringComparison.OrdinalIgnoreCase))
            {
                sessionExpiresUtc = expiresUtc;
            }
        }

        return BuildCookie(values, sessionExpiresUtc, source);
    }

    /// <summary>Copies a file that the running browser may hold an exclusive-ish lock on
    /// (Chrome/Edge open their SQLite DB with FILE_SHARE_READ|WRITE, so this still works
    /// while the browser is open).</summary>
    private static void CopyLockedFile(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static byte[] ReadMasterKey(string localStatePath)
    {
        var json = ReadLockedTextFile(localStatePath);
        using var doc = JsonDocument.Parse(json);
        var encryptedKeyB64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()
            ?? throw new InvalidOperationException("Local State không chứa os_crypt.encrypted_key");

        var encryptedKey = Convert.FromBase64String(encryptedKeyB64);
        // Strip the leading "DPAPI" (5-byte ASCII) prefix Chrome adds before the DPAPI blob.
        var dpapiBlob = encryptedKey[5..];
        return ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);
    }

    private static string ReadLockedTextFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? DecryptCookieValue(byte[] encrypted, byte[] aesKey)
    {
        if (encrypted.Length == 0)
        {
            return null;
        }

        try
        {
            var prefix = encrypted.Length >= 3 ? Encoding.ASCII.GetString(encrypted, 0, 3) : "";
            if (prefix is "v10" or "v11")
            {
                const int nonceLength = 12;
                const int tagLength = 16;
                var nonce = encrypted.AsSpan(3, nonceLength);
                var cipherAndTag = encrypted.AsSpan(3 + nonceLength);
                var ciphertext = cipherAndTag[..^tagLength];
                var tag = cipherAndTag[^tagLength..];

                var plainBytes = new byte[ciphertext.Length];
                using var aesGcm = new AesGcm(aesKey, tagLength);
                aesGcm.Decrypt(nonce, ciphertext, tag, plainBytes);
                return Encoding.UTF8.GetString(plainBytes);
            }

            // Older Chrome versions DPAPI-encrypt the value directly (no AES-GCM envelope).
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }

    private static TikTokCookie? BuildCookie(Dictionary<string, string> values, long? sessionExpiresUtc, string source)
    {
        if (!values.TryGetValue("sessionid", out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var raw = string.Join("; ", values.Select(kv => $"{kv.Key}={kv.Value}"));

        var cookie = new TikTokCookie
        {
            SessionId = sessionId,
            MsToken = values.GetValueOrDefault("msToken", ""),
            TtWebId = values.GetValueOrDefault("tt_webid_v2", values.GetValueOrDefault("tt_webid", "")),
            RawCookieString = raw,
            CapturedAt = DateTime.Now,
            Source = source,
        };

        if (sessionExpiresUtc is > 0)
        {
            var chromeEpoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var expiry = chromeEpoch.AddTicks(sessionExpiresUtc.Value * 10);
            cookie.ExpiryDate = expiry.ToLocalTime();
        }

        return cookie;
    }

    public static TikTokCookie ParseCookieString(string raw)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var name = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            values[name] = value;
        }

        if (!values.TryGetValue("sessionid", out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Chuỗi cookie không chứa 'sessionid'. Kiểm tra lại nội dung đã copy.");
        }

        return new TikTokCookie
        {
            SessionId = sessionId,
            MsToken = values.GetValueOrDefault("msToken", ""),
            TtWebId = values.GetValueOrDefault("tt_webid_v2", values.GetValueOrDefault("tt_webid", "")),
            RawCookieString = raw.Trim(),
            CapturedAt = DateTime.Now,
            Source = "manual",
        };
    }

    /// <summary>
    /// Calls TikTok to confirm the cookie is still authenticated and fetch the username.
    /// Never throws — returns IsValid = false with a reason on any failure.
    /// </summary>
    public async Task<CookieVerifyResult> VerifyCookieAsync(TikTokCookie cookie, CancellationToken ct = default)
    {
        try
        {
            var endpoint = SelectorsService.Load().ApiVerifyEndpoint;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?aid=1459&app_language=en&app_name=tiktok_web");
            request.Headers.Add("Cookie", cookie.RawCookieString);

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return CookieVerifyResult.Fail($"HTTP {(int)response.StatusCode} từ TikTok");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var username = data.TryGetProperty("username", out var u) ? u.GetString()
                    : data.TryGetProperty("user_name", out var u2) ? u2.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(username))
                {
                    return CookieVerifyResult.Ok(username!);
                }
            }

            return CookieVerifyResult.Fail("Cookie không hợp lệ hoặc đã hết hạn đăng nhập");
        }
        catch (Exception ex)
        {
            return CookieVerifyResult.Fail($"Không thể kết nối TikTok: {ex.Message}");
        }
    }

    public async Task SaveCookieAsync(TikTokCookie cookie)
    {
        var json = JsonSerializer.Serialize(cookie);
        var bytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(AppPaths.CookieFile, encrypted);
    }

    public async Task<TikTokCookie?> LoadCookieAsync()
    {
        var path = AppPaths.CookieFile;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<TikTokCookie>(json);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteCookie()
    {
        var path = AppPaths.CookieFile;
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class CookieVerifyResult
{
    public bool IsValid { get; private init; }
    public string? Username { get; private init; }
    public string? Error { get; private init; }

    public static CookieVerifyResult Ok(string username) => new()
    {
        IsValid = true,
        Username = username,
    };

    public static CookieVerifyResult Fail(string error) => new()
    {
        IsValid = false,
        Error = error,
    };
}
