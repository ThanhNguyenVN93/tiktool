# Social Toolkit (thư mục code: TikTool) — CLAUDE.md

> Dành cho Claude Code. Đọc hết trước khi sửa. Viết lại 2026-10-27 sau một ngày chạy thật với tài khoản thật; mọi thứ ghi "đã kiểm chứng" nghĩa là đã thấy chạy đúng, không phải suy đoán.

## 1. Dự án là gì
Windows desktop app (.NET 8 WinForms + Guna.UI2), tên hiển thị **Social Toolkit** (tên nội bộ, namespace, exe, thư mục dữ liệu vẫn là `TikTool` — **không đổi**, đổi sẽ làm người dùng mất dữ liệu). Tác giả: ThanhCode93 (GitHub `ThanhNguyenVN93/tiktool`). Ba nhóm chức năng:

1. **Download** video TikTok/Instagram không watermark (gọi `yt-dlp.exe`, không dùng NuGet wrapper).
2. **TikTok Unfollow**: tải danh sách Following, tìm kênh "không hoạt động" (0 video, hoặc lâu không đăng), unfollow có xác nhận.
3. **YouTube Unsubscribe**: tải danh sách đăng ký, đọc ngày đăng video mới nhất, tìm kênh không hoạt động, hủy đăng ký có xác nhận + sao lưu CSV.

Thuật ngữ: dùng **"kênh không hoạt động / inactive"**, KHÔNG dùng "kênh chết / dead" ở bất cứ chữ nào người dùng thấy (0 video không có nghĩa là chết: có thể là người thật, kể cả bạn bè). Tên biến nội bộ (`IsDead`, `SelectAllDead`) giữ nguyên.

## 2. Build, test, chạy
```bash
dotnet build TikTool.sln
dotnet test TikTool.Tests/TikTool.Tests.csproj      # 36 test: đọc tuổi video, CSV, ngày mới nhất TikTok, parser API TikTok
dotnet run --project TikTool/TikTool.csproj
```
- Cần `TikTool/Resources/yt-dlp.exe` (và tùy chọn `ffmpeg.exe`), **không commit** `*.exe`.
- Mặc định tool dùng **trình duyệt tích hợp WebView2** (có sẵn trên Windows 11; máy cũ có thể cần WebView2 Runtime). Tắt công tắc trong Cài đặt thì mới cần **Chrome hoặc Edge đã cài** (tool điều khiển chính trình duyệt đó, không tải Chromium).
- Khi build kiểm tra từ công cụ khác, **dùng thư mục obj riêng** (`-p:OutDir=... -p:IntermediateOutputPath=...`) hoặc build bình thường; build ra OutDir riêng dùng chung `obj` từng làm thư mục `bin\Debug` thiếu `TikTool.deps.json` → lỗi "Could not load Microsoft.Playwright" lúc chạy. Nếu gặp: Rebuild Solution.
- Mọi chuỗi hiển thị phải đi qua `T(vi, en)` (`LanguageService`, đã `global using static`). **Không** tự định nghĩa hàm `T` giả chỉ trả tiếng Việt (từng làm dòng kênh và trang Lịch sử bỏ qua cài đặt ngôn ngữ).

## 3. Cấu trúc
```
TikTool.sln
TikTool/
  Program.cs                 splash, AppLog, xử lý lỗi chưa bắt
  Forms/   MainForm (thanh bên, nhóm "Unfollow" mở/đóng, DI thủ công), FollowingItemControl (dòng kênh TikTok),
           YouTubeChannelRow (dòng kênh YouTube), LogPanel, NavItem, SplashForm, DonationForm, Theme
  Pages/   DownloadPage, UnfollowPage (TikTok), YouTubePage, AccountsPage (đăng nhập TikTok + YouTube), SettingsPage, HistoryPage, GuidePage
  Browser/ IBrowserHost (ChromeBrowserHost | WebViewBrowserHost), WebViewSession(s), BrowserWindow, BrowserCdp, UiThread
  Services/ PlaywrightService (TikTok, CDP), YouTubeService + YouTubeStore + AgeText (YouTube),
           CookieService (DPAPI, đừng sửa bừa), SessionStateService (phiên + whitelist + quota), LastPostCache,
           AvatarCache, AppLog, AppPaths, SettingsService, HistoryService, UpdateService, YtDlpService, LanguageService
  Models/  FollowingAccount, YouTubeChannel, AppSettings, HistoryItem, SessionState, TikTokCookie ...
TikTool.Tests/               xUnit; TikTool.csproj có InternalsVisibleTo("TikTool.Tests")
```
Thứ tự khởi tạo service trong `MainForm` (không đảo): Settings → Cookie → YtDlp → Playwright → TikTokApi → History → SessionState → Update. `Dock=Fill` phải `Controls.Add` TRƯỚC các control Dock Top/Bottom.

