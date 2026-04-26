using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Clip.Models;
using Clip.Services;

namespace Clip.ViewModels;

public sealed class DownloadViewModel : ObservableObject
{
    private readonly YTDLPService _ytDlpService;
    private readonly FFmpegService _ffmpegService;
    private readonly DownloadHistory _history;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly object _queueGate = new();
    private bool _isPaused;
    private string _historySearchText = "";

    public DownloadViewModel(
        YTDLPService ytDlpService,
        FFmpegService ffmpegService,
        DownloadHistory history,
        DispatcherQueue dispatcherQueue)
    {
        _ytDlpService = ytDlpService;
        _ffmpegService = ffmpegService;
        _history = history;
        _dispatcherQueue = dispatcherQueue;

        foreach (var entry in _history.Items)
        {
            FilteredHistory.Add(entry);
        }

        CancelCommand = new RelayCommand<DownloadItem>(Cancel);
        RetryCommand = new AsyncRelayCommand<DownloadItem>(RetryAsync);
        OpenFileCommand = new RelayCommand<object>(OpenFile);
        OpenFolderCommand = new RelayCommand<object>(OpenFolder);
        RedownloadCommand = new AsyncRelayCommand<DownloadHistoryEntry>(RedownloadAsync);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        TogglePauseCommand = new RelayCommand(TogglePause);
    }

    public ObservableCollection<DownloadItem> Downloads { get; } = [];
    public ObservableCollection<DownloadHistoryEntry> FilteredHistory { get; } = [];

