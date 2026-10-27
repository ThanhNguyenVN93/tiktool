using Guna.UI2.WinForms;
using TikTool.Forms;
using TikTool.Services;

namespace TikTool.Pages;

/// <summary>Experimental page for the future YouTube Unsubscribe feature. The probe only READS; the single write
/// action is "Test unsubscribe 1 channel", which asks for confirmation with the channel's name first.</summary>
internal sealed class YouTubeProbePage : UserControl
{
    private readonly LogPanel _log;
    private readonly Guna2Button _probeButton;
    private readonly Guna2Button _testButton;
    private readonly TextBox _filterBox;
    private CancellationTokenSource? _cts;
    private Guna2Button? _running;

    public YouTubeProbePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        _log = new LogPanel { Dock = DockStyle.Fill };
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 16), BackColor = Theme.Background };
        logHost.Controls.Add(_log);

        var header = new Panel { Dock = DockStyle.Top, Height = 200, BackColor = Theme.Background };
        header.Controls.Add(new Label
        {
            Text = "YouTube Unsubscribe — " + T("thử nghiệm", "experiment"),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 14),
        });
        header.Controls.Add(new Label
        {
            Text = T(
                "Mở YouTube trong một cửa sổ Chrome riêng (bạn đăng nhập Google một lần). 'Bắt đầu thử' chỉ ĐỌC danh sách và ngày đăng. 'Thử hủy 1 kênh' hủy đăng ký đúng MỘT kênh sau khi bạn xác nhận; có thể đăng ký lại từ trang kênh.",
                "Opens YouTube in a separate Chrome window (you sign in to Google once). 'Start test' only READS the list and upload dates. 'Test unsubscribe 1 channel' unsubscribes exactly ONE channel after you confirm; you can subscribe again from the channel's page."),
            Font = Theme.FontRegular,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(780, 62),
            Location = new Point(20, 44),
        });

        _probeButton = new Guna2Button
        {
            Text = T("▶  Bắt đầu thử (chỉ đọc)", "▶  Start test (read-only)"),
            Font = Theme.FontMedium,
            Size = new Size(210, 34),
            Location = new Point(20, 118),
            FillColor = Theme.Accent,
            ForeColor = Color.White,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _probeButton.Click += async (_, _) => await RunAsync(isUnsubscribeTest: false);

        _filterBox = new TextBox
        {
            Location = new Point(250, 125),
            Size = new Size(220, 26),
            BackColor = ColorTranslator.FromHtml("#161616"),
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Tên kênh để thử (trống = kênh đầu)", "Channel to test (empty = first)"),
        };

        _testButton = new Guna2Button
        {
            Text = T("🧪  Thử hủy 1 kênh", "🧪  Test unsubscribe 1 channel"),
            Font = Theme.FontMedium,
            Size = new Size(230, 34),
            Location = new Point(490, 118),
            FillColor = ColorTranslator.FromHtml("#2a2a2a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        _testButton.Click += async (_, _) => await RunAsync(isUnsubscribeTest: true);

        header.Controls.Add(_probeButton);
        header.Controls.Add(_filterBox);
        header.Controls.Add(_testButton);

        // Dock=Fill first, then the Top panel (see CookiePage for why).
        Controls.Add(logHost);
        Controls.Add(header);
    }

    private async Task RunAsync(bool isUnsubscribeTest)
    {
        var running = isUnsubscribeTest ? _testButton : _probeButton;

        if (_cts is not null)
        {
            if (_running == running)
            {
                _cts.Cancel();
            }
            else
            {
                // Never grey the other button out (it looked broken); just say what to do instead.
                _log.Log(T("[YouTube] Đang có một lượt chạy khác — chờ nó xong hoặc bấm Dừng ở nút đang chạy.",
                           "[YouTube] Another run is in progress — wait for it to finish or press Stop on the running button."), LogLevel.Warn);
            }

            return;
        }

        _cts = new CancellationTokenSource();
        _running = running;
        var originalText = running.Text;
        running.Text = T("⏹  Dừng", "⏹  Stop");
        var progress = new Progress<string>(m =>
            _log.Log(m, m.StartsWith('⚠') ? LogLevel.Warn : m.StartsWith('✓') ? LogLevel.Ok : LogLevel.Info));

        try
        {
            var service = new YouTubeProbeService();
            if (isUnsubscribeTest)
            {
                await service.UnsubscribeOneAsync(_filterBox.Text.Trim(), ConfirmUnsubscribe, progress, _cts.Token);
            }
            else
            {
                await service.RunAsync(progress, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Log(T("[YouTube] Đã dừng theo yêu cầu.", "[YouTube] Stopped by user."), LogLevel.Warn);
        }
        catch (Exception ex)
        {
            _log.Log($"[YouTube] {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            running.Text = originalText;
            _running = null;
        }
    }

    private bool ConfirmUnsubscribe(string channelText)
    {
        bool Ask() => MessageBox.Show(
            this,
            T($"Hủy đăng ký kênh này?\n\n{channelText}\n\nĐây là thao tác thật trên tài khoản YouTube của bạn (đăng ký lại được từ trang kênh).",
              $"Unsubscribe from this channel?\n\n{channelText}\n\nThis is a real change to your YouTube account (you can subscribe again from the channel's page)."),
            T("Social Toolkit — Thử hủy đăng ký", "Social Toolkit — Test unsubscribe"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        return InvokeRequired ? (bool)Invoke(Ask) : Ask();
    }
}