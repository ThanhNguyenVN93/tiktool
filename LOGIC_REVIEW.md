# TikTool — Logic Review

> Rà soát toàn bộ codebase. Mỗi mục có file:dòng tham chiếu và mức độ.

---

## 🔴 Critical — có thể gây crash / leak / mất dữ liệu

### C1 · `PlaywrightService` không bao giờ được dispose
**File**: `Forms/MainForm.cs`

`PlaywrightService` implements `IAsyncDisposable` và có logic dọn dẹp Chrome process + temp profile. Nhưng `MainForm` không gọi `DisposeAsync()` khi đóng app. Hậu quả:
- Chrome process (`chrome.exe`) tiếp tục chạy nền sau khi TikTool đóng
- Thư mục `%TEMP%\tiktool_chrome_session_*` không bị xóa → accumulate disk space

**Fix**: Trong `MainForm.OnFormClosing`, gọi `playwrightService.DisposeAsync()` (hoặc make `MainForm` implement `IAsyncDisposable` → register `DisposeAsync` handler).

---

### C2 · `CancellationToken` không truyền vào `GetUserDetailAsync` khi check kênh chết
**File**: `Pages/UnfollowPage.cs:407`

```csharp
var detail = await _playwright.GetUserDetailAsync(account.Username);
// thiếu: , ct
```

Khi user bấm "Dừng" trong lúc đang check dead channels (vòng lặp N×profile), `ct` bị cancel nhưng `GetUserDetailAsync` vẫn tiếp tục navigate profile tiếp theo vì không nhận token. Vòng lặp ngoài không bị block vì `OperationCanceledException` chỉ throw ở `ct.ThrowIfCancellationRequested()` ở đầu lần lặp tiếp theo — tức là chạy xong 1 profile nữa mới dừng, không sao, nhưng nếu có 100 profile thì mỗi profile mất ~2s → dừng rất chậm.

**Fix**: `await _playwright.GetUserDetailAsync(account.Username, ct);`

---

### C3 · CTS không bị dispose sau khi download bị cancel
**File**: `Pages/DownloadPage.cs` — nhánh `catch (OperationCanceledException)`

```csharp
catch (OperationCanceledException) {
    if (Interlocked.Decrement(ref _activeWorkers) == 0)
        _log.Log("...", LogLevel.Warn);
    return;  // _cts KHÔNG được dispose ở đây
}
```

Nhánh normal exit thì có `_cts?.Dispose(); _cts = null;`. Nhánh cancel thì không. Nếu user cancel download, `_cts` còn giữ bộ nhớ và `IsCancellationRequested = true`. Lần click Add tiếp theo tạo CTS mới nhưng cái cũ không bao giờ bị dispose → resource leak mỗi lần cancel.

**Fix**: Thêm `else { _cts?.Dispose(); _cts = null; }` vào `if (Interlocked.Decrement(...) == 0)` trong `OperationCanceledException` handler.

---

### C4 · Login tương tác không set `ExpiryDate` → luôn hiện `? ngày`
**File**: `Services/PlaywrightService.cs:171-179`

`LoginInteractivelyAsync` tạo `TikTokCookie` từ `context.CookiesAsync()`. Playwright's `Cookie` object có field `Expires` (là `DateTimeOffset`). Code không đọc giá trị này:

```csharp
return new TikTokCookie {
    SessionId = session.Value,
    ...
    // ExpiryDate không set → DaysLeft luôn null → "? ngày"
};
```

**Fix**: Thêm `ExpiryDate = session.Expires == DateTimeOffset.MinValue ? null : session.Expires.LocalDateTime` vào object initializer.

---

## 🟡 High — lỗi logic, hành vi sai nhưng không crash

### H1 · Auto-update yt-dlp dùng nightly thay vì stable
**File**: `Services/UpdateService.cs:43`

```csharp
Arguments = "--update-to nightly",
```

`--update-to nightly` kéo build không ổn định. User không hay biết. Khi yt-dlp nightly có breaking change, download sẽ hỏng đột ngột.

**Fix**: Đổi thành `--update` (stable channel).

---

