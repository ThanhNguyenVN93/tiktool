using TikTool.Forms;

namespace TikTool;

/// <summary>Shown during startup while MainForm's services are initialising.
/// Caller updates <see cref="Status"/> to show progress; call <see cref="CloseWhenReady"/>
/// once MainForm is ready to be displayed.</summary>
internal sealed class SplashForm : Form
{
    private readonly Label _lblStatus;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Theme.Panel;
        Size            = new Size(360, 200);
        ShowInTaskbar   = false;
        TopMost         = true;

        // — Logo / tên app —
        var lblApp = new Label
        {
            Text      = "Social Toolkit",
            Font      = new Font("Segoe UI", 28, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize  = true,
            Location  = new Point(0, 48),
        };
        lblApp.Left = (Width - lblApp.PreferredWidth) / 2;

        var lblSub = new Label
        {
            Text      = "Download & clean up your social accounts",
            Font      = new Font("Segoe UI", 9),
            ForeColor = Theme.TextMuted,
            AutoSize  = true,
            Location  = new Point(0, lblApp.Bottom + 6),
        };
        lblSub.Left = (Width - lblSub.PreferredWidth) / 2;

        // — Dòng trạng thái —
        _lblStatus = new Label
        {
            Text      = T("Đang khởi động...", "Starting..."),
            Font      = new Font("Segoe UI", 8),
            ForeColor = Theme.TextMuted,
            AutoSize  = true,
            Location  = new Point(0, Height - 30),
        };
        _lblStatus.Left = (Width - _lblStatus.PreferredWidth) / 2;

        Controls.AddRange(new Control[] { lblApp, lblSub, _lblStatus });

        // Viền mỏng
        Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    public string Status
    {
        set
        {
            if (InvokeRequired) { Invoke(() => Status = value); return; }
            _lblStatus.Text  = value;
            _lblStatus.Left  = (Width - _lblStatus.PreferredWidth) / 2;
        }
    }

    /// <summary>Fades out and closes the splash, then shows <paramref name="main"/>.</summary>
    public void CloseWhenReady(Form main)
    {
        if (InvokeRequired) { Invoke(() => CloseWhenReady(main)); return; }
        main.Show();
        Close();
    }
}
