# TikTool — CLAUDE.md

> **Dành cho Claude Code**: Đây là handoff đầy đủ cho project TikTool. Đọc hết file này trước khi làm bất cứ thứ gì.

## Tổng quan project

**TikTool** là Windows desktop app (.NET 8 WinForms + Guna.UI2) gồm 2 chức năng chính:
1. **Download video** TikTok/Instagram Reels không watermark (dùng yt-dlp.exe bundled)
2. **Unfollow kênh chết** TikTok — load danh sách following qua Playwright (Chromium headless), phát hiện kênh 0 video, unfollow qua API hoặc click trình duyệt

**Tác giả**: ThanhCode93 (GitHub: ThanhNguyenVN93)  
**Project folder**: `D:\code\frm_tiktool\`  
**Solution**: `TikTool.sln` → project `TikTool/TikTool.csproj` (.NET 8, WinForms, target `net8.0-windows`)

---

## Trạng thái hiện tại (quan trọng — đọc kỹ trước khi sửa gì)

**Tất cả 6 bước đã có code, build `dotnet build TikTool.sln` = 0 lỗi 0 warning.**

Nhưng nhiều phần **chưa test thực tế** với tài khoản TikTok thật. Chi tiết từng phần xem `PROGRESS.md` trong gốc project.

### Những gì đã chắc chắn hoạt động (đã test thật)
- Đọc/decrypt cookie Chrome/Edge (SQLite + AES-256-GCM + DPAPI), xử lý nhiều profile
- Dán cookie thủ công + verify qua mạng
- Lưu/nạp/xoá cookie mã hoá DPAPI tại `%APPDATA%\TikTool\cookie.json`
- Điều hướng 5 tab sidebar ổn định
- Tự động đóng Chrome/Edge khi file cookie bị khoá (hỏi confirm trước)
- `YtDlpService` + `SettingsService` + `HistoryService` + `UpdateService` logic đúng
- DOM/API TikTok xác nhận: `__UNIVERSAL_DATA_FOR_REHYDRATION__` script tồn tại, field `userInfo.user.id` / `userInfo.stats.videoCount` đúng

### Chưa test được (cần tài khoản TikTok thật + môi trường đúng)
- **Playwright/Chromium headless**: `InstallBrowserIfNeeded()` tải Chromium ~300MB, `LoadFollowingListAsync()` scroll + parse, `ClickUnfollowAsync()` click nút
- **Selectors TikTok** trong `Config/selectors.json`: `following_item`, `following_username`, `unfollow_btn`, `confirm_unfollow_btn` — đây là suy đoán theo brief, **phải dò lại bằng DevTools trên tài khoản thật**
- **yt-dlp download thật**: tải video thật, parse progress regex, ffmpeg merge
- **Rate limit flow**: HTTP 429 hoặc `status_code:2061` từ API unfollow
- **`SelectorsUrl`** trong `UpdateService.cs` trỏ repo placeholder `ThanhNguyenVN93/tiktool` — repo này chưa tồn tại, sẽ log lỗi 404 mỗi lần mở app (vô hại, đã try/catch)

---

## Cấu trúc file

```
D:\code\frm_tiktool\
├── TikTool.sln
├── PROGRESS.md                  ← lịch sử làm, đọc trước khi sửa gì
├── TEST_CHECKLIST.md            ← checklist test thực tế
├── FIX_COOKIEPAGE.md            ← ghi chú bug UI đã fix
├── files/
│   ├── TIKTOOL_BRIEF.md         ← spec gốc
│   ├── tiktok-tool-ui.html      ← demo UI tham khảo
│   └── tiktok-tool-flowchart.mermaid
└── TikTool/
    ├── TikTool.csproj           ← .NET 8, Guna.UI2 + Playwright + Sqlite + DPAPI
    ├── Program.cs
    ├── Config/
    │   └── selectors.json       ← CSS selectors TikTok (cần dò lại với DOM thật)
    ├── Forms/
    │   ├── MainForm.cs          ← sidebar + content area, inject services vào pages
    │   ├── NavItem.cs           ← Guna2Button custom, active state #ff2d55
    │   ├── Theme.cs             ← color tokens
    │   ├── LogPanel.cs          ← log panel tái dùng (info/ok/warn/error)
    │   ├── CookieMethodCard.cs  ← accordion card cho CookiePage
    │   ├── DownloadQueueItemRow.cs ← row queue download
    │   └── FollowingItemControl.cs ← row kênh following
    ├── Models/
    │   ├── AppSettings.cs
    │   ├── DownloadItem.cs
    │   ├── DownloadProgress.cs
    │   ├── FollowingAccount.cs
    │   ├── HistoryItem.cs
    │   ├── SelectorsConfig.cs
    │   └── TikTokCookie.cs
    ├── Pages/
    │   ├── DownloadPage.cs      ← Tab Download (564 dòng)
    │   ├── UnfollowPage.cs      ← Tab Unfollow kênh chết
    │   ├── CookiePage.cs        ← Tab Cookie (3 phương thức)
    │   ├── SettingsPage.cs      ← Tab Cài đặt
    │   ├── HistoryPage.cs       ← Tab Lịch sử
    │   └── PlaceholderPage.cs
    ├── Resources/
    │   ├── yt-dlp.exe           ← KHÔNG commit vào git, copy thủ công
    │   └── ffmpeg.exe           ← KHÔNG commit vào git, copy thủ công
    └── Services/
        ├── AppPaths.cs          ← %APPDATA%\TikTool\*
        ├── CookieService.cs     ← đọc Chrome/Edge cookie, DPAPI encrypt
        ├── HistoryService.cs    ← ghi lịch sử download/unfollow
        ├── PlaywrightService.cs ← Chromium headless, load following list
        ├── SelectorsService.cs  ← đọc + cache selectors.json
        ├── SettingsService.cs   ← đọc/ghi settings.json
        ├── TikTokApiService.cs  ← API unfollow + GetUserDetail (dùng UNIVERSAL_DATA)
        ├── UpdateService.cs     ← tự động update yt-dlp + selectors.json từ GitHub
        └── YtDlpService.cs      ← wrapper yt-dlp.exe
