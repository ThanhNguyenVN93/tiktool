using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TikTool.Forms;

namespace TikTool.Browser;

/// <summary>The window that does the work: one WebView2 for the page being driven, plus (on demand) a second control that is hidden right
/// away and used for background reading. Both come from a shared <see cref="WebViewSession"/> (same profile, same debugging port).
/// The window can be hidden: a hidden window works exactly like a visible one. In "run in the background" mode the close button only hides it,
/// so the user cannot end a running job by accident; the app closes it for real.</summary>
internal sealed class BrowserWindow : Form
{
    private readonly WebView2 _main = new() { Dock = DockStyle.Fill };
    private WebView2? _worker;
    private readonly WebViewSession _session;
    private readonly bool _background;
    private bool _allowClose;

    public bool IsOpen { get; private set; } = true;

    /// <summary>Whether the window is currently shown on screen (tracked here so other threads can read it).</summary>
    public bool IsShown { get; private set; } = true;

    /// <summary>Marker in the main control's URL so the Playwright page of THIS control can be told apart from other controls' pages.</summary>
    public string MainMarker { get; } = "tiktool-main-" + Guid.NewGuid().ToString("N");

    private BrowserWindow(WebViewSession session, string title, bool background)
    {
        _session = session;
        _background = background;
        Text = title;
        Icon = MainForm.LoadAppIcon();
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Controls.Add(_main);
        FormClosing += (_, e) =>
        {
            if (_background && !_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SetVisible(false);
            }
        };
        FormClosed += (_, _) => IsOpen = false;
    }

    /// <summary>Closes the window for real. WinForms keeps the "user closed it" reason after a cancelled close, so a plain Close() would be
    /// cancelled again in background mode.</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Do not steal focus when the window is only opened to work in the background.</summary>
    protected override bool ShowWithoutActivation => _background;

    public static async Task<BrowserWindow> OpenAsync(WebViewSession session, string title, bool startHidden = false)
    {
        var window = new BrowserWindow(session, title, startHidden);
        if (startHidden)
        {
            window.Opacity = 0; // it has to be shown once for the browser control to start; keep that invisible
        }

        window.Show();

        var env = await session.GetEnvironmentAsync();
        await window._main.EnsureCoreWebView2Async(env);
        window._main.CoreWebView2.Navigate("about:blank#" + window.MainMarker);

        if (startHidden)
        {
            window.Hide();
            window.Opacity = 1;
            window.IsShown = false;
        }

        return window;
    }

    /// <summary>Shows the window on top (also when it was minimized), or hides it. Call on the UI thread.</summary>
    public void SetVisible(bool visible)
    {
        if (!IsOpen || IsDisposed)
        {
            return;
        }

        if (visible)
        {
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            TopMost = true;
            TopMost = false;
            Activate();
        }
        else
        {
            Hide();
        }

        IsShown = visible;
    }

    /// <summary>Creates the second control (once), points it at a marker URL so the caller can find its Playwright page, then hides it.</summary>
    public async Task EnsureWorkerAsync(string marker)
    {
        if (_worker is null)
        {
            // Dock=Fill (the main control) was added first; Right-docked controls come after it.
            _worker = new WebView2 { Dock = DockStyle.Right, Width = 360 };
            Controls.Add(_worker);
            await _worker.EnsureCoreWebView2Async(await _session.GetEnvironmentAsync());
        }

        _worker.CoreWebView2.Navigate("about:blank#" + marker);
        _worker.Visible = false;
    }
}