using System.Collections.ObjectModel;
using Clip.Core.App;
using Clip.Core.History;
using Clip.Core.Models;
using Clip.Core.Platform;
using Clip.Core.Queue;
using Clip.Core.Services;
using Clip.Core.Tools;
using Clip.Core.YtDlp;

namespace Clip.Core.DownloadQueue;

public sealed class DownloadQueueService
{
    private readonly YtDlpService _ytDlpService;
    private readonly FFmpegService _ffmpegService;
    private readonly DownloadHistoryStore _historyStore;
    private readonly IAppSettingsProvider _settingsProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private QueueService _queueService;

    public DownloadQueueService(
        YtDlpService ytDlpService,
        FFmpegService ffmpegService,
        DownloadHistoryStore historyStore,
        IAppSettingsProvider settingsProvider,
        IUiDispatcher? uiDispatcher = null)
    {
        _ytDlpService = ytDlpService;
        _ffmpegService = ffmpegService;
        _historyStore = historyStore;
        _settingsProvider = settingsProvider;
        _uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        _queueService = CreateQueueService(settingsProvider.Current);
    }

    public ObservableCollection<DownloadItem> Items { get; } = [];

    public DownloadItem Enqueue(
        string url,
        string saveDirectory,
        string mediaMode,
        string format,
        string resolution,
        string? browserCookieSource = null)
    {
        var item = new DownloadItem
        {
            Url = url,
            SaveDirectory = saveDirectory,
            MediaMode = mediaMode,
            Format = format,
            Resolution = resolution,
            BrowserCookieSource = browserCookieSource,
            Platform = UrlDetector.DetectPlatform(url),
            Cancellation = new CancellationTokenSource()
        };
        _uiDispatcher.Invoke(() => Items.Add(item));
        return item;
    }