```

---

## Tech stack

| Package | Version | Dùng cho |
|---------|---------|----------|
| `Guna.UI2.WinForms` | 2.0.4.6 | UI controls (button, toggle, card) |
| `Microsoft.Playwright` | 1.47.0 | Chromium headless, load following list |
| `Microsoft.Data.Sqlite` | 8.0.10 | Đọc cookie database Chrome/Edge |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | DPAPI decrypt cookie key |

**Không dùng**: yt-dlp NuGet wrapper — gọi thẳng `yt-dlp.exe` qua `System.Diagnostics.Process` để có full control output parsing.

---

## Kiến trúc quan trọng

### 1. Dependency injection thủ công (MainForm constructor)
Thứ tự khởi tạo service trong `MainForm.cs` — **không được đảo thứ tự**:
```
SettingsService → CookieService → YtDlpService → PlaywrightService
→ TikTokApiService → HistoryService → UpdateService
```
Mỗi page nhận service qua constructor, không tự `new()`.

### 2. WinForms Dock layout — quy tắc bắt buộc
`Dock=Fill` phải được `Controls.Add()` **trước** mọi `Dock=Top/Bottom/Left/Right`. Làm ngược = Fill resolve sai → UI chồng lên nhau. Áp dụng nhất quán trong mọi Page.

### 3. yt-dlp.exe resolve logic (`YtDlpService.ResolveExecutable`)
```
1. Resources\yt-dlp.exe (cạnh exe build output)
2. PATH system
→ Không tìm thấy = throw, hiển thị friendly error
```
ffmpeg: không bắt buộc — thiếu chỉ warn, không crash (một số format cần ffmpeg mới merge được).

### 4. Cookie flow
```
Chrome/Edge SQLite → DPAPI decrypt AES key → AES-256-GCM decrypt cookie value
→ Lưu mã hoá DPAPI tại %APPDATA%\TikTool\cookie.json
→ Truyền raw cookie string vào Playwright AddCookiesAsync (domain .tiktok.com)
→ Truyền raw cookie string vào TikTokApiService header
```

### 5. GetUserDetail — dùng `__UNIVERSAL_DATA_FOR_REHYDRATION__`
**KHÔNG dùng API `user_detail_api` nữa** (TikTok trả 200 rỗng vì thiếu chữ ký chống-bot).  
Thay bằng `PlaywrightService.GetUserDetailAsync`: mở trang profile `https://www.tiktok.com/@{username}`, đọc script `__UNIVERSAL_DATA_FOR_REHYDRATION__` nhúng sẵn trong HTML, parse JSON.  
Field đúng đã xác nhận: `userInfo.user.id`, `userInfo.stats.videoCount`.

### 6. Unfollow strategy
```
Nếu chế độ = "API trước" VÀ có UserId:
  POST api_unfollow_endpoint với header Cookie
  → HTTP 429 hoặc body status_code:2061 = RateLimitException
  → Hỏi user (nếu NotifyOnRateLimit=true) → tiếp tục thì đợi 10s, fallback Playwright
  → Lỗi khác = fallback Playwright

Playwright fallback:
  Mở trang profile @username → click unfollow_btn → click confirm_unfollow_btn

Delay giữa các account: random(UnfollowDelayMinMs, UnfollowDelayMaxMs) từ Settings
Session limit: UnfollowSessionLimit (bỏ qua phần còn lại nếu vượt)
```

