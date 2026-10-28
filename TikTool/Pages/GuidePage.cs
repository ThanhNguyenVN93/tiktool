using Guna.UI2.WinForms;
using TikTool.Forms;

namespace TikTool.Pages;

internal sealed class GuidePage : UserControl
{
    private const int ContentWidth = 700;

    public GuidePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
    }

    private void BuildUi()
    {
        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Theme.Background,
            Padding = new Padding(20, 14, 20, 0),
        };

        header.Controls.Add(new Label
        {
            Text = T("Hướng dẫn sử dụng", "User Guide"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12),
        });
        header.Controls.Add(new Label
        {
            Text = T("Cách dùng Social Toolkit từ A–Z, câu hỏi thường gặp và cách khắc phục lỗi",
                     "How to use Social Toolkit A–Z, FAQ and troubleshooting"),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 34),
        });

        header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        return header;
    }

    private Control BuildBody()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(20, 16, 20, 24),
        };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            BackColor = Theme.Background,
            Location = new Point(20, 16),
        };

        flow.Controls.Add(BuildSection("🚀", T("Bắt đầu nhanh", "Quick start"),
            T("Năm bước đầu tiên", "The first five steps"),
            new[]
            {
                T("1. Tab Tài khoản: đăng nhập TikTok và/hoặc Google (YouTube) một lần, ngay trong cửa sổ của app. Chỉ cần đăng nhập nền tảng bạn định dùng.",
                  "1. Accounts tab: sign in to TikTok and/or Google (YouTube) once, right inside the app. You only need the platform you want to use."),
                T("2. Dọn TikTok: Bỏ theo dõi → TikTok Unfollow, bấm Tải lại, chọn các kênh không hoạt động, bấm Bắt đầu.",
                  "2. Clean up TikTok: Unfollow → TikTok Unfollow, press Reload, select the inactive channels, press Start."),
                T("3. Dọn YouTube: Bỏ theo dõi → YouTube Unsubscribe, bấm Kết nối, chờ tool đọc ngày đăng, chọn kênh rồi Hủy đăng ký (có file sao lưu CSV).",
                  "3. Clean up YouTube: Unfollow → YouTube Unsubscribe, press Connect, wait while the tool reads the upload dates, select channels and press Unsubscribe (a CSV backup is written)."),
                T("4. Tải video: tab Download, dán link TikTok / Instagram Reels / YouTube..., bấm Thêm.",
                  "4. Download videos: Download tab, paste a TikTok / Instagram Reels / YouTube... link, press Add."),
                T("5. Mọi thao tác thật (bỏ theo dõi, hủy đăng ký) đều hỏi xác nhận và mặc định chọn Không. Tool không làm gì trên tài khoản của bạn mà không hỏi.",
                  "5. Every real action (unfollow, unsubscribe) asks for confirmation with No as the default. The tool never changes your account without asking."),
            }));

        flow.Controls.Add(BuildSection("📥", T("Tab Download", "Download Tab"),
            T("Cách tải video TikTok && Instagram Reels", "How to download TikTok && Instagram Reels"),
            new[]
            {
                T("1. Dán URL video vào ô nhập liệu phía trên (hỗ trợ nhiều URL, mỗi dòng 1 link).",
                  "1. Paste a video URL into the input box (multiple URLs supported, one per line)."),
                T("2. Bấm Thêm — video xuất hiện trong hàng chờ.",
                  "2. Click Add — the video appears in the queue."),
                T("3. Bấm Tải xuống để bắt đầu. Tiến trình hiện theo thời gian thực.",
                  "3. Click Download to start. Progress updates in real time."),
                T("4. Video lưu vào thư mục bạn cài ở tab Cài đặt (mặc định Videos\\TikTool).",
                  "4. Files are saved to the folder configured in Settings (default: Videos\\TikTool)."),
                T("⚠️ Cần có yt-dlp.exe trong thư mục Resources cạnh file .exe. Tải tại github.com/yt-dlp/yt-dlp/releases.",
                  "⚠️ yt-dlp.exe must be in the Resources folder next to the .exe. Download from github.com/yt-dlp/yt-dlp/releases."),
            }));

        flow.Controls.Add(BuildSection("🍪", T("Tab Tài khoản — Đăng nhập", "Accounts Tab — Sign in"),
            T("Vì sao cần đăng nhập?", "Why sign in?"),
            new[]
            {
                T("Social Toolkit cần đăng nhập TikTok để đọc danh sách Following và Bỏ theo dõi với quyền tài khoản của bạn.",
                  "Social Toolkit needs you signed in to TikTok to read your Following list and unfollow on your behalf."),
                T("— Vào tab Tài khoản và đăng nhập TikTok ngay trong cửa sổ của app (mật khẩu, QR hay SMS đều được). App tự nhận khi bạn đăng nhập xong và nhớ cho các lần sau, không đọc gì từ Chrome của bạn.",
                  "— Open the Accounts tab and sign in to TikTok right inside the app (password, QR or SMS). The app notices when you are done and remembers it; it reads nothing from your own Chrome."),
                T("— Muốn dùng tài khoản khác: bấm Đăng xuất / Đổi tài khoản. Tài khoản TikTok của bạn trên Chrome hay điện thoại không bị ảnh hưởng.",
                  "— To use another account press Sign out / Switch account. Your TikTok account on Chrome or your phone is not affected."),
            }));

        flow.Controls.Add(BuildSection("🚫", T("Tab Bỏ Theo Dõi — Dọn kênh không hoạt động", "Unfollow Tab — Inactive channel cleanup"),
            T("'Kênh không hoạt động' là tài khoản không có video, hoặc lâu không đăng (theo ngưỡng bạn chọn)", "'Inactive channel' = an account with no videos, or none posted within the threshold you pick"),
            new[]
            {
                T("1. Vào tab Tài khoản và đăng nhập TikTok trước.",
                  "1. Go to the Accounts tab and sign in to TikTok first."),
                T("2. Bấm Tải lại để load danh sách Following (lần đầu có thể mất 1–3 phút).",
                  "2. Click Reload to load your Following list (first time may take 1–3 minutes)."),
                T("3. Social Toolkit tự động kiểm tra số video của từng kênh. Kênh không có video hoặc lâu không đăng được đánh dấu đỏ; bạn bè (theo dõi lẫn nhau) được bỏ qua mặc định.",
                  "3. Social Toolkit checks each channel's video count. Channels with no videos or no recent posts are marked red; mutual friends are skipped by default."),
                T("4. Chọn các kênh muốn bỏ theo dõi (hoặc Chọn không hoạt động / Chọn tất cả đang hiện), rồi bấm Bắt đầu.",
                  "4. Select channels to unfollow (or Select inactive / Select all shown), then click Start."),
                T("5. Giới hạn mỗi phiên: mặc định 50 kênh/lần để tránh bị TikTok khoá tài khoản.",
                  "5. Session limit: default 50 per run to avoid TikTok account restrictions."),
                T("⚠️ Mặc định dùng trình duyệt tích hợp của app (WebView2, có sẵn trên Windows 11). Nếu tắt trong Cài đặt thì cần Google Chrome (hoặc Edge) cài sẵn.",
                  "⚠️ By default the app uses its built-in browser (WebView2, included with Windows 11). If you turn that off in Settings, Google Chrome (or Edge) must be installed."),
            }));

        flow.Controls.Add(BuildSection("▶️", T("Tab YouTube — Hủy đăng ký kênh không hoạt động", "YouTube Tab — Unsubscribe inactive channels"),
            T("Dùng trình duyệt tích hợp của tool; bạn đăng nhập Google một lần ở tab Tài khoản", "Uses the tool's built-in browser; you sign in to Google once on the Accounts tab"),
            new[]
            {
                T("1. Vào tab Tài khoản → YouTube và đăng nhập Google một lần (lần sau không cần). Rồi vào Bỏ theo dõi → YouTube Unsubscribe, bấm \"Kết nối\": một cửa sổ trình duyệt của app mở ra để đọc danh sách.",
                  "1. Open the Accounts tab → YouTube and sign in to Google once (not needed next time). Then go to Unfollow → YouTube Unsubscribe and click \"Connect\": an app browser window opens to read the list."),
                T("2. Tool tải danh sách đăng ký rồi tự đọc ngày đăng video mới nhất của từng kênh (khoảng 1–2 giây mỗi kênh, được nhớ 7 ngày). Có thể bấm Dừng bất cứ lúc nào.",
                  "2. The tool loads your subscriptions and reads each channel's latest upload (about 1–2 s per channel, remembered for 7 days). You can press Stop at any time."),
                T("3. Kênh không hoạt động = không đăng video quá ngưỡng bạn chọn (mặc định 1 năm). Kênh có Shorts mà video dài đã cũ được ghi \"Có Shorts — chưa rõ\" vì YouTube không cho biết ngày của Shorts; tool không tự chọn các kênh này.",
                  "3. An inactive channel has not uploaded within the threshold you pick (default 1 year). A channel with old long videos but Shorts is marked \"Has Shorts — unclear\" because YouTube shows no dates for Shorts; the tool never selects those by itself."),
                T("4. Chọn kênh (hoặc \"Chọn không hoạt động\") rồi bấm Hủy đăng ký. Tool hỏi xác nhận và tự sao lưu danh sách ra file CSV trước khi hủy.",
                  "4. Select channels (or \"Select inactive\") and click Unsubscribe. The tool asks for confirmation and first backs your list up to a CSV file."),
                T("5. Chuột phải vào kênh để bảo vệ (không bao giờ bị hủy), mở hoặc sao chép link. Muốn đăng ký lại, dùng link trong CSV hoặc tab Lịch sử.",
                  "5. Right-click a channel to protect it (never unsubscribed), open it or copy its link. To subscribe again use the link in the CSV or the History tab."),
                T("🔑 Đăng xuất YouTube: tab Tài khoản → YouTube → Đăng xuất / Đổi tài khoản.",
                  "🔑 To sign out of YouTube: Accounts tab → YouTube → Sign out / Switch account."),
            }));
        flow.Controls.Add(BuildSection("⚙️", T("Cài đặt quan trọng", "Key settings"),
            T("Các tùy chọn ảnh hưởng đến hiệu năng", "Options that affect performance"),
            new[]
            {
                T("Số lượt tải song song: 2–3 là tối ưu trên mạng 4G/ADSL. Tăng cao trên mạng cáp quang.",
                  "Concurrent downloads: 2–3 is optimal on 4G/ADSL. Increase on fibre."),
                T("Delay giữa mỗi lượt bỏ theo dõi: mặc định 2000–5000 ms (chọn ngẫu nhiên trong khoảng này). Giảm xuống có thể bị rate limit.",
                  "Delay between unfollows: default 2000–5000 ms (random within that range). Reducing it risks rate limiting."),
                T("Giới hạn mỗi phiên (mặc định 50) và mỗi ngày (mặc định 100): để bảo vệ tài khoản. Có thể chỉnh nhưng đừng đặt quá cao.",
                  "Session limit (default 50) and daily limit (default 100): they protect your account. You can change them but do not set them too high."),
                T("Dùng trình duyệt tích hợp: bật mặc định, không cần Chrome. Tắt thì tool dùng Chrome/Edge cài sẵn trên máy.",
                  "Use the built-in browser: on by default, no Chrome needed. Turn it off to use the Chrome/Edge installed on your computer."),
                T("Chạy trình duyệt ngầm: cửa sổ làm việc ẩn đi nhưng vẫn chạy bình thường; nút 👁 ở thanh dưới của trang để hiện lại. Áp dụng cho lần chạy tiếp theo.",
                  "Run the browser in the background: the working window stays hidden but works normally; the 👁 button on the bottom bar of the page shows it again. Applies the next time you run."),
                T("Ưu tiên API trước Playwright: nhanh hơn nhưng TikTok có thể chặn sớm hơn.",
                  "Prefer API over browser: faster but TikTok may block sooner."),
                T("Bỏ qua bạn bè: bật lên để không vô tình unfollow người đang follow lại bạn.",
                  "Skip mutual follows: enable to avoid accidentally unfollowing people who follow you back."),
            }));

        flow.Controls.Add(BuildSection("🧩", T("Trình duyệt ngầm và xác minh (captcha)", "Background browser and verification (captcha)"),
            T("Khi TikTok hoặc Google hỏi bạn có phải người thật", "When TikTok or Google asks whether you are human"),
            new[]
            {
                T("Công việc chạy trong một cửa sổ trình duyệt của app. Bật Cài đặt → Chạy trình duyệt ngầm nếu muốn ẩn nó; nút 👁 ở thanh dưới của trang để hiện hoặc ẩn bất cứ lúc nào.",
                  "The work runs in a browser window of the app. Turn on Settings → Run the browser in the background to hide it; the 👁 button on the bottom bar shows or hides it at any time."),
                T("Nếu xuất hiện captcha hoặc trang xác minh, tool tự DỪNG, hiện cửa sổ lên trên cùng, phát tiếng, nhấp nháy thanh tác vụ (và hiện thông báo ở khay hệ thống nếu app đang nằm trong khay). Bạn giải xác minh ngay trong cửa sổ đó.",
                  "If a captcha or verification page appears, the tool STOPS, shows the window on top, plays a sound, flashes the taskbar button (and shows a tray balloon if the app sits in the tray). You solve it right in that window."),
                T("Giải xong, tool tự nghỉ vài chục giây (theo Thời gian đợi rate limit) rồi làm lại đúng bước bị gián đoạn. Cửa sổ tự ẩn lại nếu bạn đang để chế độ ngầm.",
                  "Once solved, the tool pauses for a while (the rate-limit wait) and repeats the interrupted step. The window hides again if you use background mode."),
                T("Tool chờ bao lâu cũng được (bạn có thể vắng máy) và nhắc ở 1, 2, 5, 10 phút rồi cứ 30 phút một lần. Bấm Dừng để bỏ cuộc. Tool không bao giờ tự bấm captcha hay tải lại trang.",
                  "The tool waits as long as needed (you may be away) and reminds you at 1, 2, 5 and 10 minutes, then every 30 minutes. Press Stop to give up. It never clicks the captcha or reloads the page by itself."),
                T("Captcha tự đổi hình khi hết hạn: cứ giải cái đang hiện. Nếu bị hỏi quá thường xuyên, hãy tăng Delay và giảm giới hạn mỗi phiên.",
                  "A captcha changes by itself when it expires: just solve the one showing. If you are asked too often, raise the delay and lower the session limit."),
            }));

        flow.Controls.Add(BuildSection("📂", T("Nhật ký (log)", "Logs"),
            T("Khi cần biết chuyện gì đã xảy ra", "When you need to know what happened"),
            new[]
            {
                T("Cài đặt → Nhật ký → Mở thư mục. Các file trong đó:",
                  "Settings → Logs → Open folder. The files in it:"),
                T("— tiktok.log, youtube.log: những gì tool đã làm với từng nền tảng (nối tiếp qua nhiều lần chạy).",
                  "— tiktok.log, youtube.log: what the tool did on each platform (kept across runs)."),
                T("— challenges.log: mọi lần gặp captcha hoặc xác minh (lúc nào, đang làm gì, chờ bao lâu). Ảnh chụp và HTML của trang đó nằm ở thư mục debug (giữ 20 lần gần nhất).",
                  "— challenges.log: every captcha or verification (when, what the tool was doing, how long it waited). A screenshot and the HTML of that page are in the debug folder (the newest 20 are kept)."),
                T("— update.log, download.log, errors.log: cập nhật, tải video, và lỗi của chính tool. session_*.log là toàn bộ một lần chạy.",
                  "— update.log, download.log, errors.log: updates, downloads and the tool's own errors. session_*.log is one whole run."),
                T("Các file chỉ nằm trên máy bạn và có thể chứa tên tài khoản: hãy xem qua trước khi gửi cho ai.",
                  "The files stay on your computer and may contain account names: look them over before sending them to anyone."),
            }));

        flow.Controls.Add(BuildSection("🔧", T("Xử lý sự cố thường gặp", "Common troubleshooting"),
            T("Những lỗi hay gặp và cách fix", "Common errors and how to fix them"),
            new[]
            {
                T("'yt-dlp.exe không tìm thấy': Copy file yt-dlp.exe vào thư mục Resources cạnh TikTool.exe.",
                  "'yt-dlp.exe not found': Copy yt-dlp.exe into the Resources folder next to TikTool.exe."),
                T("'Phiên TikTok hết hạn': vào tab Tài khoản, bấm Đăng xuất / Đổi tài khoản rồi đăng nhập lại.",
                  "'TikTok session expired': go to the Accounts tab, press Sign out / Switch account and sign in again."),
                T("'Không parse được danh sách Following': Selector TikTok có thể đã đổi. Kiểm tra file Config/selectors.json.",
                  "'Cannot parse Following list': TikTok selectors may have changed. Check Config/selectors.json."),
                T("'TikTok chặn' (rate limit): Tăng Delay, giảm Session limit, chờ 30–60 phút rồi thử lại.",
                  "'TikTok blocked' (rate limit): Increase Delay, reduce Session limit, wait 30–60 min then retry."),
                T("'Máy chưa có WebView2 Runtime': tải \"Evergreen Bootstrapper\" tại developer.microsoft.com/microsoft-edge/webview2 (Windows 11 đã có sẵn), hoặc tắt \"Dùng trình duyệt tích hợp\" trong Cài đặt và cài Chrome/Edge.",
                  "'The WebView2 Runtime is not installed': get the \"Evergreen Bootstrapper\" from developer.microsoft.com/microsoft-edge/webview2 (Windows 11 has it), or turn off \"Use the built-in browser\" in Settings and install Chrome/Edge."),
                T("Windows SmartScreen hoặc antivirus cảnh báo: app chưa được ký số nên Windows coi là \"ứng dụng không xác định\". Bấm Thông tin thêm → Vẫn chạy, và so mã SHA256 của file tải về với mã ghi ở trang Release.",
                  "Windows SmartScreen or an antivirus warns: the app is not code-signed, so Windows calls it an \"unrecognized app\". Press More info → Run anyway, and compare the SHA256 of the download with the one on the Release page."),
                T("Mở app lần hai không ra cửa sổ mới: đúng thiết kế, app chỉ chạy một bản và đưa bản đang chạy lên trước.",
                  "Opening the app a second time shows no new window: by design, only one copy runs and it brings the running one to the front."),
                T("Danh sách TikTok ít hơn số bạn đang theo dõi (ví dụ 516 trên 539): TikTok không trả hết danh sách. Bỏ theo dõi xong đợt này rồi bấm Tải lại để thử lấy tiếp.",
                  "The TikTok list is shorter than what you follow (for example 516 of 539): TikTok does not return the whole list. Finish this batch, then press Reload to try for the rest."),
                T("Báo lỗi chi tiết tại: github.com/ThanhNguyenVN93/tiktool/issues",
                  "Report detailed bugs at: github.com/ThanhNguyenVN93/tiktool/issues"),
            }));

        scroll.Controls.Add(flow);
        return scroll;
    }

    private static Control BuildSection(string icon, string title, string subtitle, string[] items)
    {
        var wrap = new Panel { Width = ContentWidth, AutoSize = true, Margin = new Padding(0, 0, 0, 20) };

        var card = new Guna2Panel
        {
            Width = ContentWidth,
            AutoSize = true,
            BorderRadius = 10,
            FillColor = Theme.Panel,
            BorderColor = Theme.Border,
            BorderThickness = 1,
            Padding = new Padding(20, 16, 20, 16),
        };

        var titlePanel = new Panel { Width = ContentWidth - 40, Height = 48, BackColor = Theme.Panel };

        var iconLbl = new Label
        {
            Text = icon,
            Font = new Font("Segoe UI Emoji", 16f),
            Size = new Size(36, 36),
            Location = new Point(0, 6),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
        };
        var titleLbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(44, 4),
            BackColor = Theme.Panel,
        };
        var subLbl = new Label
        {
            Text = subtitle,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(44, 24),
            BackColor = Theme.Panel,
        };

        titlePanel.Controls.Add(iconLbl);
        titlePanel.Controls.Add(titleLbl);
        titlePanel.Controls.Add(subLbl);

        var sep = new Panel { Width = ContentWidth - 40, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, 8, 0, 8) };

        var itemsFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = ContentWidth - 40,
            BackColor = Theme.Panel,
            Margin = new Padding(0, 6, 0, 0),
        };

        foreach (var item in items)
        {
            var lbl = new Label
            {
                Text = item,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = item.StartsWith("⚠️") ? Theme.Orange : Theme.TextSecondary,
                AutoSize = false,
                Width = ContentWidth - 40,
                AutoEllipsis = false,
                BackColor = Theme.Panel,
                Margin = new Padding(0, 0, 0, 6),
            };
            lbl.Height = TextRenderer.MeasureText(item, lbl.Font,
                new Size(lbl.Width, 0), TextFormatFlags.WordBreak).Height + 4;
            itemsFlow.Controls.Add(lbl);
        }

        var innerFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = ContentWidth - 40,
            BackColor = Theme.Panel,
        };
        innerFlow.Controls.Add(titlePanel);
        innerFlow.Controls.Add(sep);
        innerFlow.Controls.Add(itemsFlow);

        card.Controls.Add(innerFlow);
        wrap.Controls.Add(card);
        return wrap;
    }
}