    public async Task AnalyzeAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Status = DownloadStatus.Analyzing;
            item.CurrentStage = "Analyzing";
            item.ErrorMessage = null;
        });

        await _queueService.AnalyzeAsync(async token =>
        {
            using var linked = CreateLinkedToken(item, token, cancellationToken);
            var result = await _ytDlpService.AnalyzeAsync(item.Url, item.BrowserCookieSource, linked.Token);
            await _uiDispatcher.InvokeAsync(() =>
            {
                item.Metadata = result.Metadata;
                item.Title = result.Metadata.DisplayTitle;
                item.Thumbnail = result.Metadata.BestThumbnail;
                item.DurationSeconds = result.Metadata.DurationSeconds;
                item.ClipRange.DurationSeconds = result.Metadata.DurationSeconds ?? 0;
                item.Status = DownloadStatus.Ready;
                item.CurrentStage = result.IsFromCache ? "Ready from metadata cache" : "Ready";
            });
        }, cancellationToken);
    }

    public async Task DownloadAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Status = DownloadStatus.Downloading;
            item.CurrentStage = "Downloading";
            item.Progress = 0;
            item.ErrorMessage = null;
        });

        try
        {
            await _queueService.DownloadAsync(async downloadToken =>
            {
                using var linked = CreateLinkedToken(item, downloadToken, cancellationToken);
                var result = await _ytDlpService.DownloadAsync(
                    new YtDlpDownloadOptions
                    {
                        Url = item.Url,
                        SaveDirectory = item.SaveDirectory,
                        MediaMode = item.MediaMode,
                        Format = item.Format,
                        Resolution = item.Resolution,
                        BrowserCookieSource = item.BrowserCookieSource
                    },
                    progress =>
                    {
                        _uiDispatcher.Post(() =>
                        {
                            if (progress.Percent is { } percent)
                            {
                                item.Progress = percent;
                            }

                            item.Speed = progress.Speed;
                            item.Eta = progress.Eta;
                            item.CurrentStage = progress.Status ?? progress.Stage;
                        });
                    },
                    linked.Token);

                var outputPath = string.IsNullOrWhiteSpace(result.OutputPath)
                    ? item.OutputFilePath
                    : result.OutputPath;
                await _uiDispatcher.InvokeAsync(() => item.OutputFilePath = outputPath);

                if (item.ClipRange.IsEnabled && outputPath is not null)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        item.Status = DownloadStatus.PostProcessing;
                        item.CurrentStage = "Trimming";
                    });
                    var originalPath = outputPath;
                    var trimOutputPath = BuildTrimOutputPath(originalPath);
                    await _queueService.RunFfmpegAsync(
                        ffmpegToken => _ffmpegService.TrimAsync(
                            originalPath,
                            trimOutputPath,
                            item.ClipRange.StartSeconds,
                            item.ClipRange.EndSeconds,
                            ffmpegToken),
                        linked.Token);

                    outputPath = trimOutputPath;
                    await _uiDispatcher.InvokeAsync(() => item.OutputFilePath = trimOutputPath);
                    if (!_settingsProvider.Current.KeepOriginalWhenClipping)
                    {
                        TryDeleteOriginalClipInput(originalPath, trimOutputPath);
                    }
                }

                await _uiDispatcher.InvokeAsync(() =>
                {
                    item.OutputFilePath = outputPath;
                    item.Progress = 100;
                    item.CompletedAt = DateTimeOffset.Now;
                    item.Status = DownloadStatus.Completed;
                    item.CurrentStage = "Completed";
                });

                if (!_settingsProvider.Current.DisableHistory)
                {
                    await AddHistoryAsync(ToHistoryEntry(item), linked.Token);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                item.Status = DownloadStatus.Cancelled;
                item.CurrentStage = "Cancelled";
            });
        }
        catch (Exception ex)
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                item.Status = DownloadStatus.Failed;
                item.CurrentStage = "Failed";
                item.ErrorMessage = ex.Message;
            });
        }
    }

    public void Cancel(DownloadItem item)
    {
        item.Cancellation?.Cancel();
        _uiDispatcher.Invoke(() =>
        {
            item.Status = DownloadStatus.Cancelled;
            item.CurrentStage = "Cancelled";
        });
    }

    public bool ApplySettings()
    {
        _settingsProvider.Current.Normalize();
        if (Items.Any(item => item.IsActive))
        {
            return false;
        }

        _queueService = CreateQueueService(_settingsProvider.Current);
        return true;
    }

    private static QueueService CreateQueueService(AppSettings settings) =>
        new(settings.MaxConcurrentDownloads, settings.MaxConcurrentMetadataAnalysis, settings.MaxConcurrentFfmpegJobs);

    private static CancellationTokenSource CreateLinkedToken(
        DownloadItem item,
        CancellationToken queueToken,
        CancellationToken externalToken)
    {
        item.Cancellation ??= new CancellationTokenSource();
        return CancellationTokenSource.CreateLinkedTokenSource(item.Cancellation.Token, queueToken, externalToken);
    }

    private static string BuildTrimOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{fileName}.clip{extension}");
    }

    private DownloadHistoryEntry ToHistoryEntry(DownloadItem item)
    {
        var settings = _settingsProvider.Current;
        return new DownloadHistoryEntry(
            item.Title,
            settings.StoreOnlyHistoryTitles ? "" : item.Url,
            item.Platform,
            settings.StoreOnlyHistoryTitles ? "" : item.Format,
            settings.StoreOnlyHistoryTitles ? "" : item.Resolution,
            settings.StoreOnlyHistoryTitles ? "" : (item.OutputFilePath ?? ""),
            item.CompletedAt ?? DateTimeOffset.Now,
            item.Status);
    }

    private async Task AddHistoryAsync(DownloadHistoryEntry entry, CancellationToken cancellationToken)
    {
        List<DownloadHistoryEntry>? snapshot = null;
        await _uiDispatcher.InvokeAsync(() =>
        {
            _historyStore.Items.Insert(0, entry);
            snapshot = _historyStore.Items.ToList();
        });

        await _historyStore.SaveSnapshotAsync(snapshot ?? [], cancellationToken);
    }

    private static void TryDeleteOriginalClipInput(string originalPath, string trimOutputPath)
    {
        if (string.Equals(originalPath, trimOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }
        }
        catch
        {
            // Deleting the original is best-effort; the clipped output is already available.
        }
    }

}
