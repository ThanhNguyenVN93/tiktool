using TikTool.Services;

namespace TikTool;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // Initialize language BEFORE any UI is created so T() calls resolve correctly.
        var settings = new SettingsService();
        LanguageService.Initialize(settings.Current.Language);
        Application.Run(new Forms.MainForm());
    }
}
