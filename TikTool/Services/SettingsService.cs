using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>Loads/saves <see cref="AppSettings"/> at %APPDATA%\TikTool\settings.dat. One
/// instance is owned by MainForm and shared (constructor-injected) with every page that reads
/// or edits settings, so a save from SettingsPage is immediately visible everywhere else.</summary>
public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            var path = AppPaths.SettingsFile;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    Current = settings;
                }
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        var tmp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
    }
}