---

## selectors.json — cần dò lại với DOM thật

File `Config/selectors.json` hiện tại dùng selector suy đoán. **Trước khi test Playwright**, mở TikTok trên Chrome, đăng nhập, vào trang Following, dùng DevTools kiểm tra và cập nhật:

```json
{
  "Version": "1.0.0",
  "FollowingPageUrl": "https://www.tiktok.com/following",
  "FollowingItem": "[data-e2e='follow-item']",        ← CẦN DÒ LẠI
  "FollowingUsername": "[data-e2e='follow-item'] a",  ← CẦN DÒ LẠI
  "UnfollowBtn": "[data-e2e='follow-button']",        ← CẦN DÒ LẠI
  "ConfirmUnfollowBtn": "[data-e2e='unfollow-popup-unfollow-btn']", ← CẦN DÒ LẠI
  "ApiUnfollowEndpoint": "https://www.tiktok.com/api/relation/user/unfollow/",
  "CookieVerifyEndpoint": "https://www.tiktok.com/api/user/detail/?uniqueId=tiktok"
}
```

Sau khi dò đúng selector, tăng `Version` lên `"1.0.1"` rồi push lên GitHub (nếu đã tạo repo) để `UpdateService` phân phối selector mới tự động.

---

## Những việc cần làm (TODO list)

### 🔴 Quan trọng / blocking

1. **Test Playwright thật**
   - Cài Chromium: `dotnet run` lần đầu → `InstallBrowserIfNeeded()` tự chạy (~300MB download)
   - Đăng nhập TikTok trong cookie, bấm "Tải lại" trong tab Unfollow
   - Quan sát log để xem `LoadFollowingListAsync` có scroll và parse đúng không
   - Nếu không parse được = selector sai → dò lại bằng DevTools, sửa `selectors.json`

2. **Xác nhận selector unfollow**
   - Sau khi có danh sách kênh, chọn 1 kênh test, bấm Unfollow
   - Kiểm tra `unfollow_btn` và `confirm_unfollow_btn` selector có click đúng không
   - Quan sát có bị rate limit không với tài khoản test

3. **Test download video**
   - Dán URL TikTok → bấm Thêm → quan sát progress bar và log
   - Nếu lỗi: kiểm tra `Resources\yt-dlp.exe` có tồn tại không (`YtDlpService.ResolveExecutable`)
   - Test cả 3 format: MP4, MP3, MKV

4. **Tạo repo GitHub** `ThanhNguyenVN93/tiktool` (nếu muốn dùng auto-update selector)
   - Upload `Config/selectors.json` lên `main/config/selectors.json` trong repo đó
   - Hoặc sửa `SelectorsUrl` trong `UpdateService.cs` sang URL khác

### 🟡 Cải thiện / nice-to-have

5. **`SkipFriends` còn là stub**
   - `FollowingAccount.IsFriend` luôn = false vì chưa có selector cho badge "Follows you"
   - Thêm selector cho badge này trong `selectors.json`
   - Đọc badge trong `PlaywrightService.LoadFollowingListAsync`
   - Lọc trong `UnfollowPage.StartOrStopUnfollowAsync` nếu `SkipFriends=true`

6. **`FileSizeBytes` = 0 cho Download history**
   - `YtDlpService` trả `FileSize` dạng chuỗi (`"24.30MiB"`), chưa parse ra byte
   - Sửa để parse và điền `FileSizeBytes` vào `HistoryItem` → thống kê "Tổng GB" mới đúng

7. **`ConcurrentDownloads` chưa có tác dụng**
   - `DownloadPage.ProcessQueueAsync` vẫn tải tuần tự
   - Nếu muốn song song: đổi sang N worker `Task` chạy cùng lúc, đọc `SettingsService.Current.ConcurrentDownloads`

8. **Instagram Reels support**
   - yt-dlp hỗ trợ Instagram sẵn, chỉ cần pass URL vào — về lý thuyết đã hoạt động
   - Test với vài URL Instagram Reels để xác nhận

### 🟢 Nhỏ / polish

9. **App icon** — chưa có `.ico`, dùng icon mặc định
10. **Splash screen** — Playwright install lần đầu mất vài phút, nên có progress indicator riêng
11. **Tray icon** — minimize to tray khi đóng cửa sổ (setting tùy chọn)

---

## Conventions & style guide

### Tên
- Namespace: `TikTool`
- Form/Page class: `PascalCase` + suffix `Page` hoặc `Form`
- Service class: `PascalCase` + suffix `Service`
- Private field: `_camelCase`
- Event handler: `On` + tên control + tên event (`OnBtnAddClick`)

