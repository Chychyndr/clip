using System.Collections.ObjectModel;
using System.Text.Json;
using Clip.Core.App;
using Clip.Core.Models;

namespace Clip.Core.History;

public sealed class DownloadHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyPath;

    public DownloadHistoryStore(string? historyPath = null)
    {
        _historyPath = historyPath ?? ClipPaths.HistoryPath;
    }

    public ObservableCollection<DownloadHistoryEntry> Items { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        if (!File.Exists(_historyPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_historyPath);
            var entries = await JsonSerializer.DeserializeAsync<List<DownloadHistoryEntry>>(stream, JsonOptions, cancellationToken) ?? [];
            foreach (var entry in entries.OrderByDescending(item => item.CompletedAt))
            {
                Items.Add(entry);
            }
        }
        catch
        {
            // A corrupt history file should never block the application.
        }
    }

    public async Task AddAsync(DownloadHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Items.Insert(0, entry);
        await SaveAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        await SaveAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath) ?? ClipPaths.AppDataDirectory);
        await using var stream = File.Create(_historyPath);
        await JsonSerializer.SerializeAsync(stream, Items, JsonOptions, cancellationToken);
    }
}
