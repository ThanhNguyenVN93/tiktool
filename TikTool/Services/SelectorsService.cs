using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

internal sealed class SelectorsService
{
    private static volatile SelectorsConfig? _cached;

    public static SelectorsConfig Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var path = AppPaths.SelectorsFile;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<SelectorsConfig>(json);
                if (config is not null)
                {
                    _cached = config;
                    return config;
                }
            }
        }
        catch
        {
            // Fall through to defaults if the file is missing or malformed.
        }

        _cached = new SelectorsConfig();
        return _cached;
    }

    /// <summary>Drops the cached config so the next <see cref="Load"/> re-reads the file —
    /// called after <see cref="UpdateService"/> overwrites selectors.json with a newer version.</summary>
    public static void Invalidate()
    {
        _cached = null;
    }
}