### Async
- Mọi I/O đều `async/await`; không `Task.Result` hoặc `.Wait()`
- Fire-and-forget trong event handler: bọc try/catch + log lỗi vào `LogPanel`
- `CancellationToken` truyền xuyên suốt chuỗi gọi

### UI thread
- `Control.Invoke()` hoặc `BeginInvoke()` khi update UI từ background thread
- Hoặc dùng `Progress<T>` với `IProgress<T>` như trong `YtDlpService.DownloadAsync`

### Log
- Dùng `LogPanel.AppendLog(message, LogLevel)` — không `Console.WriteLine`
- `LogLevel`: Info / Ok / Warn / Error

### Error handling
- Service layer: throw exception có message tiếng Việt thân thiện
- Page layer: bắt exception, log vào `LogPanel.Error()`, không popup MessageBox trừ confirm quan trọng
- `RateLimitException` trong `UnfollowPage`: hỏi MessageBox vì cần quyết định của user

---

## Build & run

```bash
# Build
cd D:\code\frm_tiktool
dotnet build TikTool.sln

# Run debug
dotnet run --project TikTool/TikTool.csproj

# Yêu cầu trước khi chạy
# 1. Copy yt-dlp.exe vào TikTool/Resources/yt-dlp.exe
# 2. Copy ffmpeg.exe vào TikTool/Resources/ffmpeg.exe (optional nhưng nên có)
# 3. Chromium sẽ tự download lần đầu khi dùng tab Unfollow
```

**Không commit `Resources/*.exe`** — file quá lớn, user tự download.  
yt-dlp: https://github.com/yt-dlp/yt-dlp/releases/latest  
ffmpeg: https://github.com/BtbN/FFmpeg-Builds/releases

---

## Luồng data chính

### Download flow
```
User paste URL
→ DownloadPage: tạo DownloadItem, thêm vào queue FlowLayoutPanel
→ ProcessQueueAsync (tuần tự): gọi YtDlpService.DownloadAsync
→ YtDlpService: Process yt-dlp.exe, IProgress<DownloadProgressInfo>
→ DownloadPage: cập nhật DownloadQueueItemRow (progress bar, badge)
→ Xong/lỗi: gọi HistoryService.AddAsync
```

### Unfollow flow
```
User bấm "Tải lại"
→ UnfollowPage: load cookie từ CookieService
→ PlaywrightService.LaunchAsync(cookie): mở Chromium, inject cookie .tiktok.com
→ PlaywrightService.LoadFollowingListAsync(): scroll trang Following, parse username
→ (tuần tự, 300ms delay): PlaywrightService.GetUserDetailAsync(username)
  → mở trang profile, đọc __UNIVERSAL_DATA_FOR_REHYDRATION__
  → parse videoCount → đánh dấu FollowingAccount.IsDead
→ User chọn kênh muốn unfollow, bấm "Bắt đầu Unfollow"
→ (tuần tự, random 2-5s delay): TikTokApiService.UnfollowAsync hoặc Playwright click
→ Mỗi lần: cập nhật badge FollowingItemControl, gọi HistoryService.AddAsync
```

---

## Các quyết định thiết kế không hiển nhiên

| Quyết định | Lý do |
|-----------|-------|
| Không có combo "số ngày không hoạt động" | TikTok không lộ ngày đăng cuối trên trang Following; fetch ngày cuối cho từng profile = N×M request, không thực tế |
| Tiêu chí "kênh chết" = videoCount==0 duy nhất | Thông tin duy nhất có thể lấy được mà không bị rate limit |
| Không dùng `user_detail_api` endpoint | TikTok trả 200 rỗng vì thiếu chữ ký chống-bot — dùng UNIVERSAL_DATA thay thế |
| Playwright thay vì HTTP thuần để load following | TikTok dùng JS lazy-load, không có endpoint public nào trả toàn bộ following list |
| Cookie đọc từ browser thay vì user login | Không cần implement OAuth/login flow phức tạp; user đã đăng nhập sẵn trên Chrome |
| yt-dlp.exe bundled thay vì NuGet wrapper | Full control output parsing, không phụ thuộc wrapper có thể lag update |

---

## Files đừng sửa lung tung

- **`PlaywrightService.cs`** — logic phức tạp, nhiều edge case; đọc kỹ comment trước khi sửa
- **`CookieService.cs`** — crypto DPAPI nhạy cảm; sửa sai = mất cookie đã lưu
- **`MainForm.cs` constructor** — thứ tự inject service phải giữ nguyên
- **`selectors.json`** — tăng `Version` khi sửa để `UpdateService` phân phối đúng

---

*File này được tạo tự động từ PROGRESS.md + codebase thực tế. Cập nhật PROGRESS.md sau mỗi phiên làm việc.*