## 4. Dữ liệu người dùng (`%APPDATA%\TikTool\`)
`cookie.dat` (cookie TikTok do chính tool lấy lúc đăng nhập, mã hóa DPAPI), `tiktok_webview\` và `youtube_webview\` (hồ sơ WebView2; Google nhớ phiên ở đây, TikTok thì KHÔNG), `settings.dat`, `history.dat`, `session_resume.dat`, `whitelist.dat`, `lastpost.dat` (ngày đăng TikTok, 30 ngày), `avatars\` (logo, 30 ngày), `youtube_profile\` (hồ sơ Chrome riêng, chỉ khi tắt trình duyệt tích hợp; đăng xuất ở tab Tài khoản xóa cả hai), `youtube_ages.dat` (7 ngày), `youtube_protected.dat`, `youtube_backups\*.csv`, `logs\session_*.log` (mỗi lần chạy một file mới, dòng thời gian đầy đủ) và các log THEO LOẠI ghi nối tiếp qua nhiều lần chạy (có ngày đầy đủ, xoay vòng ở 1 MB): `tiktok.log`, `youtube.log`, `challenges.log` (captcha), `update.log`, `download.log`, `errors.log` (mọi dòng ERROR/FATAL từ bất kỳ đâu). Ghi bằng `AppLog.Write(level, text, LogCategory.X)`; `LogPanel.Category` gắn loại cho từng trang, `debug\` (ảnh + HTML khi lỗi).

## 5. TikTok — những điều đã kiểm chứng (đừng làm ngược)
- **CDP attach Chrome/Edge thật** (`--remote-debugging-port`, profile tạm). TikTok chặn Chromium của Playwright.
- **Danh sách Following**: mở `tiktok.com/following?lang=en` → bấm "View all" → cuộn panel đó. Modal ở trang profile bị TikTok cắt ở ~60 hàng; widget sidebar chỉ có 5 hàng không cuộn được và cùng khớp selector hàng (đây là lý do từng kẹt ở 5). Chỉ đếm hàng đang **hiển thị**. Ép viewport 1440×900 (sidebar thu thành icon khi cửa sổ hẹp → mất nút "View all"). **Không ghé trang profile** để đọc tổng: tổng lấy từ trường `total` của `/api/user/list/`; trang profile là nơi TikTok chặn đầu tiên.
- Kết quả thật: 524/547 kênh; TikTok tự báo `hasMore=False, isTruncated=False` ở 524 (23 kênh không được trả, nhiều khả năng là follow cũ nhất, chưa rõ nguyên nhân).
- **Bộ chặn API** (`page.Response`): bắt `userId`, `videoCount`, `relation`, `avatarThumb` từ chính XHR của TikTok → không cần mở từng profile. Số video có sẵn nên "0 video" nhanh và an toàn.
- **Ngày đăng cuối = `createTime` MỚI NHẤT trong trang `item_list`, không phải phần tử đầu** (phần tử đầu thường là bài GHIM, cũ có chủ đích; từng khiến kênh còn sống bị báo 237/950 ngày).
- **Quét ngày là thao tác riêng (nút "Kiểm tra ngày")**, từng đợt nhỏ, 1 luồng, nghỉ 3–5 s, cache `lastpost.dat` (chỉ tin khi số video không đổi, tối đa 30 ngày). Quét tự động ~507 profile từng làm TikTok chặn phiên ("Something went wrong" / Access Denied). Sau ~36 kênh TikTok trả `item_list` **rỗng** (HTTP 200 không nội dung): tool dừng sau 4 lần rỗng liên tiếp.
- Đọc profile (`GetUserDetail*`): dữ liệu `__UNIVERSAL_DATA_FOR_REHYDRATION__` đôi khi chưa có lúc vừa tải → đợi vài giây, rồi dự phòng bằng phản hồi `api/user/detail` của chính trang.
- Unfollow: bấm trong trình duyệt (HTTP trực tiếp không dùng được: thiếu chữ ký). Kênh đã xóa (Access Denied) + có `userId` → `fetch()` ngay trong ngữ cảnh trang. Có hộp xác nhận (mặc định No), bỏ qua bạn bè (`SkipFriends`, `relation == 2`) và whitelist, giới hạn phiên/ngày, tự chờ rate limit.
- **Chưa kiểm chứng thật**: một lượt unfollow nhiều kênh qua hộp xác nhận mới; nút "Kiểm tra ngày" vượt 36 kênh với nhịp chậm.

## 5b. Trình duyệt tích hợp (WebView2) và tab Tài khoản — đã kiểm chứng
- **Vì sao**: không còn phải cài Chrome, không còn mở/kill Chrome; quan trọng nhất là **bỏ hẳn việc giải mã cookie của Chrome/Edge thật** (tín hiệu giống phần mềm trộm tài khoản, rủi ro antivirus lớn nhất). Chrome 136+ cũng không cho bật cổng debug trên hồ sơ mặc định. Đừng thêm lại việc đọc dữ liệu của trình duyệt người dùng.
- **Cách điều khiển giống hệt Chrome**: WebView2 bật `--remote-debugging-port`, Playwright `ConnectOverCDPAsync`. Mỗi hồ sơ có đúng **một `WebViewSession`** (môi trường + cổng, sống suốt đời app): khung đăng nhập trong tab Tài khoản và `BrowserWindow` làm việc đều lấy control từ đó (một thư mục dữ liệu chỉ mở được với một bộ tùy chọn).
- **Trang chính tìm bằng dấu hiệu URL** (`about:blank#tiktool-main-…`), KHÔNG dùng `Pages.First()`: context còn chứa trang của các khung khác (khung đăng nhập). Việc đọc nền (hồ sơ TikTok, ngày YouTube) chạy trong **một khung phụ ẩn** (`NewWorkerPageAsync`, dùng lại, chạy từng cái một: `WorkerRunsOneAtATime`). Không dùng `NewPageAsync` trên WebView2: nó mở cửa sổ bật lên.
- **Khung ẩn vẫn bấm/cuộn/mở trang được** (đo bằng bộ thử tự động: click, `requestAnimationFrame`, `scrollTop`, `Goto` đều đạt). Ẩn chỉ chậm hơn ~3 lần khi đọc.
- **TikTok mất phiên khi đóng app** (Google thì giữ): tab Tài khoản bắt cookie `sessionid` qua `CookieManager`, xác thực, lưu `cookie.dat`; mỗi lần chạy `PlaywrightService.LaunchAsync` nạp lại bằng `AddCookiesAsync`. Đăng xuất TikTok = xóa `cookie.dat` + `DeleteAllCookies`. YouTube: cookie `SAPISID` cho biết đã đăng nhập (`GoogleSession` đọc bản sao cookie DB, có cờ trong bộ nhớ vì Chromium ghi cookie xuống đĩa chậm).
- **Cổng đăng nhập**: tab Unfollow chưa đăng nhập thì `RequestLogin("tiktok"|"youtube")` mở tab Tài khoản đúng phần; mỗi nền tảng độc lập, không khóa nhau.
- **Cửa sổ làm việc chạy ngầm được** (công tắc "Chạy trình duyệt ngầm", nút mắt 👁 ở thanh dưới trang Unfollow/YouTube). Đo ở 5 trạng thái (thu nhỏ, ẩn, ngoài màn hình, trong suốt): khung hình, sự kiện cuộn, IntersectionObserver, bộ đếm giờ, click, điều hướng đều giống hệt hiển thị (đang có Playwright gắn vào thì trang vẫn coi là hiển thị). Ở chế độ ngầm nút X chỉ ẩn cửa sổ; đóng thật phải gọi `BrowserWindow.CloseForReal()` (WinForms giữ lý do "người dùng đóng" sau một lần hủy đóng nên `Close()` thường bị hủy tiếp).
- **Captcha** (`ChallengeGate`, `Services/ChallengeGate.cs`): mỗi bước làm việc đi qua `RunStepAsync`/`CheckpointAsync`. Thấy thử thách (phần tử nhìn thấy có chữ captcha, câu thử thách quen thuộc, trang Google "unusual traffic") thì HIỆN cửa sổ (kể cả khung đọc nền), báo (âm thanh, nhấp nháy thanh tác vụ, bong bóng khay), CHỜ KHÔNG GIỚI HẠN (người dùng có thể vắng máy) nhắc ở 1, 2, 5, 10 phút rồi 30 phút/lần, KHÔNG bao giờ chạm vào captcha hay tải lại trang (site tự cấp thử thách mới khi hết hạn: coi là cùng một lần chờ vì ta nhận biết theo "có hộp captcha hay không"). Hết thì ẩn lại, nghỉ `RateLimitWaitSeconds`, LÀM LẠI bước bị gián đoạn (hàm bỏ theo dõi làm lại an toàn vì đọc trạng thái nút trước; hủy đăng ký YouTube đã xong thì giữ, không làm lại). **Markup captcha thật của TikTok CHƯA từng thấy**: bộ nhận biết cố ý lỏng; mỗi lần gặp tự lưu ảnh + HTML vào `debug\challenge_*` (20 lần gần nhất) và dòng vào `challenges.log`; lần gặp thật đầu tiên cần đọc những file đó để chỉnh `ChallengeDetector`.
- Công tắc **Cài đặt → Dùng trình duyệt tích hợp** (mặc định bật). Tắt = đường Chrome cũ (profile tạm cho TikTok + cookie.dat, `youtube_profile` cho YouTube). Đường Chrome là phương án dự phòng; đừng xóa trước khi người dùng thật xác nhận đường WebView2 ổn.
- **Chưa kiểm chứng trên tài khoản thật**: bấm Unfollow/Unsubscribe thật trong cửa sổ WebView2, tải danh sách 500+ kênh TikTok bằng `BrowserWindow`, máy không có WebView2 Runtime, máy sạch.
## 6. YouTube — những điều đã kiểm chứng
- Một phiên trình duyệt dùng xuyên suốt, **profile riêng lưu lâu dài** (`youtube_webview`, hoặc `youtube_profile` khi dùng Chrome), không đọc cookie của trình duyệt người dùng, không OAuth. Google cho đăng nhập và nhớ phiên. Tab 1 giữ danh sách `youtube.com/feed/channels`, tab 2 đọc ngày đăng; luôn `BringToFrontAsync` tab đang làm việc (tab nền bị Chrome làm chậm).
- Danh sách: cuộn đến khi số `ytd-channel-renderer` ổn định; tên lấy từ `ytd-channel-name #text` (textContent của `ytd-channel-name` bị lặp tên do tooltip).
- **Ngày đăng**: RSS (`feeds/videos.xml`) hiện trả 404 cho MỌI kênh (sự cố phía YouTube đã được báo cáo, không có thông báo ngừng) nên không dùng. Đọc từ tab Videos (`/videos?hl=en`, mặc định Latest): YouTube viết `13h ago`, `5d ago`, `9mo ago`, `3y ago` → `AgeText` quy ra ngày xấp xỉ.
- **Shorts**: tab Videos không liệt kê Shorts và Shorts không có ngày. Kênh có tab Shorts mà video dài ≥ 30 ngày/không có → **`IsUncertain` ("Có Shorts — chưa rõ")**, không bao giờ tự chọn/không gọi là inactive.
- **Hủy đăng ký**: Playwright từ chối bấm nút ("element is not visible") → **bấm bằng DOM `click()`** (`button.EvaluateAsync("b => b.click()")`), bấm nút xác nhận `yt-confirm-dialog-renderer #confirm-button button`, rồi **chờ phản hồi HTTP của `/subscription/unsubscribe`**; chỉ tin kết quả sau phản hồi đó (giao diện đổi nút ngay lập tức, tải lại trang quá sớm sẽ hủy request).
- **Trusted Types**: YouTube chặn chạy chuỗi lệnh động → `WaitForFunctionAsync` lỗi `EvalError`. Chỉ dùng `EvaluateAsync` kiểu hàm, tự lặp kiểm tra bằng vòng `while`.
- An toàn: hộp xác nhận (mặc định No), **tự sao lưu CSV trước mỗi lượt hủy**, kênh được bảo vệ không bao giờ bị hủy, giới hạn mỗi lượt + nhịp nghỉ lấy từ Cài đặt (`UnfollowSessionLimit`, `UnfollowDelayMinMs/MaxMs`), dừng sau 3 lỗi liên tiếp, lịch sử loại `Unsubscribe`.
- Đã chạy thật: đăng nhập + nhớ phiên, đọc danh sách, đọc ngày (7/7), hủy 1 kênh và 2 kênh (HTTP 200, kênh biến mất sau khi tải lại, CSV + cache được ghi). Chưa thử với danh sách hàng trăm kênh.

