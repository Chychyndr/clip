using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Clip.Core.App;
using Clip.Models;
using Clip.Services;

namespace Clip.ViewModels;

public sealed class DownloadViewModel : ObservableObject
{
    private readonly YTDLPService _ytDlpService;
    private readonly FFmpegService _ffmpegService;
    private readonly DownloadHistory _history;
    private readonly SettingsViewModel _settings;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly object _queueGate = new();
    private readonly HashSet<string> _runningAnalysis = [];
    private readonly HashSet<string> _runningDownloads = [];
    private readonly HashSet<string> _runningFfmpeg = [];
    private readonly SemaphoreSlim _ffmpegSemaphore = new(1, 1);
    private bool _isPaused;
    private string _historySearchText = "";

    public DownloadViewModel(
        YTDLPService ytDlpService,
        FFmpegService ffmpegService,
        DownloadHistory history,
        SettingsViewModel settings,
        DispatcherQueue dispatcherQueue)
    {
        _ytDlpService = ytDlpService;
        _ffmpegService = ffmpegService;
        _history = history;
        _settings = settings;
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
                    StartQueueWork();
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
    public int QueuedCount => Downloads.Count(item => item.Status is DownloadStatus.Pending or DownloadStatus.Ready);
    public bool IsBusy => ActiveCount > 0;

    public string QueueSummary
    {
        get
        {
            if (IsPaused)
            {
                return "Queue paused";
            }

            int analyzing;
            int downloading;
            int ffmpeg;
            lock (_queueGate)
            {
                analyzing = _runningAnalysis.Count;
                downloading = _runningDownloads.Count;
                ffmpeg = _runningFfmpeg.Count;
            }
            if (analyzing + downloading + ffmpeg > 0)
            {
                return $"{downloading} downloading, {analyzing} analyzing, {ffmpeg} processing";
            }

            return QueuedCount > 0 ? $"{QueuedCount} queued" : "Ready";
        }
    }

    public Task EnqueueAsync(DownloadItem item)
    {
        RunOnUi(() =>
        {
            item.Status = item.Metadata is null ? DownloadStatus.Pending : DownloadStatus.Ready;
            item.StatusText = item.Metadata is null ? "Pending analysis" : "Ready";
            HydrateFromMetadata(item);
            Downloads.Insert(0, item);
            item.PropertyChanged += OnDownloadItemChanged;
            StartQueueWork();
            RaiseQueueState();
        });

        return Task.CompletedTask;
    }

    public void TogglePause() => IsPaused = !IsPaused;

    private void StartQueueWork()
    {
        StartPendingAnalyses();
        StartReadyDownloads();
        RaiseQueueState();
    }

    private void StartPendingAnalyses()
    {
        lock (_queueGate)
        {
            while (_runningAnalysis.Count < _settings.MaxConcurrentMetadataAnalysis)
            {
                var next = Downloads.LastOrDefault(item => item.Status == DownloadStatus.Pending);
                if (next is null)
                {
                    break;
                }

                next.IsCancelled = false;
                next.Cancellation = new CancellationTokenSource();
                next.Status = DownloadStatus.Analyzing;
                next.CurrentStage = "Analysis";
                next.StatusText = "Analyzing URL";
                _runningAnalysis.Add(next.Id);
                _ = RunAnalysisAsync(next, next.Cancellation.Token);
            }
        }
    }

    private void StartReadyDownloads()
    {
        if (IsPaused)
        {
            return;
        }

        lock (_queueGate)
        {
            while (_runningDownloads.Count < _settings.MaxConcurrentDownloads)
            {
                var next = Downloads.LastOrDefault(item => item.Status == DownloadStatus.Ready);
                if (next is null)
                {
                    break;
                }

                next.IsCancelled = false;
                next.Cancellation = new CancellationTokenSource();
                next.Status = DownloadStatus.Downloading;
                next.CurrentStage = "Download";
                next.StatusText = "Starting yt-dlp";
                next.Progress = Math.Max(next.Progress, 5);
                _runningDownloads.Add(next.Id);
                _ = RunDownloadAsync(next, next.Cancellation.Token);
            }
        }
    }

    private async Task RunAnalysisAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _ytDlpService.AnalyzeAsync(item.Url, cancellationToken);
            item.Metadata = metadata;
            item.Title = metadata.DisplayTitle;
            item.Platform = URLDetector.DetectPlatform(metadata.WebpageUrl ?? item.Url);
            HydrateFromMetadata(item);
            item.Status = DownloadStatus.Ready;
            item.StatusText = metadata.IsFromCache ? "Ready - metadata cache" : "Ready";
            item.CurrentStage = "Ready";
        }
        catch (OperationCanceledException)
        {
            MarkCancelled(item);
        }
        catch (MissingBinaryException ex)
        {
            MarkFailed(item, ex.Message, "Missing required tools");
        }
        catch (Exception ex)
        {
            MarkFailed(item, ex.Message, "Analysis failed");
        }
        finally
        {
            lock (_queueGate)
            {
                _runningAnalysis.Remove(item.Id);
            }

            item.Cancellation?.Dispose();
            item.Cancellation = null;
            StartQueueWork();
        }
    }

    private async Task RunDownloadAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        var downloadSlotReleased = false;
        try
        {
            var hasClip = item.ClipRange.IsEnabled && item.ClipRange.LengthSeconds > 0;
            var hasCompression = item.UseCustomTargetSize;
            var downloadEnd = hasClip && hasCompression
                ? 76
                : hasClip
                    ? 84
                    : hasCompression
                        ? 82
                        : 98;

            var outputPath = await RunStageAsync(
                item,
                5,
                downloadEnd,
                progress => _ytDlpService.DownloadAsync(item, progress, cancellationToken),
                cancellationToken);

            item.OutputFilePath = outputPath;
            item.Progress = Math.Max(item.Progress, downloadEnd);
            ReleaseDownloadSlot(item, ref downloadSlotReleased);

            if (hasClip || hasCompression)
            {
                outputPath = await RunPostProcessingAsync(item, outputPath, hasClip, hasCompression, downloadEnd, cancellationToken);
            }

            item.OutputFilePath = outputPath;
            item.CompletedAt = DateTimeOffset.Now;
            item.Progress = 100;
            item.Status = DownloadStatus.Completed;
            item.CurrentStage = "Completed";
            item.StatusText = "Completed";
            AddHistory(item, DownloadStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            MarkCancelled(item);
        }
        catch (MissingBinaryException ex)
        {
            MarkFailed(item, ex.Message, "Missing required tools");
        }
        catch (Exception ex)
        {
            MarkFailed(item, ex.Message, "Failed");
        }
        finally
        {
            ReleaseDownloadSlot(item, ref downloadSlotReleased);
            item.Cancellation?.Dispose();
            item.Cancellation = null;
            RaiseQueueState();
            StartQueueWork();
        }
    }

    private async Task<string> RunPostProcessingAsync(
        DownloadItem item,
        string inputPath,
        bool hasClip,
        bool hasCompression,
        double downloadEnd,
        CancellationToken cancellationToken)
    {
        item.Status = DownloadStatus.PostProcessing;
        item.CurrentStage = "Post-processing";
        item.StatusText = "Waiting for ffmpeg";
        await _ffmpegSemaphore.WaitAsync(cancellationToken);
        lock (_queueGate)
        {
            _runningFfmpeg.Add(item.Id);
        }

        try
        {
            var outputPath = inputPath;
            if (hasClip)
            {
                item.StatusText = _settings.TrimMode == TrimMode.Fast
                    ? "Fast trim with stream copy"
                    : "Exact trim with re-encode";
                var originalPath = outputPath;
                var clipEnd = hasCompression ? 90 : 98;
                outputPath = await RunStageAsync(
                    item,
                    downloadEnd,
                    clipEnd,
                    progress => _ffmpegService.ClipAsync(originalPath, item.ClipRange, progress, cancellationToken),
                    cancellationToken);
                if (!item.KeepOriginalWhenClipping)
                {
                    TryDeleteIntermediateFile(originalPath, outputPath);
                }

                item.Progress = Math.Max(item.Progress, clipEnd);
            }

            if (hasCompression)
            {
                item.StatusText = "Compressing to target size";
                var duration = item.ClipRange.IsEnabled ? item.ClipRange.LengthSeconds : item.Metadata?.DurationSeconds;
                var compressStart = hasClip ? 90 : downloadEnd;
                var compressionInputPath = outputPath;
                outputPath = await RunStageAsync(
                    item,
                    compressStart,
                    98,
                    progress => _ffmpegService.CompressToTargetSizeAsync(
                        compressionInputPath,
                        item.TargetSizeMegabytes,
                        duration,
                        progress,
                        cancellationToken),
                    cancellationToken);
                item.Progress = Math.Max(item.Progress, 98);
            }

            return outputPath;
        }
        finally
        {
            lock (_queueGate)
            {
                _runningFfmpeg.Remove(item.Id);
            }

            _ffmpegSemaphore.Release();
            RaiseQueueState();
        }
    }

    private void ReleaseDownloadSlot(DownloadItem item, ref bool released)
    {
        if (released)
        {
            return;
        }

        lock (_queueGate)
        {
            _runningDownloads.Remove(item.Id);
        }

        released = true;
        StartReadyDownloads();
    }

    private void Cancel(DownloadItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Status is DownloadStatus.Pending or DownloadStatus.Ready)
        {
            MarkCancelled(item);
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
        item.Speed = null;
        item.Eta = null;
        item.Status = item.Metadata is null ? DownloadStatus.Pending : DownloadStatus.Ready;
        item.StatusText = item.Metadata is null ? "Pending analysis" : "Ready";
        await EnqueueAsyncIfMissing(item);
        StartQueueWork();
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

        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false
            });
            return;
        }

        var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        var fileName = OperatingSystem.IsMacOS() ? "open" : "explorer.exe";
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            ArgumentList = { folder },
            UseShellExecute = false
        });
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
                    entry.Platform.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Status.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));

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

    private async Task<T> RunStageAsync<T>(
        DownloadItem item,
        double start,
        double end,
        Func<IProgress<DownloadProgress>, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var animationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var animationTask = AnimateProgressAsync(item, start, Math.Max(start, end - 2), animationCancellation.Token);
        try
        {
            return await action(CreateStageProgress(item, start, end));
        }
        finally
        {
            animationCancellation.Cancel();
            await IgnoreCancellationAsync(animationTask);
        }
    }

    private IProgress<DownloadProgress> CreateStageProgress(DownloadItem item, double start, double end)
    {
        return new Progress<DownloadProgress>(update =>
        {
            if (double.IsFinite(update.Percent))
            {
                var percent = Math.Clamp(update.Percent, 0, 100);
                var mapped = start + ((end - start) * percent / 100);
                item.Progress = Math.Max(item.Progress, mapped);
            }

            item.Speed = update.Speed;
            item.Eta = update.Eta;
            item.CurrentStage = update.Stage ?? item.CurrentStage;
            item.StatusText = FriendlyProgress(update.Message);
        });
    }

    private async Task AnimateProgressAsync(DownloadItem item, double start, double cap, CancellationToken cancellationToken)
    {
        RunOnUi(() => item.Progress = Math.Max(item.Progress, start));

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(350, cancellationToken);
            RunOnUi(() =>
            {
                if (!item.IsActive || item.Progress >= cap)
                {
                    return;
                }

                var remaining = cap - item.Progress;
                item.Progress = Math.Min(cap, item.Progress + Math.Max(0.2, remaining * 0.025));
            });
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MarkCancelled(DownloadItem item)
    {
        item.IsCancelled = true;
        item.Status = DownloadStatus.Cancelled;
        item.CurrentStage = "Cancelled";
        item.StatusText = "Cancelled";
        item.CompletedAt = DateTimeOffset.Now;
        AddHistory(item, DownloadStatus.Cancelled);
    }

    private void MarkFailed(DownloadItem item, string message, string statusText)
    {
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = message;
        item.CurrentStage = "Failed";
        item.StatusText = statusText;
        item.CompletedAt = DateTimeOffset.Now;
        AddHistory(item, DownloadStatus.Failed);
    }

    private void AddHistory(DownloadItem item, DownloadStatus status)
    {
        if (item.CompletedAt is null)
        {
            item.CompletedAt = DateTimeOffset.Now;
        }

        var historyEntry = new DownloadHistoryEntry(
            item.Title,
            item.Url,
            item.Platform,
            item.Format,
            item.Resolution,
            item.OutputFilePath ?? "",
            item.CompletedAt.Value,
            status);
        _history.Add(historyEntry);
        RefreshHistoryFilter();
    }

    private static void HydrateFromMetadata(DownloadItem item)
    {
        if (item.Metadata is null)
        {
            return;
        }

        item.Thumbnail = item.Metadata.BestThumbnail;
        item.DurationSeconds = item.Metadata.DurationSeconds;
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

        var text = message
            .Replace("download:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("[download]", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var parts = text.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length >= 3)
        {
            var speed = string.IsNullOrWhiteSpace(parts[1]) || parts[1] == "N/A"
                ? ""
                : $" at {parts[1]}";
            var eta = string.IsNullOrWhiteSpace(parts[2]) || parts[2] == "N/A"
                ? ""
                : $" - ETA {parts[2]}";
            return $"{parts[0]} downloaded{speed}{eta}";
        }

        return text;
    }

    private static void TryDeleteIntermediateFile(string sourcePath, string outputPath)
    {
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Info($"Could not delete intermediate file {sourcePath}: {ex.Message}");
        }
    }
}
