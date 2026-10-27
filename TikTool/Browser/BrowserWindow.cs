using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TikTool.Forms;

namespace TikTool.Browser;

/// <summary>The app's own browser window: one visible WebView2 for sign-in and the list, plus (on demand) a second control that is hidden
/// right away and used for background reading. Both share one profile folder and one debugging port.</summary>
internal sealed class BrowserWindow : Form
{
    private readonly WebView2 _main = new() { Dock = DockStyle.Fill };
    private WebView2? _worker;
    private CoreWebView2Environment? _env;

    public bool IsOpen { get; private set; } = true;

    private BrowserWindow(string title)
    {
        Text = title;
        Icon = MainForm.LoadAppIcon();
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Controls.Add(_main);
        FormClosed += (_, _) => IsOpen = false;
    }

    public static async Task<BrowserWindow> OpenAsync(string profileDir, int port, string title)
    {
        var window = new BrowserWindow(title);
        window.Show();

        Directory.CreateDirectory(profileDir);
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = $"--remote-debugging-port={port}" };
        window._env = await CoreWebView2Environment.CreateAsync(null, profileDir, options);
        await window._main.EnsureCoreWebView2Async(window._env);
        window._main.CoreWebView2.Navigate("about:blank");
        return window;
    }

    /// <summary>Creates the second control (once), points it at a marker URL so the caller can find its Playwright page, then hides it.</summary>
    public async Task EnsureWorkerAsync(string marker)
    {
        if (_worker is null)
        {
            // Dock=Fill (the main control) was added first; Right-docked controls come after it.
            _worker = new WebView2 { Dock = DockStyle.Right, Width = 360 };
            Controls.Add(_worker);
            await _worker.EnsureCoreWebView2Async(_env);
        }

        _worker.CoreWebView2.Navigate("about:blank#" + marker);
        _worker.Visible = false;
    }
}