### H2 · `HistoryService.SaveAsync` lỗi bị nuốt hoàn toàn
**File**: `Services/HistoryService.cs:100-110`

`SaveAsync` không có try/catch. `AddAsync` gọi nó nhưng không bắt exception. Nếu disk đầy hoặc bị permission denied → stack unwind từ một fire-and-forget async call. Nhưng `AddAsync` được await trong `UnfollowPage` và `DownloadPage`, nên exception sẽ bubble lên và bị log là lỗi unfollow/download, không phải lỗi lưu history.

**Fix**: Thêm try/catch trong `SaveAsync` hoặc trong `AddAsync`, log warning riêng biệt.

---

### H3 · `SelectorsService._cached` không có lock — data race nhẹ
**File**: `Services/SelectorsService.cs:8`

`_cached` là static field. `Load()` được gọi từ UI thread (UnfollowPage, ClickUnfollowAsync), `Invalidate()` được gọi từ `Task.Run` trong background (`UpdateService`). Không có `lock` hay `volatile`. Worst case: UI thread đọc stale cache một lần rồi tự update lần tiếp. Không crash, nhưng là data race kỹ thuật.

**Fix**: Dùng `volatile` cho `_cached` hoặc bọc read/write trong `lock(_sync)`.

---

### H4 · `GetUserDetailAsync` — race timing với `WaitForResponseAsync`
**File**: `Services/PlaywrightService.cs:634`

`WaitForResponseAsync` timeout 8000ms bắt đầu chạy TRƯỚC `GotoAsync`. Nếu `GotoAsync` bản thân mất > 4s (mạng chậm, TikTok load chậm), cửa sổ intercept còn < 4s để chờ `api/post/item_list/` response. Nếu timeout → catch exception → `lastPostUnixSeconds = null`. Hệ quả: kênh có video nhưng `DaysSinceLastPost = null` → không bị đánh dấu dead theo tiêu chí ngày, chỉ theo tiêu chí 0 video. Degraded gracefully nhưng **mất dữ liệu inactivity**.

**Fix**: Tăng timeout lên `15_000` hoặc bắt đầu `WaitForResponseAsync` AFTER `GotoAsync` complete (cần restructure — khó hơn vì intercept phải đăng ký trước khi navigate).

Cách thực tế: tăng timeout 8000 → 15000 ms.

---

### H5 · `CookieVerifyResult.DaysLeft` là dead code sau fix
**File**: `Services/CookieService.cs:422-430`

`CookieVerifyResult.Ok(username, cookie.DaysLeft)` vẫn truyền `DaysLeft`, nhưng code CookiePage không còn đọc `result.DaysLeft` nữa (assignment đã bị remove). `CookieVerifyResult.DaysLeft` và param `daysLeft` trong `Ok()` không có consumer. Không gây lỗi nhưng misleading.

**Fix**: Remove `DaysLeft` khỏi `CookieVerifyResult`, đơn giản hóa `Ok(string username)`.

---

## 🟠 Medium — edge case, hành vi không mong đợi

### M1 · `HistoryService.Load()` blocking synchronous trong constructor
**File**: `Services/HistoryService.cs:17-18`

```csharp
public HistoryService() { Load(); }  // Load() → File.ReadAllText (sync)
```

`HistoryService` được tạo trong `MainForm` constructor (UI thread). `history.json` có thể lên đến 500 entries với JSON text. Trên máy chậm hoặc khi file được scan bởi antivirus, này block UI khởi động.

**Fix**: Làm `HistoryService` constructor async hoặc dùng `Lazy<Task>` để load lần đầu khi `Items` được access.

---

### M2 · CSV export không escape newline trong title
**File**: `Services/HistoryService.cs:74-77`

`CsvEscape` chỉ quote khi có `,` hoặc `"`. Nếu title video có `\n` (TikTok cho phép newline trong caption), CSV sẽ bị broken row.

**Fix**: Thêm điều kiện `|| value.Contains('\n')` hoặc `value.Contains('\r')` vào `CsvEscape`.

---

### M3 · `HistoryPage.UpdateStats()` luôn tính tổng all-time, bỏ qua filter đang active
**File**: `Pages/HistoryPage.cs:286-294`

