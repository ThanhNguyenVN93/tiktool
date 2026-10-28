namespace TikTool.Browser;

/// <summary>The services run on whatever thread the page awaited them on; WebView2 controls must be created and touched on the UI thread.</summary>
internal static class UiThread
{
    private static Control? _owner;

    public static void Initialize(Control owner) => _owner = owner;

    /// <summary>Set by the main form: a sound, a flashing taskbar button and (when the app sits in the tray) a balloon, for something that needs the user.</summary>
    public static Action<string, string>? AlertHandler { get; set; }

    public static void Alert(string title, string text)
    {
        var owner = _owner;
        var handler = AlertHandler;
        if (owner is null || handler is null)
        {
            return;
        }

        try
        {
            owner.BeginInvoke(new Action(() => handler(title, text)));
        }
        catch (Exception)
        {
            // the form is closing
        }
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var owner = _owner ?? throw new InvalidOperationException("The UI is not ready yet.");
        if (!owner.InvokeRequired)
        {
            return work();
        }

        var tcs = new TaskCompletionSource<T>();
        owner.BeginInvoke(new Action(async () =>
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }));
        return tcs.Task;
    }

    public static Task InvokeAsync(Func<Task> work) => InvokeAsync<bool>(async () =>
    {
        await work();
        return true;
    });
}