## 7. Bẫy khi làm việc với Claude / công cụ
- **Ứng dụng Claude (MSIX) thấy bản sao CŨ của một số file trong `%APPDATA%\TikTool`** (`session_resume.dat`, `history.dat`, `tiktool_session*.log`...). Đừng kết luận "ghi không được"/"bị Kaspersky chặn": ghi chú Kaspersky cũ rất có thể là hiện tượng này. Muốn đọc file thật, nhờ người dùng chạy trong terminal của họ: `Copy-Item "$env:APPDATA\TikTool\<file>" D:\code\frm_tiktool\TikTool\bin\<tên> -Force`. File MỚI (`logs\`, `avatars\`...) đọc đúng. Log chẩn đoán đọc từ `logs\session_*.log`.
- Hook của phiên worktree từng chặn Edit/Write vào thư mục chính; ghi file bằng PowerShell khi người dùng đã cho phép.
- `Process.Start` với Playwright: Chrome do tool mở phải được đóng bằng CDP `Browser.close` (không kill) để profile lưu cookie đúng.

## 8. Quy ước
- Async đến cùng, `CancellationToken` xuyên suốt, không `.Result/.Wait()`. Cập nhật UI từ luồng nền qua `BeginInvoke`.
- Log qua `LogPanel.Log(...)` (tự ghi cả vào file phiên qua `AppLog`). Service ném exception có nội dung đã qua `T()`; trường hợp cần phản ứng riêng thì dùng **loại exception riêng** (`CookieFileLockedException`), không tìm chữ trong thông báo.
- Page bắt lỗi và log, không popup trừ khi cần quyết định của người dùng (hộp xác nhận hủy, tiếp tục phiên cũ, tự đóng trình duyệt giữ khóa cookie).
- Commit không có dòng `Co-Authored-By`; PR không có chữ ký công cụ (yêu cầu của chủ dự án).
- Không sửa bừa: `PlaywrightService.cs`, `CookieService.cs` (DPAPI), thứ tự trong `MainForm`, `selectors.json` (tăng `Version` khi sửa).

## 9. Việc còn lại (theo ưu tiên)
1. Người dùng thử thật: unfollow nhiều kênh TikTok qua hộp xác nhận; "Kiểm tra ngày" vượt 36 kênh; YouTube (bảo vệ kênh, tìm kiếm, xuất CSV, lịch sử).
2. 23 kênh TikTok không được API trả (thử xin trang kế tiếp bằng `fetch` trong ngữ cảnh trang — chưa chắc).
3. App icon `.ico`; đóng gói để người khác dùng (file chạy độc lập + hướng dẫn cài).
4. Gộp mã dùng chung giữa hai trang (dòng kênh, bộ lọc) nếu thêm nền tảng thứ ba.
5. `LOGIC_REVIEW.md` là bản rà soát cũ, nhiều mục đã xử lý; đối chiếu trước khi tin.