using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace TikTool.Browser;

internal static class BrowserCdp
{
    public static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The browser needs a moment before its debugging port answers, so retry for about ten seconds.</summary>
    public static async Task<IBrowser> ConnectAsync(IPlaywright playwright, int port, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{port}");
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500, ct);
            }
        }

        throw new InvalidOperationException(
            T($"Không kết nối được vào trình duyệt (cổng {port}). Nếu một cửa sổ trình duyệt của tool đang mở sẵn, hãy đóng nó rồi thử lại.",
              $"Could not connect to the browser (port {port}). If a browser window opened by this tool is already open, close it and try again."), last);
    }
}