using TikTool.Models;

namespace TikTool.Services;

internal enum UpdateStatus
{
    Idle,
    Checking,
    Available,
    Downloading,
    Ready,
    Failed,
}

/// <summary>The one place that knows the update state: the banner, the Settings page and the start-up code all read it and listen to <see cref="Changed"/>
/// (raised on any thread). Every step is logged to update.log and nothing here ever throws to its caller.</summary>
internal static class UpdateController
{
    public static UpdateStatus Status { get; private set; } = UpdateStatus.Idle;

    public static UpdateInfo? Info { get; private set; }

    public static double Progress { get; private set; }

    public static string? Error { get; private set; }

    /// <summary>Why the last check could not reach GitHub (empty when it could).</summary>
    public static string? LastCheckError { get; private set; }

    public static event Action? Changed;

    private static void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static void Log(string level, string text) => AppLog.Write(level, "[Update] " + text, LogCategory.Update);

    /// <summary>Asks GitHub for a newer version. With <paramref name="autoDownload"/> a newer version is also downloaded and staged right away.</summary>
    public static async Task<UpdateStatus> CheckAsync(bool autoDownload, CancellationToken ct = default)
    {
        if (Status is UpdateStatus.Checking or UpdateStatus.Downloading)
        {
            return Status;
        }

        Set(UpdateStatus.Checking);
        try
        {
            var info = await AppUpdater.CheckAsync(ct);
            LastCheckError = null;
            Info = info;
            Error = null;
            if (info is null)
            {
                Log("INFO", $"up to date ({AppVersion.Text})");
                Set(UpdateStatus.Idle);
                return Status;
            }

            Log("INFO", $"{info.Tag} is available (running {AppVersion.Text})");
            var staged = AppUpdater.FindStaged();
            if (staged is not null && staged.Value.Tag == info.Tag)
            {
                Set(UpdateStatus.Ready);
                return Status;
            }

            Set(UpdateStatus.Available);
            if (autoDownload)
            {
                await DownloadAsync(ct);
            }
        }
        catch (Exception ex)
        {
            LastCheckError = ex.Message;
            Log("WARN", "check failed: " + ex.Message);
            Set(UpdateStatus.Idle);
        }

        return Status;
    }

    /// <summary>Downloads and stages the update found by <see cref="CheckAsync"/>.</summary>
    public static async Task DownloadAsync(CancellationToken ct = default)
    {
        var info = Info;
        if (info is null || Status == UpdateStatus.Downloading)
        {
            return;
        }

        Progress = 0;
        Error = null;
        Set(UpdateStatus.Downloading);
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            });
            await AppUpdater.DownloadAndStageAsync(info, progress, ct);
            Log("INFO", $"{info.Tag} downloaded, checked and staged");
            Set(UpdateStatus.Ready);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log("ERROR", $"download of {info.Tag} failed: {ex.Message}");
            Set(UpdateStatus.Failed);
        }
    }

    /// <summary>Installs the staged update over the running app and starts the new copy. True when the caller should now close the app.</summary>
    public static (bool Ok, string? Error) ApplyAndRestart()
    {
        var staged = AppUpdater.FindStaged();
        if (staged is null)
        {
            return (false, "No update is staged.");
        }

        var result = AppUpdater.ApplyFiles(staged.Value.Dir, AppContext.BaseDirectory);
        if (!result.Ok)
        {
            Log("ERROR", $"installing {staged.Value.Tag} failed and was rolled back: {result.Error}");
            Error = result.Error;
            Set(UpdateStatus.Failed);
            return (false, result.Error);
        }

        Log("INFO", $"installed {staged.Value.Tag} ({result.Replaced} files replaced), restarting");
        AppUpdater.PurgeStaging();
        AppUpdater.StartNewCopy(AppContext.BaseDirectory, Environment.ProcessId);
        return (true, null);
    }

    /// <summary>"Install updates automatically": a staged update is installed the moment the app starts. True when the app was restarted and this copy must exit.</summary>
    public static bool TryApplyStagedAtStartup(AppSettings settings)
    {
        if (!settings.AutoInstallUpdates || AppUpdater.FindStaged() is null)
        {
            return false;
        }

        return ApplyAndRestart().Ok;
    }
}