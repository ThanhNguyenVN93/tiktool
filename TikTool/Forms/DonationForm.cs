using System.Diagnostics;
using Guna.UI2.WinForms;

namespace TikTool.Forms;

internal sealed class DonationForm : Form
{
    public DonationForm()
    {
        Text = T("TikTool — Đóng góp", "TikTool — Donate");
        Size = new Size(420, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Background;
        Font = Theme.FontRegular;

        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = T("Ủng hộ tác giả ☕", "Support the author ☕"),
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 16),
        };

        var sub = new Label
        {
            Text = T("Mọi khoản ủng hộ giúp duy trì và phát triển TikTool.", "Every contribution helps maintain and grow TikTool."),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 44),
        };

        var sep = new Panel { Location = new Point(0, 70), Width = 420, Height = 1, BackColor = Theme.Border };

        var momoLabel = new Label
        {
            Text = T("MoMo — quét QR bên dưới:", "MoMo — scan QR below:"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(20, 84),
        };

        // Load momo.jpg from Resources folder next to the exe
        var picBox = new PictureBox
        {
            Size = new Size(320, 320),
            Location = new Point(50, 108),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Background,
        };

        var momoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "momo.jpg");
        if (File.Exists(momoPath))
        {
            try { picBox.Image = Image.FromFile(momoPath); }
            catch { picBox.BackColor = Theme.Panel; }
        }
        else
        {
            picBox.BackColor = Theme.Panel;
            picBox.Controls.Add(new Label
            {
                Text = T("momo.jpg không tìm thấy", "momo.jpg not found"),
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(80, 148),
            });
        }

        var kofiBtn = new Guna2Button
        {
            Text = T("☕  Ko-fi — ủng hộ online", "☕  Ko-fi — donate online"),
            Font = Theme.FontMedium,
            Size = new Size(200, 36),
            Location = new Point(110, 444),
            FillColor = ColorTranslator.FromHtml("#FF5E5B"),
            ForeColor = Color.White,
            BorderRadius = 8,
            Cursor = Cursors.Hand,
        };
        kofiBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/thanhnguyen150993", UseShellExecute = true }); }
            catch { }
        };

        var closeBtn = new Guna2Button
        {
            Text = T("Đóng", "Close"),
            Font = Theme.FontMedium,
            Size = new Size(90, 32),
            Location = new Point(310, 492),
            FillColor = ColorTranslator.FromHtml("#1a1a1a"),
            ForeColor = Theme.TextSecondary,
            BorderRadius = 7,
            Cursor = Cursors.Hand,
        };
        closeBtn.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(sub);
        Controls.Add(sep);
        Controls.Add(momoLabel);
        Controls.Add(picBox);
        Controls.Add(kofiBtn);
        Controls.Add(closeBtn);
    }
}