Filter chip "Hôm nay" / "Tuần này" chỉ filter list bên dưới. 3 stat card (Video đã tải, Đã unfollow, Tổng dung lượng) luôn dùng `_historyService.Items` (all time). Nhìn vào tab History với filter "Hôm nay", stat vẫn hiện 1000 video.

**Fix**: Thay `_historyService.Items` trong `UpdateStats()` bằng `FilterItems(_historyService.Items)`, hoặc document rõ là "lifetime totals".

---

### M4 · Selectors không dùng trong `SelectorsConfig`
**File**: `Models/SelectorsConfig.cs:38-45`

`FollowingUsername`, `FollowingNickname`, `LastPostDate` được define trong model và JSON nhưng không có code nào đọc chúng. Playwright parse username từ href trực tiếp (dùng `FollowingItem` selector), không qua 2 field kia. `LastPostDate` được intercept qua API response, không phải DOM.

Không gây lỗi, nhưng misleading khi user sửa `selectors.json` — họ có thể sửa field không có tác dụng gì.

---

### M5 · `ReadMasterKey` không verify prefix "DPAPI"
**File**: `Services/CookieService.cs:231`

```csharp
var dpapiBlob = encryptedKey[5..];  // assumes prefix is exactly "DPAPI"
```

Nếu `encrypted_key` trong Local State không có prefix này (Chrome bị corrupt / non-standard build), `encryptedKey[5..]` sẽ slice sai data và `ProtectedData.Unprotect` sẽ throw. Exception được propagate lên `ReadFromChromiumBrowser` và rethrow, nên không crash silently — nhưng error message sẽ là DPAPI exception, không phải "invalid format".

**Fix nhỏ**: Assert `Encoding.ASCII.GetString(encryptedKey, 0, 5) == "DPAPI"` rồi throw message rõ hơn nếu không match.

---

## 🔵 Low — dead code, minor polish

### L1 · `AppSettings.Theme = "Dark"` không có tác dụng
**File**: `Models/AppSettings.cs:24`

Field `Theme` được lưu/đọc trong settings nhưng không có code nào check `settings.Theme` để switch theme. TikTool luôn dùng màu dark mode hardcoded trong `Forms/Theme.cs`.

---

### L2 · "API trước" mode combo vẫn hiện dù API đã retire
**File**: `Pages/UnfollowPage.cs:213`

`TikTokApiService.UnfollowAsync` always throws `NotSupportedException`. Mode "API trước, dự phòng trình duyệt" vẫn hiện trong combo và user có thể chọn, nhưng nó luôn fallback sang Playwright ngay lập tức (silent). Misleading.

**Fix nhỏ**: Ẩn option này hoặc đổi text thành "Tự động (qua trình duyệt)".

---

### L3 · `FollowingAccount.IsFriend` không tồn tại
**File**: `Models/FollowingAccount.cs`

`AppSettings.SkipFriends = true` nhưng model `FollowingAccount` không có field `IsFriend`. Setting này không có tác dụng gì. Đã ghi chú trong CLAUDE.md TODO #5.

---

## Tóm tắt ưu tiên fix

| # | Vấn đề | File | Fix effort |
|---|--------|------|------------|
| C1 | Chrome process leak khi đóng app | MainForm.cs | Nhỏ |
| C2 | Cancel không stop Playwright khi check kênh | UnfollowPage.cs:407 | 1 dòng |
| C3 | CTS leak sau cancel download | DownloadPage.cs | 2 dòng |
| C4 | Login flow không set ExpiryDate → `? ngày` | PlaywrightService.cs:171 | 1 dòng |
| H1 | yt-dlp update → nightly thay vì stable | UpdateService.cs:43 | 1 từ |
| H2 | History save error nuốt im | HistoryService.cs | Nhỏ |
| H4 | GetUserDetailAsync timeout quá ngắn | PlaywrightService.cs:636 | 1 số |
| M2 | CSV newline trong title | HistoryService.cs:74 | 1 dòng |
| M3 | Stats filter mismatch | HistoryPage.cs:286 | Nhỏ |
