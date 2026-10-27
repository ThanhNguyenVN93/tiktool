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
            T("Cách tải video TikTok & Instagram Reels", "How to download TikTok & Instagram Reels"),
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

        flow.Controls.Add(BuildSection("🍪", T("Tab Cookie — Xác thực tài khoản", "Cookie Tab — Account Auth"),
            T("Cookie là gì và tại sao cần?", "What is a cookie and why is it needed?"),
            new[]
            {
                T("Cookie cho phép Social Toolkit truy cập danh sách Following và thực hiện Bỏ theo dõi với quyền tài khoản của bạn.",
                  "The cookie lets Social Toolkit access your Following list and perform unfollows on your behalf."),
                T("— Phương thức 1 (Tự động): Social Toolkit đọc cookie thẳng từ Chrome/Edge đang đăng nhập. Nhanh nhất.",
                  "— Method 1 (Auto): Social Toolkit reads cookies directly from a logged-in Chrome/Edge. Fastest."),
                T("— Phương thức 2 (Dán thủ công): Mở DevTools (F12) → Network → chọn request bất kỳ → copy header Cookie → dán vào ô.",
                  "— Method 2 (Manual paste): Open DevTools (F12) → Network → pick any request → copy Cookie header → paste it in."),
                T("— Phương thức 3 (File): Xuất cookie bằng tiện ích 'Cookie-Editor', lưu file JSON, chọn file đó.",
                  "— Method 3 (File): Export cookies with the 'Cookie-Editor' extension, save as JSON, select that file."),
                T("Sau khi lưu cookie, bấm Kiểm tra để xác nhận cookie còn hiệu lực.",
                  "After saving, click Verify to confirm the cookie is valid."),
            }));

        flow.Controls.Add(BuildSection("🚫", T("Tab Bỏ Theo Dõi — Unfollow kênh chết", "Unfollow Tab — Dead channel cleanup"),
            T("'Kênh chết' là tài khoản có 0 video", "'Dead channel' = account with 0 videos"),
            new[]
            {
                T("1. Vào tab Cookie, lưu cookie hợp lệ trước.",
                  "1. Go to the Cookie tab and save a valid cookie first."),
                T("2. Bấm Tải lại để load danh sách Following (lần đầu có thể mất 1–3 phút).",
                  "2. Click Reload to load your Following list (first time may take 1–3 minutes)."),
                T("3. Social Toolkit tự động kiểm tra số video của từng kênh. Kênh có 0 video được đánh dấu đỏ.",
                  "3. Social Toolkit checks each channel's video count. Channels with 0 videos are marked red."),
                T("4. Chọn các kênh muốn bỏ theo dõi (hoặc Chọn tất cả kênh chết), rồi bấm Bắt đầu.",
                  "4. Select channels to unfollow (or Select all dead channels), then click Start."),
                T("5. Giới hạn mỗi phiên: mặc định 50 kênh/lần để tránh bị TikTok khoá tài khoản.",
                  "5. Session limit: default 50 per run to avoid TikTok account restrictions."),
                T("⚠️ Lần đầu dùng tab này, Chromium (~300 MB) sẽ tự tải về. Quá trình này mất vài phút.",
                  "⚠️ First use of this tab downloads Chromium (~300 MB). This takes a few minutes."),
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
                T("'Cookie không hợp lệ': Cookie hết hạn — vào tab Cookie, xóa cookie cũ và lấy lại.",
                  "'Invalid cookie': Cookie expired — go to Cookie tab, clear and re-fetch."),
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
