using TikTool.Services;

namespace TikTool;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppLog.Initialize();
        Application.ThreadException += (_, e) => AppLog.Write("FATAL", e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("FATAL", e.ExceptionObject.ToString() ?? "");
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Write("ERROR", "Unobserved task exception: " + e.Exception);
        AppPaths.MigrateLegacyFiles();

        var settings = new SettingsService();
        LanguageService.Initialize(settings.Current.Language);

        var splash = new SplashForm();
        splash.Show();
        splash.Status = T("Đang khởi động...", "Starting...");
        Application.DoEvents(); // render splash trước khi MainForm constructor chạy

        splash.Status = T("Đang khởi tạo dịch vụ...", "Initializing services...");
        Application.DoEvents();

        var main = new Forms.MainForm();

        splash.CloseWhenReady(main);
        Application.Run(main);
    }
}
