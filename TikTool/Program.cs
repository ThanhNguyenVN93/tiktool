using TikTool.Services;

namespace TikTool;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // After an update the new copy is started while the old one is still closing: wait for it, then carry on as a normal start.
        var waitIndex = Array.IndexOf(args, "--wait-pid");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var waitPid))
        {
            AppUpdater.WaitForExit(waitPid, TimeSpan.FromSeconds(30));
        }

        // One window only: two copies would fight over the Chrome debug port and the YouTube profile. A second launch just
        // asks the running copy to come to the front, then exits.
        using var instanceLock = new Mutex(true, @"Local\SocialToolkit.SingleInstance", out var isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SocialToolkit.Show");
        if (!isFirstInstance)
        {
            showSignal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        AppLog.Initialize();
        Application.ThreadException += (_, e) => AppLog.Write("FATAL", e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("FATAL", e.ExceptionObject.ToString() ?? "");
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Write("ERROR", "Unobserved task exception: " + e.Exception);
        AppPaths.MigrateLegacyFiles();

        var settings = new SettingsService();
        LanguageService.Initialize(settings.Current.Language);

        // "Install updates automatically": an update downloaded earlier is installed now, before the app window opens. Swapping hundreds of
        // files can take a while (security software scans each one), so show the splash with a message instead of nothing.
        SplashForm? updateSplash = null;
        if (settings.Current.AutoInstallUpdates && AppUpdater.FindStaged() is not null)
        {
            updateSplash = new SplashForm();
            updateSplash.Show();
            updateSplash.Status = T("Đang cài bản cập nhật, vui lòng đợi...", "Installing the update, please wait...");
            Application.DoEvents();
        }

        var restarted = UpdateController.TryApplyStagedAtStartup(settings.Current);
        updateSplash?.Close();
        if (restarted)
        {
            return;
        }

        _ = Task.Run(() => AppUpdater.CleanupOldFiles(AppContext.BaseDirectory));

        var splash = new SplashForm();
        splash.Show();
        splash.Status = T("Đang khởi động...", "Starting...");
        Application.DoEvents(); // render splash trước khi MainForm constructor chạy

        splash.Status = T("Đang khởi tạo dịch vụ...", "Initializing services...");
        Application.DoEvents();

        var main = new Forms.MainForm();
        Browser.UiThread.Initialize(main);
        Browser.UiThread.AlertHandler = main.ShowAlert;

        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
        {
            if (main.IsHandleCreated && !main.IsDisposed)
            {
                main.BeginInvoke(main.BringToFrontFromAnotherInstance);
            }
        }, null, Timeout.Infinite, false);

        splash.CloseWhenReady(main);
        Application.Run(main);
    }
}