    public RelayCommand<DownloadItem> CancelCommand { get; }
    public AsyncRelayCommand<DownloadItem> RetryCommand { get; }
    public RelayCommand<object> OpenFileCommand { get; }
    public RelayCommand<object> OpenFolderCommand { get; }
    public AsyncRelayCommand<DownloadHistoryEntry> RedownloadCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand TogglePauseCommand { get; }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(QueueSummary));
                if (!value)
                {
                    StartNextQueued();
                }
            }
        }
    }

    public string HistorySearchText
    {
        get => _historySearchText;
        set
        {
            if (SetProperty(ref _historySearchText, value))
            {
                RefreshHistoryFilter();
            }
        }
    }

    public int ActiveCount => Downloads.Count(item => item.IsActive);
    public int QueuedCount => Downloads.Count(item => item.Status == DownloadStatus.Queued);
    public bool IsBusy => ActiveCount > 0;

    public string QueueSummary
    {
        get
        {
            if (IsPaused)
            {
                return "Queue paused";
            }

            if (ActiveCount > 0)
            {
                return QueuedCount > 0 ? $"Downloading, {QueuedCount} queued" : "Downloading";
            }

            return QueuedCount > 0 ? $"{QueuedCount} queued" : "Ready";
        }
    }

    public Task EnqueueAsync(DownloadItem item)
    {
        RunOnUi(() =>
        {
            Downloads.Insert(0, item);
            item.PropertyChanged += OnDownloadItemChanged;
            StartNextQueued();
            RaiseQueueState();
        });

        return Task.CompletedTask;
    }

    public void TogglePause() => IsPaused = !IsPaused;

    private void StartNextQueued()
    {
        if (IsPaused)
        {
            return;
        }

        lock (_queueGate)
        {
            while (ActiveCount < ClipConstants.MaxConcurrentDownloads)
            {
                var next = Downloads.LastOrDefault(item => item.Status == DownloadStatus.Queued);
                if (next is null)
                {
                    break;
                }

                next.IsCancelled = false;
                next.Cancellation = new CancellationTokenSource();
                next.Status = DownloadStatus.Analyzing;
                next.Progress = 0;
                _ = RunDownloadAsync(next, next.Cancellation.Token);
            }
        }

        RaiseQueueState();
    }

    private async Task RunDownloadAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        try
        {
            await AnalyzeQueuedItemAsync(item, cancellationToken);

            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Starting yt-dlp";
            var progress = new Progress<DownloadProgress>(update =>
            {
                item.Progress = update.Percent;
                item.StatusText = FriendlyProgress(update.Message);
            });

            var outputPath = await _ytDlpService.DownloadAsync(item, progress, cancellationToken);

            if (item.ClipRange.IsEnabled && item.ClipRange.LengthSeconds > 0)
            {
                item.Status = DownloadStatus.Converting;
                item.Progress = 0;
                item.StatusText = "Clipping selection";
                outputPath = await _ffmpegService.ClipAsync(outputPath, item.ClipRange, progress, cancellationToken);
            }

            if (item.UseCustomTargetSize)
            {
                item.Status = DownloadStatus.Compressing;
                item.Progress = 0;
                item.StatusText = "Compressing to target size";
                var duration = item.ClipRange.IsEnabled ? item.ClipRange.LengthSeconds : item.Metadata?.DurationSeconds;
                outputPath = await _ffmpegService.CompressToTargetSizeAsync(
                    outputPath,
                    item.TargetSizeMegabytes,
                    duration,
                    progress,
                    cancellationToken);
            }

            item.OutputFilePath = outputPath;
            item.CompletedAt = DateTimeOffset.Now;
            item.Progress = 100;
            item.Status = DownloadStatus.Completed;
            item.StatusText = "Completed";

            var historyEntry = new DownloadHistoryEntry(
                item.Title,
                item.Url,
                item.Platform,
                item.Format,
                item.Resolution,
                outputPath,
                item.CompletedAt.Value);
            _history.Add(historyEntry);
            RefreshHistoryFilter();
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
        }
        catch (MissingBinaryException ex)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.StatusText = "Missing bundled binaries";
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.StatusText = "Failed";
        }
        finally
        {
            item.Cancellation?.Dispose();
            item.Cancellation = null;
            RaiseQueueState();
            StartNextQueued();
        }
    }

    private async Task AnalyzeQueuedItemAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        if (item.Metadata is not null)
        {
            return;
        }

        item.Status = DownloadStatus.Analyzing;
        item.StatusText = "Analyzing URL";
        var metadata = await _ytDlpService.AnalyzeAsync(item.Url, cancellationToken);
        item.Metadata = metadata;
        item.Title = metadata.DisplayTitle;
        item.Platform = URLDetector.DetectPlatform(metadata.WebpageUrl ?? item.Url);
    }

    private void Cancel(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Status == DownloadStatus.Queued)
        {
            item.IsCancelled = true;
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
            RaiseQueueState();
            return;
        }

        item.IsCancelled = true;
        item.Cancellation?.Cancel();
    }

    private async Task RetryAsync(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.ErrorMessage = null;
        item.IsCancelled = false;
        item.Progress = 0;
        item.Status = DownloadStatus.Queued;
        item.StatusText = "Queued";
        await EnqueueAsyncIfMissing(item);
        StartNextQueued();
    }

    private Task RedownloadAsync(DownloadHistoryEntry? entry)
    {
        if (entry is null)
        {
            return Task.CompletedTask;
        }

        var item = new DownloadItem
        {
            Url = entry.Url,
            Title = entry.Title,
            Platform = entry.Platform,
            Format = entry.Format,
            Resolution = entry.Resolution,
            SaveDirectory = Path.GetDirectoryName(entry.FilePath) ?? ClipConstants.DefaultDownloadDirectory
        };
        return EnqueueAsync(item);
    }

    private Task EnqueueAsyncIfMissing(DownloadItem item)
    {
        if (!Downloads.Contains(item))
        {
            return EnqueueAsync(item);
        }

        RaiseQueueState();
        return Task.CompletedTask;
    }

    private void OpenFile(object? parameter)
    {
        var path = parameter switch
        {
            DownloadItem item => item.OutputFilePath,
            DownloadHistoryEntry entry => entry.FilePath,
            string value => value,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }

    private void OpenFolder(object? parameter)
    {
        var path = parameter switch
        {
            DownloadItem item => item.OutputFilePath,
            DownloadHistoryEntry entry => entry.FilePath,
            string value => value,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false
            });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = false
            });
        }
    }

    private void ClearHistory()
    {
        _history.Clear();
        RefreshHistoryFilter();
    }

    private void RefreshHistoryFilter()
    {
        RunOnUi(() =>
        {
            FilteredHistory.Clear();
            var query = HistorySearchText.Trim();
            var entries = string.IsNullOrWhiteSpace(query)
                ? _history.Items
                : _history.Items.Where(entry =>
                    entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Platform.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in entries)
            {
                FilteredHistory.Add(entry);
            }
        });
    }

    private void OnDownloadItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Progress))
        {
            RaiseQueueState();
        }
    }

    private void RaiseQueueState()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(QueuedCount));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(QueueSummary));
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    private static string FriendlyProgress(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Working";
        }

        return message
            .Replace("download:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("[download]", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
