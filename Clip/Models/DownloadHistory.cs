using System.Collections.ObjectModel;
using System.Text.Json;

namespace Clip.Models;

public sealed class DownloadHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ObservableCollection<DownloadHistoryEntry> Items { get; } = [];

    public static DownloadHistory Load(string path)
    {
        var history = new DownloadHistory();
        if (!File.Exists(path))
        {
            return history;
        }

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<DownloadHistoryEntry>>(json, JsonOptions) ?? [];
            foreach (var entry in entries.OrderByDescending(item => item.CompletedAt))
            {
                history.Items.Add(entry);
            }
        }
        catch
        {
            // A broken history file should never block downloads.
        }

        return history;
    }

    public void Add(DownloadHistoryEntry entry)
    {
        Items.Insert(0, entry);
        Save(ClipConstants.HistoryPath);
    }

    public void Clear()
    {
        Items.Clear();
        Save(ClipConstants.HistoryPath);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ClipConstants.AppDataDirectory);
        var json = JsonSerializer.Serialize(Items, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed record DownloadHistoryEntry(
    string Title,
    string Url,
    Platform Platform,
    string Format,
    string Resolution,
    string FilePath,
    DateTimeOffset CompletedAt);
