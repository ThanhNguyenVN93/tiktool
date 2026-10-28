# Social Toolkit

Ứng dụng Windows giúp bạn **dọn tài khoản mạng xã hội** và **tải video**, tất cả chạy trên máy của bạn:

- **TikTok Unfollow**: tải danh sách bạn đang theo dõi, tìm các kênh *không hoạt động* (không có video, hoặc lâu không đăng) và bỏ theo dõi hàng loạt, có xác nhận.
- **YouTube Unsubscribe**: tải danh sách kênh đã đăng ký, đọc ngày đăng video mới nhất của từng kênh, tìm kênh không hoạt động và hủy đăng ký hàng loạt, có xác nhận và **tự sao lưu ra file CSV**.
- **Download**: tải video TikTok / Instagram Reels / YouTube và hơn 1000 trang khác không logo, bằng `yt-dlp`.

> English: see [README.en.md](README.en.md).

Tên hiển thị là **Social Toolkit**; tên file chạy và thư mục dữ liệu vẫn là `TikTool` (giữ nguyên để người dùng cũ không mất dữ liệu).

## 1. Cài đặt

1. Vào trang **[Releases](https://github.com/ThanhNguyenVN93/tiktool/releases)**, tải file `SocialToolkit-…-win-x64.zip`.
2. Giải nén ra một thư mục bất kỳ (ví dụ `D:\SocialToolkit`) rồi chạy **`TikTool.exe`**. Không cần cài đặt, không cần cài .NET, không cần cài Chrome.
3. Yêu cầu: **Windows 10/11 64-bit** và **WebView2 Runtime** (Windows 11 có sẵn; Windows 10 thường đã có). Nếu app báo thiếu, tải "Evergreen Bootstrapper" tại <https://developer.microsoft.com/microsoft-edge/webview2/>.
4. **Windows SmartScreen / antivirus có thể cảnh báo** vì app chưa được ký số (chữ ký số là loại chứng chỉ trả phí). Bấm *Thông tin thêm → Vẫn chạy*. Bạn có thể so mã SHA256 của file zip với mã ghi trên trang Release để chắc file không bị sửa.

## 1b. Cập nhật phiên bản

- Khi mở app, nếu có bản mới trên GitHub, đầu cửa sổ hiện một dải **"Có bản mới vX"** với các nút **Cập nhật ngay**, **Có gì mới** (mở trang Release) và **Để sau**. App chỉ gửi **một yêu cầu** tới GitHub, không gửi dữ liệu của bạn.
- **Cập nhật ngay**: app tải file zip, **kiểm tra SHA256** với file `.sha256` của bản phát hành (sai là bỏ file, không cài), giải nén an toàn, hỏi xác nhận, thay các file đã đổi rồi **tự khởi động lại**. Nếu một bước lỗi thì **hoàn tác hết**, app vẫn như cũ.
- **Tự cài** (mặc định bật, tắt được ở *Cài đặt → Cập nhật*): bản mới được tải ở nền và **cài vào lần mở app kế tiếp**.
- Dữ liệu của bạn trong `%APPDATA%\TikTool\` không bị đụng tới. `Resources\yt-dlp.exe` và `ffmpeg.exe` không bao giờ bị thay (do bạn hoặc chính yt-dlp cập nhật).
- Bản cài thường (không phải bản thử nghiệm) sẽ không bao giờ bị chuyển sang bản thử nghiệm. Muốn tự tay: tải zip mới ở trang Release và giải nén đè lên thư mục cũ.
- Lỗi cập nhật hoặc cài không được: xem `logs\update.log`.

## 2. Bắt đầu nhanh

1. Mở tab **Tài khoản**, chọn **TikTok** hoặc **YouTube**, đăng nhập ngay trong khung (mật khẩu, QR hay SMS đều được). App tự nhận khi bạn đăng nhập xong. Chỉ cần đăng nhập nền tảng bạn định dùng.
2. Vào **Bỏ theo dõi → TikTok Unfollow** hoặc **YouTube Unsubscribe** và làm theo mục 3 và 4 bên dưới.
3. Lần sau mở app là dùng được ngay, trừ khi bạn muốn đổi tài khoản (bấm **Đăng xuất / Đổi tài khoản** ở tab Tài khoản).

## 3. TikTok Unfollow

1. Bấm **Tải lại**. Một cửa sổ trình duyệt của app mở ra và tự cuộn danh sách bạn đang theo dõi (khoảng 1–3 phút với vài trăm kênh). TikTok không phải lúc nào cũng trả đủ danh sách (ví dụ 516 trên 539); bỏ theo dõi xong đợt này rồi bấm Tải lại để thử lấy tiếp.
2. Chọn **tiêu chí kênh không hoạt động**: không đăng quá 30 / 90 / 180 (mặc định) / 365 ngày, hoặc chỉ cần chưa từng đăng video (0 video). Dùng ô lọc, sắp xếp và tìm kiếm để xem danh sách.
3. Số video có ngay sau khi tải. **Ngày đăng gần nhất** phải đọc riêng bằng nút **Kiểm tra ngày** (chậm và TikTok giới hạn): mỗi đợt khoảng 40 kênh rồi TikTok chặn tạm, chờ rồi bấm lại; kết quả được lưu 30 ngày nên không phải đọc lại.
4. Bấm **Chọn không hoạt động** hoặc tích từng kênh, rồi **Bắt đầu Unfollow**. App hiện hộp xác nhận (mặc định *Không*) ghi rõ số kênh và ví dụ vài tên.
5. **An toàn**: bỏ qua bạn bè theo dõi lại bạn (có thể tắt), chuột phải vào kênh để **bảo vệ** (🔒, không bao giờ bị bỏ theo dõi), giới hạn mỗi phiên (mặc định 50) và mỗi ngày (mặc định 100), nghỉ ngẫu nhiên giữa các lượt, tự chờ khi TikTok giới hạn tần suất. Phiên dang dở được hỏi tiếp tục khi mở app lần sau.
6. Muốn theo dõi lại: tab **Lịch sử** có nút follow lại từng kênh và xuất danh sách đã bỏ theo dõi ra CSV.

## 4. YouTube Unsubscribe

1. Đăng nhập Google ở tab Tài khoản (một lần), rồi bấm **Kết nối**. App tải danh sách đăng ký và đọc ngày đăng video mới nhất của từng kênh (khoảng 1–2 giây mỗi kênh, kết quả được nhớ 7 ngày).
2. Chọn **ngưỡng không hoạt động** (mặc định 1 năm). Kênh có tab **Shorts** mà video dài đã cũ được gắn *"Có Shorts — chưa rõ"* và **không bao giờ tự được chọn**, vì Shorts không có ngày.
3. Chọn kênh (hoặc **Chọn không hoạt động**) rồi bấm **Hủy đăng ký**. App hỏi xác nhận (mặc định *Không*), **tự lưu danh sách đăng ký ra CSV** trước khi hủy (thư mục `youtube_backups`), rồi hủy từng kênh với nhịp nghỉ.
4. Chuột phải vào kênh để **bảo vệ** (🔒), mở hoặc sao chép link. Muốn đăng ký lại, dùng link trong CSV hoặc nút *Đăng ký lại* ở tab Lịch sử.

## 5. Download

Dán link (nhiều link, mỗi dòng một cái), chọn MP4 / MP3 / MKV, chọn thư mục lưu rồi bấm **Thêm**. Tiến trình hiện theo thời gian thực; lịch sử nằm ở tab Lịch sử. File `yt-dlp.exe` đã có sẵn trong thư mục `Resources`; app tự kiểm tra cập nhật khi mở (tắt được trong Cài đặt). `ffmpeg.exe` là tùy chọn (một số định dạng cần), tải ở <https://github.com/BtbN/FFmpeg-Builds/releases> và bỏ vào `Resources`.

## 6. Trình duyệt ngầm và captcha

- Công việc chạy trong cửa sổ trình duyệt của app (WebView2). Bật **Cài đặt → Chạy trình duyệt ngầm** để ẩn nó; nút **👁** ở thanh dưới của trang Bỏ theo dõi / YouTube hiện hoặc ẩn bất cứ lúc nào. Chạy ngầm hoạt động y hệt chạy hiện.
- Khi TikTok hoặc Google hiện **captcha / xác minh**, app **dừng lại**, hiện cửa sổ lên trên cùng, phát tiếng và nhấp nháy thanh tác vụ. Bạn giải trong cửa sổ đó; app tự nghỉ vài chục giây rồi làm lại bước bị gián đoạn.
- App chờ **không giới hạn** (bạn có thể vắng máy) và nhắc ở 1, 2, 5, 10 phút rồi 30 phút một lần. Bấm **Dừng** để bỏ cuộc. App không bao giờ tự bấm captcha hay tải lại trang. Mỗi lần gặp được ghi vào `challenges.log` kèm ảnh chụp để cải thiện bộ nhận biết.

## 7. Cài đặt

| Mục | Ý nghĩa |
|---|---|
| Dùng trình duyệt tích hợp | Bật (mặc định): không cần Chrome. Tắt: dùng Chrome/Edge cài sẵn |
| Chạy trình duyệt ngầm | Ẩn cửa sổ làm việc (mặc định tắt) |
| Delay giữa mỗi lượt / giới hạn mỗi phiên / mỗi ngày | Nhịp bỏ theo dõi để tránh bị TikTok giới hạn (mặc định 2–5 giây, 50, 100) |
| Bỏ qua bạn bè | Không bỏ theo dõi người đang theo dõi lại bạn (mặc định bật) |
| Tự chờ khi bị giới hạn, thời gian chờ | Mặc định bật, 90 giây; cũng là thời gian nghỉ sau khi giải captcha |
| Khởi động cùng Windows / thu nhỏ xuống khay | Tùy chọn; menu khay có đường tắt tới các tab |
| Ngôn ngữ | English (mặc định) hoặc Tiếng Việt (app tự khởi động lại) |
| Nhật ký → Mở thư mục | Mở thư mục log |
| Cập nhật | Kiểm tra bản mới khi mở app (bật), tự cài bản mới (bật), nút *Kiểm tra ngay* |

## 8. Dữ liệu lưu ở đâu, quyền riêng tư

Mọi thứ nằm trên máy bạn, trong `%APPDATA%\TikTool\`. App **không gửi dữ liệu của bạn đi đâu** (chỉ gọi mạng để tải video, kiểm tra cập nhật yt-dlp, và nói chuyện với TikTok / YouTube thay bạn).

| File / thư mục | Nội dung |
|---|---|
| `cookie.dat` | Phiên TikTok, do chính app lấy lúc bạn đăng nhập, mã hóa bằng khóa Windows của bạn (không dùng được trên máy khác) |
| `tiktok_webview\`, `youtube_webview\` | Hồ sơ trình duyệt tích hợp (Google nhớ phiên ở đây) |
| `history.dat`, `whitelist.dat`, `youtube_protected.dat` | Lịch sử, kênh được bảo vệ |
| `youtube_backups\*.csv` | Bản sao lưu danh sách đăng ký trước mỗi lần hủy |
| `settings.dat`, `session_resume.dat`, `lastpost.dat`, `youtube_ages.dat`, `avatars\` | Cài đặt, phiên dang dở, bộ nhớ đệm |
| `logs\`, `debug\` | Nhật ký và ảnh chụp khi gặp captcha |
| `update\` | Bản cập nhật đã tải và đang chờ cài (tự xóa sau khi cài) |

**App không đọc, không giải mã dữ liệu của Chrome/Edge của bạn.** Muốn xóa sạch: đăng xuất ở tab Tài khoản rồi xóa thư mục `%APPDATA%\TikTool\` khi app đã đóng.

## 9. Xử lý sự cố

| Triệu chứng | Cách xử lý |
|---|---|
| Báo thiếu WebView2 Runtime | Cài "Evergreen Bootstrapper" của Microsoft, hoặc tắt "Dùng trình duyệt tích hợp" và cài Chrome/Edge |
| Phiên TikTok hết hạn | Tab Tài khoản → Đăng xuất / Đổi tài khoản → đăng nhập lại |
| TikTok báo "Something went wrong" / danh sách rỗng | TikTok đang giới hạn tần suất: chờ 30–60 phút, tăng delay, giảm giới hạn mỗi phiên |
| Kiểm tra ngày dừng sau ~40 kênh | TikTok chặn tạm: chờ rồi bấm lại, kết quả cũ đã được lưu |
| Mở app lần hai không thấy cửa sổ mới | Đúng thiết kế: chỉ một bản chạy, bản đang chạy được đưa lên trước |
| Antivirus / SmartScreen cảnh báo | App chưa ký số; so SHA256 với trang Release, bấm Vẫn chạy |
| Cần báo lỗi | Gửi nội dung `errors.log` (xem qua trước) tại [Issues](https://github.com/ThanhNguyenVN93/tiktool/issues) |

## 10. Hạn chế đã biết

- TikTok và YouTube **đổi giao diện bất cứ lúc nào** và có thể làm một chức năng ngừng chạy; các bộ phân tích dữ liệu đã có test để phát hiện sớm.
- Danh sách TikTok có thể thiếu vài chục kênh cũ nhất (TikTok không trả).
- Ngày đăng của TikTok đọc chậm và bị giới hạn (khoảng 40 kênh mỗi đợt); của YouTube chỉ là xấp xỉ ("3y ago").
- Bộ nhận biết captcha chưa từng thấy captcha thật của TikTok nên có thể cần chỉnh sau lần gặp đầu tiên.
- Bản phát hành này mới được thử trên một máy; chưa thử trên máy sạch.

## 11. Build từ mã nguồn

```bash
dotnet build TikTool.sln
dotnet test TikTool.Tests/TikTool.Tests.csproj
dotnet run --project TikTool/TikTool.csproj
```

Cần .NET 8 SDK. Bỏ `yt-dlp.exe` (và tùy chọn `ffmpeg.exe`) vào `TikTool/Resources/` (không commit). Chi tiết kiến trúc và những bài học đã kiểm chứng nằm trong [CLAUDE.md](CLAUDE.md).

## 12. Giấy phép, lưu ý

Chưa có file giấy phép trong repo. `yt-dlp` là phần mềm riêng theo giấy phép Unlicense (<https://github.com/yt-dlp/yt-dlp>). Hãy dùng tool theo điều khoản của TikTok và YouTube; việc bỏ theo dõi hoặc hủy đăng ký hàng loạt là do bạn quyết định và chịu trách nhiệm.