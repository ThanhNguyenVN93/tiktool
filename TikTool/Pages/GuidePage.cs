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
                T("⚠️ Cần cài sẵn Google Chrome (hoặc Edge) trên máy; Social Toolkit điều khiển chính trình duyệt đó, không tải thêm gì.",
                  "⚠️ Google Chrome (or Edge) must be installed; Social Toolkit drives that browser and downloads nothing extra."),
            }));

        flow.Controls.Add(BuildSection("▶️", T("Tab YouTube — Hủy đăng ký kênh không hoạt động", "YouTube Tab — Unsubscribe inactive channels"),
            T("Dùng một cửa sổ Chrome riêng của tool; bạn đăng nhập Google một lần", "Uses a separate Chrome window of the tool; you sign in to Google once"),
            new[]
            {
                T("1. Vào Bỏ theo dõi → YouTube Unsubscribe, bấm \"Kết nối\". Một cửa sổ Chrome riêng mở ra; đăng nhập Google nếu được hỏi (lần sau không cần).",
                  "1. Go to Unfollow → YouTube Unsubscribe and click \"Connect\". A separate Chrome window opens; sign in to Google if asked (not needed next time)."),
                T("2. Tool tải danh sách đăng ký rồi tự đọc ngày đăng video mới nhất của từng kênh (khoảng 1–2 giây mỗi kênh, được nhớ 7 ngày). Có thể bấm Dừng bất cứ lúc nào.",
                  "2. The tool loads your subscriptions and reads each channel's latest upload (about 1–2 s per channel, remembered for 7 days). You can press Stop at any time."),
                T("3. Kênh không hoạt động = không đăng video quá ngưỡng bạn chọn (mặc định 1 năm). Kênh có Shorts mà video dài đã cũ được ghi \"Có Shorts — chưa rõ\" vì YouTube không cho biết ngày của Shorts; tool không tự chọn các kênh này.",
                  "3. An inactive channel has not uploaded within the threshold you pick (default 1 year). A channel with old long videos but Shorts is marked \"Has Shorts — unclear\" because YouTube shows no dates for Shorts; the tool never selects those by itself."),
                T("4. Chọn kênh (hoặc \"Chọn không hoạt động\") rồi bấm Hủy đăng ký. Tool hỏi xác nhận và tự sao lưu danh sách ra file CSV trước khi hủy.",
                  "4. Select channels (or \"Select inactive\") and click Unsubscribe. The tool asks for confirmation and first backs your list up to a CSV file."),
                T("5. Chuột phải vào kênh để bảo vệ (không bao giờ bị hủy), mở hoặc sao chép link. Muốn đăng ký lại, dùng link trong CSV hoặc tab Lịch sử.",
                  "5. Right-click a channel to protect it (never unsubscribed), open it or copy its link. To subscribe again use the link in the CSV or the History tab."),
                T("🔑 Đăng xuất YouTube: Cài đặt → YouTube → Đăng xuất (xóa hồ sơ Chrome riêng của tool).",
                  "🔑 To sign out of YouTube: Settings → YouTube → Sign out (deletes the tool's own Chrome profile)."),
            }));
        flow.Controls.Add(BuildSection("⚙️", T("Cài đặt quan trọng", "Key settings"),
            T("Các tùy chọn ảnh hưởng đến hiệu năng", "Options that affect performance"),
            new[]
            {
                T("Số lượt tải song song: 2–3 là tối ưu trên mạng 4G/ADSL. Tăng cao trên mạng cáp quang.",
                  "Concurrent downloads: 2–3 is optimal on 4G/ADSL. Increase on fibre."),
                T("Delay giữa mỗi lượt bỏ theo dõi: mặc định 500–5000 ms. Giảm xuống có thể bị rate limit.",
                  "Delay between unfollows: default 500–5000 ms. Reducing it risks rate limiting."),
                T("Ưu tiên API trước Playwright: nhanh hơn nhưng TikTok có thể chặn sớm hơn.",
                  "Prefer API over browser: faster but TikTok may block sooner."),
                T("Bỏ qua bạn bè: bật lên để không vô tình unfollow người đang follow lại bạn.",
                  "Skip mutual follows: enable to avoid accidentally unfollowing people who follow you back."),
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
