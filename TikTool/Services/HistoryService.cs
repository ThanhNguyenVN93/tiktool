using System.Text;
using System.Text.Json;
using TikTool.Models;

namespace TikTool.Services;

/// <summary>Persists a rolling log of completed downloads/unfollows at
/// %APPDATA%\TikTool\history.json, capped at 500 entries (oldest dropped first). One instance
/// is owned by MainForm and shared with DownloadPage, UnfollowPage and HistoryPage.</summary>
public sealed class HistoryService
{
    private const int MaxItems = 500;
    private readonly object _lock = new();
    private List<HistoryItem> _items = new();

    public HistoryService()
    {
        Load();
    }

    public IReadOnlyList<HistoryItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }
    }

    public async Task AddAsync(HistoryItem item)
    {
        lock (_lock)
        {
            _items.Insert(0, item);
            if (_items.Count > MaxItems)
            {
                _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            }
        }

        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        lock (_lock)
        {
            _items.Clear();
        }

        await SaveAsync();
    }

    public string ExportCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Type,Title,Detail,Success,FileSizeBytes");
        foreach (var item in Items)
        {
            sb.AppendLine(string.Join(',',
                item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                CsvEscape(item.Type),
                CsvEscape(item.Title),
                CsvEscape(item.Detail),
                item.Success,
                item.FileSizeBytes));
        }

        return sb.ToString();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private void Load()
    {
        try
        {
            var path = AppPaths.HistoryFile;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<List<HistoryItem>>(json);
                if (items is not null)
                {
                    _items = items;
                }
            }
        }
        catch
        {
            _items = new List<HistoryItem>();
        }
    }

    private async Task SaveAsync()
    {
        List<HistoryItem> snapshot;
        lock (_lock)
        {
            snapshot = _items.ToList();
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            // Viết qua temp file rồi rename để tránh file bị corrupt nếu kill giữa chừng.
            var tmp = AppPaths.HistoryFile + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, AppPaths.HistoryFile, overwrite: true);
        }
        catch
        {
            // Ghi lỗi không được crash app — mất lịch sử không quan trọng bằng mất chức năng.
        }
    }
}
