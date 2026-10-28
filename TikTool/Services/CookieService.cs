using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>
/// Verifies a TikTok cookie, parses a pasted cookie string, and persists the active cookie encrypted with DPAPI.
/// The cookie itself comes from the sign-in on the Accounts tab; nothing is read from the user's own browsers.
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
            throw new InvalidOperationException(T("Chuỗi cookie không chứa 'sessionid'. Kiểm tra lại nội dung đã copy.", "The cookie string does not contain 'sessionid'. Check what you copied."));
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
                return CookieVerifyResult.Fail(T($"HTTP {(int)response.StatusCode} từ TikTok", $"HTTP {(int)response.StatusCode} from TikTok"));
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

            return CookieVerifyResult.Fail(T("Cookie không hợp lệ hoặc đã hết hạn đăng nhập", "The cookie is invalid or the login has expired"));
        }
        catch (Exception ex)
        {
            return CookieVerifyResult.Fail(T($"Không thể kết nối TikTok: {ex.Message}", $"Could not connect to TikTok: {ex.Message}"));
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
